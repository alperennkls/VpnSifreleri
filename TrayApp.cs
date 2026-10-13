using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VpnSifreleri
{
    internal static class Program
    {
        private const string MutexName = @"Local\VpnSifreleri";
        private const string ShowCockpitEventName = @"Local\VpnSifreleri.Kokpit";
        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        [STAThread]
        private static void Main(string[] args)
        {
            // Build step: build.ps1 embeds this .ico as the .exe icon
            if (args.Length == 2 && args[0] == "--make-icon")
            {
                AppIcon.SaveIco(args[1]);
                return;
            }

            // Crisp menu, icon, notice and cockpit on scaled displays instead of bitmap-stretched ones
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startedAtSignIn = AutoStart.WasStartedAtSignIn(args);
            bool firstInstance;
            using (new Mutex(true, MutexName, out firstInstance))
            using (EventWaitHandle showCockpit = new EventWaitHandle(false, EventResetMode.AutoReset, ShowCockpitEventName))
            {
                if (!firstInstance)
                {
                    // Opened again by hand: the running copy shows its cockpit and may come to the front for it.
                    // Started at sign-in while already running: nothing to do.
                    if (!startedAtSignIn)
                    {
                        AllowSetForegroundWindow(AsfwAny);
                        showCockpit.Set();
                    }
                    return;
                }

                PasswordStore store;
                try
                {
                    store = PasswordStore.Open();
                }
                catch (Exception ex)
                {
                    // Unreadable and could not even be set aside, or saved by a newer version: stop without touching it
                    MessageBox.Show("Şifre dosyası açılamadı. Dosyaya dokunulmadı:\n" + PasswordStore.DefaultPath + "\n\n" + ex.Message,
                        "VPN Şifreleri", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(new TrayContext(store, showCockpit, startedAtSignIn));
            }
        }
    }

    /// <summary>Tray icon whose right-click menu copies a saved password, or a user name and password.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private const string IconHint = "Saatin yanındaki kilit ikonuna sağ tıkla";

        private readonly PasswordStore store;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenu menu = new ContextMenu();
        private readonly MenuItem manageItem;
        private readonly MenuItem startupItem;
        private readonly MenuItem exitItem;
        private readonly SecureClipboard clipboard = new SecureClipboard();
        private readonly Control invoker = new Control(); // brings work from other threads onto this one
        private readonly RegisteredWaitHandle showCockpitWait;
        private readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        private MenuItem updateItem; // only once GitHub has a newer release; its Tag is that version
        private CockpitForm cockpit;

        public TrayContext(PasswordStore store, WaitHandle showCockpit, bool startedAtSignIn)
        {
            this.store = store;
            manageItem = new MenuItem("Şifreleri yönet…", delegate { OpenCockpit(); });
            manageItem.DefaultItem = true; // bold: what a double-click on the icon does
            startupItem = new MenuItem("Windows ile başlat", ToggleStartup);
            AutoStart.FollowThisExe();
            startupItem.Checked = AutoStart.IsEnabled();
            exitItem = new MenuItem("Çıkış", delegate { ExitThread(); });

            tray.Icon = AppIcon.ForTray(SystemInformation.SmallIconSize.Width);
            tray.Text = "VPN Şifreleri";
            tray.ContextMenu = menu;
            tray.DoubleClick += delegate { OpenCockpit(); };
            BuildMenu();
            store.Changed += delegate { BuildMenu(); };
            tray.Visible = true;

            invoker.CreateControl();
            showCockpitWait = ThreadPool.RegisterWaitForSingleObject(showCockpit, OnShowCockpitSignal, null, Timeout.Infinite, false);

            // Soon after starting; each answer sets when to ask again (OfferUpdate)
            updateTimer.Interval = UpdateCheck.FirstCheckMs;
            updateTimer.Tick += delegate
            {
                updateTimer.Stop();
                CheckForUpdate();
            };
            updateTimer.Start();

            TrayPin.PinWhenRegistered();
            WarnAboutOpening(store);
            // Opened by hand: say where it went, and with nothing saved yet go straight to adding. At sign-in: stay quiet.
            if (!startedAtSignIn)
            {
                Toast.Popup("VPN Şifreleri çalışıyor", IconHint, true, 4000);
                if (store.Entries.Count == 0) OpenCockpit();
            }
        }

        protected override void ExitThreadCore()
        {
            updateTimer.Dispose();
            showCockpitWait.Unregister(null);
            if (cockpit != null) cockpit.Close();
            clipboard.ClearIfUnchanged();
            clipboard.Dispose();
            tray.Visible = false;
            tray.Icon.Dispose();
            tray.Dispose();
            invoker.Dispose();
            base.ExitThreadCore();
        }

        // Entries first, in the store's alphabetical order, then the fixed items
        private void BuildMenu()
        {
            menu.MenuItems.Clear();
            menu.MenuItems.AddRange(TrayMenu.EntryItems(store.Entries, Copy));
            menu.MenuItems.Add("-");
            if (updateItem != null) menu.MenuItems.Add(updateItem);
            menu.MenuItems.Add(manageItem);
            menu.MenuItems.Add(startupItem);
            menu.MenuItems.Add(exitItem);
        }

        // The notice names the entry, never the user name or password: the screen may be shared
        private void Copy(Entry entry)
        {
            try
            {
                clipboard.Copy(entry.ClipboardText);
                Toast.Popup(TrayMenu.CopiedCaption(entry), entry.Name, true);
            }
            catch (ExternalException)
            {
                Toast.Popup("Panoya kopyalanamadı, tekrar dene", entry.Name, false);
            }
        }

        private void OpenCockpit()
        {
            if (cockpit == null)
            {
                cockpit = new CockpitForm(store);
                cockpit.FormClosed += delegate { cockpit = null; };
                cockpit.Show();
            }
            else
            {
                cockpit.HidePasswords();
                if (cockpit.WindowState == FormWindowState.Minimized) cockpit.WindowState = FormWindowState.Normal;
            }
            cockpit.Activate();
        }

        // Runs on a pool thread when the exe is opened again
        private void OnShowCockpitSignal(object state, bool timedOut)
        {
            try
            {
                invoker.BeginInvoke(new MethodInvoker(OpenCockpit));
            }
            catch (InvalidOperationException)
            {
                // Exiting: the invoker is already gone
            }
        }

        // The answer arrives on a pool thread and is brought back here; while exiting it is simply dropped
        private void CheckForUpdate()
        {
            UpdateCheck.FetchLatestAsync().ContinueWith(delegate(Task<Version> done)
            {
                try
                {
                    invoker.BeginInvoke(new Action<Version>(OfferUpdate), done.Result);
                }
                catch (InvalidOperationException)
                {
                    // Exiting: the invoker is already gone
                }
            });
        }

        private void OfferUpdate(Version latest)
        {
            updateTimer.Interval = UpdateCheck.NextCheckMs(latest);
            updateTimer.Start();
            if (!UpdateCheck.IsNewer(latest, UpdateCheck.Current)) return;
            if (updateItem != null && latest.Equals(updateItem.Tag)) return; // told once per version
            updateItem = new MenuItem("Yeni sürüm var (" + latest + ")…", delegate { OpenReleasesPage(); });
            updateItem.Tag = latest;
            BuildMenu();
            Toast.Popup("Yeni sürüm var: " + latest, "Menüden indirebilirsin", true, 6000);
        }

        private static void OpenReleasesPage()
        {
            try
            {
                Process.Start(UpdateCheck.ReleasesPage);
            }
            catch (Win32Exception)
            {
                Toast.Popup("Tarayıcı açılamadı", UpdateCheck.ReleasesPage, false, 6000);
            }
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                AutoStart.SetEnabled(!startupItem.Checked);
            }
            catch (Exception ex)
            {
                Toast.Popup("Ayar kaydedilemedi", ex.Message, false);
            }
            startupItem.Checked = AutoStart.IsEnabled();
        }

        // Says what opening did with a missing or unreadable file: set aside, restored from .bak, or both
        private static void WarnAboutOpening(PasswordStore store)
        {
            List<string> parts = new List<string>();
            if (store.UnreadableMovedTo != null)
                parts.Add("Şifre dosyası okunamadı; başka bir bilgisayardan ya da Windows kullanıcısından gelmiş olabilir.\n"
                    + "Dosya silinmedi, şu adla saklandı:\n" + store.UnreadableMovedTo);
            else if ((store.RestoredFromBackup || store.BackupMovedTo != null) && store.RecoveredFrom == null)
                parts.Add("Şifre dosyası bulunamadı.");
            if (store.RestoredFromBackup && store.RecoveredFrom == null)
                parts.Add("Kayıtlar bir önceki yedekten (sifreler.dat.bak) geri yüklendi; son değişiklik eksik olabilir.");
            if (store.BackupMovedTo != null)
                parts.Add("Yedek dosyası da okunamadı, şu adla saklandı:\n" + store.BackupMovedTo);
            if (store.RecoveredFrom != null)
                parts.Add("Uygulamanın eski bir sürümü açılmış ve yeni kayıt dosyasını okuyamadığı için kenara almış. "
                    + "Kayıtların o dosyadan geri yüklendi:\n" + store.RecoveredFrom
                    + (store.PreviousMovedTo == null ? "" : "\n\nEski sürümün açtığı liste silinmedi, şu adla saklandı:\n" + store.PreviousMovedTo)
                    + "\n\nEski exe'yi silebilirsin.");
            if (parts.Count == 0) return;
            if (store.Entries.Count == 0) parts.Add("Uygulama boş listeyle açılıyor.");
            MessageBox.Show(string.Join("\n\n", parts), "VPN Şifreleri", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
