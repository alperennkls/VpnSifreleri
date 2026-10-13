using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VpnSifreleri
{
    /// <summary>
    /// Windows 11 puts new tray icons under the ^ overflow. The first time our icon appears this turns on
    /// "show in taskbar" for it, the switch in Settings > Taskbar > Other system tray icons. An icon the user
    /// has already shown or hidden has an IsPromoted value, which is never touched, so their choice wins.
    /// </summary>
    internal static class TrayPin
    {
        private const string SettingsKey = @"Control Panel\NotifyIconSettings";
        private const int PollMs = 500, MaxPolls = 20; // Explorer registers a new icon within a few seconds

        public static void PinWhenRegistered()
        {
            if (TryPin()) return;
            int polls = 0;
            Timer timer = new Timer { Interval = PollMs };
            timer.Tick += delegate
            {
                if (!TryPin() && ++polls < MaxPolls) return;
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        /// <summary>Explorer stores paths under system folders as "{known folder id}\rest" and all others as-is.</summary>
        internal static string ResolveShellPath(string stored)
        {
            if (stored == null || !stored.StartsWith("{", StringComparison.Ordinal)) return stored;
            int end = stored.IndexOf("}\\", StringComparison.Ordinal);
            Guid folderId;
            if (end < 0 || !Guid.TryParse(stored.Substring(0, end + 1), out folderId)) return stored;

            IntPtr folder = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out folder) != 0) return stored;
                return Path.Combine(Marshal.PtrToStringUni(folder), stored.Substring(end + 2));
            }
            finally
            {
                Marshal.FreeCoTaskMem(folder); // required even when the call fails
            }
        }

        // True when nothing is left to do: our entry was found, or this Windows has no such setting
        private static bool TryPin()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (root == null) return true;
                    using (RegistryKey icon = OpenOurEntry(root, true))
                    {
                        if (icon == null) return false;
                        if (icon.GetValue("IsPromoted") == null) icon.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        return true;
                    }
                }
            }
            // Pinning is a convenience; never let it take the app down
            catch (SecurityException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch (IOException) { return true; }
        }

        // Our exe's entry among the per-icon settings, or null while Explorer has not registered the icon
        private static RegistryKey OpenOurEntry(RegistryKey root, bool writable)
        {
            foreach (string name in root.GetSubKeyNames())
            {
                using (RegistryKey icon = root.OpenSubKey(name))
                {
                    if (icon == null || !IsThisExe(icon.GetValue("ExecutablePath") as string)) continue;
                }
                return root.OpenSubKey(name, writable);
            }
            return null;
        }

        private static bool IsThisExe(string storedPath)
        {
            return storedPath != null
                && string.Equals(ResolveShellPath(storedPath), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token, out IntPtr path);
    }
}
