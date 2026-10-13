using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VpnSifreleri
{
    /// <summary>
    /// Console test runner; build.ps1 runs it and stops the build if anything fails. Everything happens in a
    /// temporary folder: the real sifreler.dat, the clipboard and the Run entry are never written.
    /// </summary>
    internal static class Tests
    {
        private static readonly byte[] TestEntropy = Encoding.UTF8.GetBytes("VpnSifreleri/test");
        private static int failures;

        [STAThread]
        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            string work = Path.Combine(Path.GetTempPath(), "VpnSifreleri-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                StoreRules(work);
                StoreFile(work);
                UnreadableFile(work);
                BackupRecovery(work);
                InvisibleCharacters(work);
                Usernames(work);
                FileFormats(work);
                OldExeAfterUpdate(work);
                ClipboardData();
                ClipboardCopy();
                MenuItems(work);
                Infrastructure(work);
                AutoStartFollows();
                Cockpit(work);
                CockpitUsernames(work);
                UpdateTags();
                SourceHygiene(root);
            }
            finally
            {
                Directory.Delete(work, true);
            }
            Console.WriteLine(failures == 0 ? "Tum testler gecti." : failures + " test basarisiz.");
            return failures == 0 ? 0 : 1;
        }

        private static void StoreRules(string work)
        {
            PasswordStore store = NewStore(work, "kurallar");
            Expect("Yeni depo bos", store.Entries.Count == 0);

            // Turkish alphabet, whatever the order of adding
            foreach (string name in new[] { "Üzüm", "İnci", "Cam", "Uğur", "açelya", "Işık", "Çelik" })
                store.Add(name, "sifre-" + name);
            Expect("Turkce alfabe sirasi", Names(store) == "açelya|Cam|Çelik|Işık|İnci|Uğur|Üzüm");

            // One entry per name, compared the Turkish way and ignoring case
            Expect("Yeni ad ekleniyor", store.Add("Köprü", "k1") == StoreResult.Ok);
            Expect("KOPRU ayni ad sayiliyor", store.Add("KÖPRÜ", "k2") == StoreResult.NameExists);
            Expect("Reddedilen ekleme sifreyi degistirmiyor", store.Find("köprü").Password == "k1");
            store.Add("İzmir Liman", "i1");
            store.Add("Irmak Kum", "p1");
            Expect("ISTANBUL = istanbul", store.Find("izmir liman") != null && store.Find("İZMİR LİMAN") != null);
            Expect("Isparta = ısparta, isparta degil", store.Find("ırmak kum") != null && store.Find("irmak kum") == null);

            // Text pasted from OneNote often brings spaces or a line break along
            Expect("Bosluk ve satir sonu kirpiliyor", store.Add("  Taşlık \t", " t-sifre\r\n") == StoreResult.Ok
                && store.Find("taşlık").Name == "Taşlık" && store.Find("Taşlık").Password == "t-sifre");
            Expect("Bos ad reddediliyor", store.Add("   ", "x") == StoreResult.NameEmpty);
            Expect("null ad reddediliyor", store.Add(null, "x") == StoreResult.NameEmpty);
            Expect("Bos sifre reddediliyor", store.Add("Yeni", "\r\n") == StoreResult.PasswordEmpty);
            Expect("Ic bosluklar korunuyor", store.Add("Ak Kıyı", "a b  c") == StoreResult.Ok && store.Find("ak kıyı").Password == "a b  c");

            // Update: password, clash, case-only rename, rename
            Expect("Sifre guncelleniyor", store.Update("köprü", "Köprü", "k9") == StoreResult.Ok && store.Find("Köprü").Password == "k9");
            Expect("Baska kayitla cakisan ad reddediliyor", store.Update("Köprü", "CAM", "k9") == StoreResult.NameExists && store.Find("Köprü") != null);
            Expect("Yalniz harf buyuklugu degisebiliyor", store.Update("Köprü", "KÖPRÜ", "k9") == StoreResult.Ok && store.Find("köprü").Name == "KÖPRÜ");
            Expect("Yeniden adlandirma", store.Update("KÖPRÜ", "Köprü Test", "k9") == StoreResult.Ok
                && store.Find("Köprü") == null && store.Find("köprü test").Password == "k9");
            Expect("Olmayan kayit guncellenmiyor", store.Update("Yok", "Yok", "x") == StoreResult.NotFound);
            Expect("Guncellemede bos sifre reddediliyor", store.Update("Cam", "Cam", " ") == StoreResult.PasswordEmpty && store.Find("Cam").Password == "sifre-Cam");
            Expect("Guncellemede bos ad reddediliyor", store.Update("Cam", "", "c2") == StoreResult.NameEmpty);

            Expect("Siliniyor", store.Remove("KÖPRÜ TEST") == StoreResult.Ok && store.Find("Köprü Test") == null);
            Expect("Olmayan kayit silinmiyor", store.Remove("Yok") == StoreResult.NotFound);

            bool readOnly = false;
            try { store.Entries.Add(new Entry("Dis", "d")); }
            catch (NotSupportedException) { readOnly = true; }
            Expect("Liste disaridan degistirilemiyor", readOnly);

            // Changed: once per successful change, never for a rejected one
            int changes = 0;
            store.Changed += delegate { changes++; };
            store.Add("Ayva", "a1");
            store.Add("ayva", "a2");
            store.Update("Ayva", "Ayva", "a3");
            store.Update("Ayva", "Cam", "a3");
            store.Remove("Ayva");
            Expect("Changed yalniz basarili degisiklikte", changes == 3);
        }

        private static void StoreFile(string work)
        {
            string path = Path.Combine(work, "dosya", "sifreler.dat");
            PasswordStore store = PasswordStore.Open(path, TestEntropy);
            Expect("Degisiklik olmadan dosya yazilmiyor", !File.Exists(path));

            // Every sample is long enough that finding it in the encrypted bytes by chance is out of the question
            string[][] samples =
            {
                new[] { "Köprü Plastik", "ĞÜŞİÖÇ-ğüşıöç-99" },
                new[] { "B&B \"Otel\" 'Yapı'", "a&b\"c'd\te;f" },
                new[] { "Emoji 🔑 Anonim", "🔒 gizli-kelime" },
            };
            store.Add(samples[0][0], samples[0][1]);
            Expect("Ilk kayitta klasor ve dosya olusuyor", File.Exists(path));
            Expect("Ilk kayitta .bak yok", !File.Exists(path + ".bak"));
            store.Add(samples[1][0], samples[1][1]);
            store.Add(samples[2][0], samples[2][1]);
            Expect("Gecici dosya kalmiyor", !File.Exists(path + ".tmp"));

            PasswordStore reopened = PasswordStore.Open(path, TestEntropy);
            Expect("Kaydet-ac: kayit sayisi", reopened.Entries.Count == samples.Length);
            foreach (string[] sample in samples)
            {
                Entry entry = reopened.Find(sample[0]);
                Expect("Kaydet-ac: kayit aynen geri geliyor", entry != null && entry.Name == sample[0] && entry.Password == sample[1]);
            }
            Expect("Kaydet-ac: sira korunuyor", Names(reopened) == Names(store));

            // Neither names nor passwords can be read from the file
            byte[] raw = File.ReadAllBytes(path);
            foreach (string[] sample in samples)
                foreach (string text in sample)
                    Expect("Dosyada duz metin yok", !Contains(raw, Encoding.UTF8.GetBytes(text)) && !Contains(raw, Encoding.Unicode.GetBytes(text)));

            // The version before each save stays as .bak
            store.Add("Dördüncü Kayıt", "d4");
            PasswordStore backup = PasswordStore.Open(path + ".bak", TestEntropy);
            Expect(".bak bir onceki surum", backup.Entries.Count == samples.Length && backup.Find("Dördüncü Kayıt") == null);

            // A save that fails changes nothing: here a file sits where the folder should be
            string blocker = Path.Combine(work, "engel");
            File.WriteAllText(blocker, "");
            PasswordStore blocked = PasswordStore.Open(Path.Combine(blocker, "sifreler.dat"), TestEntropy);
            int changes = 0;
            blocked.Changed += delegate { changes++; };
            bool threw = false;
            try { blocked.Add("Köprü", "k1"); }
            catch (IOException) { threw = true; }
            Expect("Kaydedilemeyince istisna", threw);
            Expect("Kaydedilemeyince liste ayni", blocked.Entries.Count == 0 && changes == 0);
        }

        private static void UnreadableFile(string work)
        {
            // Written by another Windows user or PC: another entropy stands in for that
            string foreign = Path.Combine(work, "yabanci", "sifreler.dat");
            PasswordStore.Open(foreign, Encoding.UTF8.GetBytes("baska-bilgisayar")).Add("Köprü", "k1");
            ExpectSetAside("Baska kullanicinin dosyasi", foreign);

            ExpectSetAside("Rastgele baytlar", WriteFile(work, "rastgele", new byte[] { 1, 2, 3, 4, 5 }));
            ExpectSetAside("Bos dosya", WriteFile(work, "bos", new byte[0]));
            ExpectSetAside("Imzasiz icerik", WriteFile(work, "imzasiz",
                ProtectedData.Protect(Encoding.UTF8.GetBytes("baska bir icerik"), TestEntropy, DataProtectionScope.CurrentUser)));

            // A file that cannot even be set aside stops the opening and stays exactly as it was
            string locked = WriteFile(work, "kilitli", new byte[] { 9, 9, 9 });
            bool threw = false;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { PasswordStore.Open(locked, TestEntropy); }
                catch (IOException) { threw = true; }
            }
            Expect("Kenara alinamayan dosyada acilis duruyor", threw);
            Expect("Kenara alinamayan dosya yerinde", File.Exists(locked) && SameBytes(File.ReadAllBytes(locked), new byte[] { 9, 9, 9 }));
        }

        private static void ExpectSetAside(string what, string path)
        {
            byte[] original = File.ReadAllBytes(path);
            PasswordStore store = PasswordStore.Open(path, TestEntropy);
            string aside = store.UnreadableMovedTo;
            Expect(what + ": liste bos", store.Entries.Count == 0);
            Expect(what + ": kenara alindi", aside != null && !File.Exists(path) && File.Exists(aside)
                && Path.GetFileName(aside).StartsWith("sifreler.dat.okunamadi-", StringComparison.Ordinal));
            store.Add("Yeni", "y1");
            Expect(what + ": eski dosyanin uzerine yazilmadi", aside != null && SameBytes(File.ReadAllBytes(aside), original));
            Expect(what + ": yeni dosya okunuyor", PasswordStore.Open(path, TestEntropy).Find("Yeni") != null);
        }

        // When the file is missing or unreadable, .bak is the last good copy: it comes back and is never overwritten
        private static void BackupRecovery(string work)
        {
            // Unreadable file, readable .bak: the list comes back from .bak and survives the next saves
            string path = Path.Combine(work, "yedek-okunur", "sifreler.dat");
            PasswordStore first = PasswordStore.Open(path, TestEntropy);
            first.Add("Açelya", "a1");
            first.Add("Köprü", "k1");
            File.WriteAllBytes(path, new byte[] { 5, 5, 5 });
            PasswordStore restored = PasswordStore.Open(path, TestEntropy);
            Expect("Bozuk dosya, okunur .bak: yedekten acildi", restored.RestoredFromBackup && Names(restored) == "Açelya");
            Expect("Bozuk dosya, okunur .bak: bozuk dosya kenarda", restored.UnreadableMovedTo != null && File.Exists(restored.UnreadableMovedTo));
            restored.Add("Yeni1", "y1");
            restored.Add("Yeni2", "y2");
            Expect("Bozuk dosya, okunur .bak: iki kayittan sonra hicbir sey kaybolmadi",
                Names(PasswordStore.Open(path, TestEntropy)) == "Açelya|Yeni1|Yeni2"
                && Names(PasswordStore.Open(path + ".bak", TestEntropy)) == "Açelya|Yeni1");

            // Missing file, readable .bak (what a crash between File.Replace's two renames leaves): no silent empty start
            string lost = Path.Combine(work, "yedek-kayip", "sifreler.dat");
            PasswordStore before = PasswordStore.Open(lost, TestEntropy);
            before.Add("Açelya", "a1");
            before.Add("Köprü", "k1");
            File.Delete(lost);
            PasswordStore back = PasswordStore.Open(lost, TestEntropy);
            Expect("Dosya yok, okunur .bak: yedekten acildi", back.RestoredFromBackup && back.UnreadableMovedTo == null && Names(back) == "Açelya");

            // Missing file, unreadable .bak: the .bak is set aside, and stays as it was after later saves
            string bad = Path.Combine(work, "yedek-bozuk", "sifreler.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(bad));
            File.WriteAllBytes(bad + ".bak", new byte[] { 6, 6, 6 });
            PasswordStore empty = PasswordStore.Open(bad, TestEntropy);
            Expect("Dosya yok, bozuk .bak: kenara alindi", empty.BackupMovedTo != null && !File.Exists(bad + ".bak")
                && Path.GetFileName(empty.BackupMovedTo).StartsWith("sifreler.dat.bak.okunamadi-", StringComparison.Ordinal));
            Expect("Dosya yok, bozuk .bak: bos liste, geri yukleme yok", empty.Entries.Count == 0 && !empty.RestoredFromBackup);
            empty.Add("Yeni1", "y1");
            empty.Add("Yeni2", "y2");
            Expect("Dosya yok, bozuk .bak: kenardaki .bak ayni kaldi",
                empty.BackupMovedTo != null && SameBytes(File.ReadAllBytes(empty.BackupMovedTo), new byte[] { 6, 6, 6 }));
        }

        // Zero-width characters come along from web pages and chat windows; at the ends they only break the login
        private static void InvisibleCharacters(string work)
        {
            PasswordStore store = NewStore(work, "gorunmez");
            Expect("Gorunmez karakterler kirpiliyor", store.Add("\u200BZeytin \uFEFF", "\u200B z1\u2060\r\n") == StoreResult.Ok
                && store.Find("Zeytin") != null && store.Find("Zeytin").Name == "Zeytin" && store.Find("Zeytin").Password == "z1");
            Expect("Ic gorunmez karakter korunuyor", store.Add("Zeytin 2", "a\u200Bb") == StoreResult.Ok && store.Find("Zeytin 2").Password == "a\u200Bb");
        }

        private static void Usernames(string work)
        {
            string path = Path.Combine(work, "kullanici", "sifreler.dat");
            PasswordStore store = PasswordStore.Open(path, TestEntropy);
            Expect("SAP kaydi kullanici adiyla ekleniyor", store.Add("Kartal SAP PRD", "  kullanıcı1 \r\n", "test123") == StoreResult.Ok);
            Entry sap = store.Find("kartal sap prd");
            Expect("Kullanici adi kirpiliyor", sap != null && sap.Username == "kullanıcı1");
            Expect("SAP kaydi iki satir kopyalaniyor", sap != null && sap.ClipboardText == "kullanıcı1\r\ntest123");
            store.Add("Akasya VPN", "a-sifre-1");
            Entry vpn = store.Find("Akasya VPN");
            Expect("VPN kaydinda kullanici adi bos", vpn.Username == "" && vpn.ClipboardText == "a-sifre-1");
            Expect("Bos kullanici adi yalniz sifreyi kopyaliyor", store.Add("Bos Kullanici", "  ", "b1") == StoreResult.Ok
                && store.Find("Bos Kullanici").ClipboardText == "b1");
            Expect("Null kullanici adi bos sayiliyor", new Entry("X", null, "p").Username == "");

            // Two lines pasted into one box: a line break inside a field would end up in the copied text
            Expect("Ic satir sonu reddediliyor (kullanici)", store.Add("Iki Satir", "kullanici1\r\ntest123", "x") == StoreResult.MultiLine);
            Expect("Ic satir sonu reddediliyor (sifre)", store.Add("Iki Satir", "", "kullanici1\ntest123") == StoreResult.MultiLine);
            Expect("Ic satir sonu reddediliyor (ad)", store.Add("Iki\r\nSatir", "", "x") == StoreResult.MultiLine && store.Find("Iki Satir") == null);

            Expect("Kullanici adi guncelleniyor", store.Update("Kartal SAP PRD", "Kartal SAP PRD", "kullanıcı2", "test456") == StoreResult.Ok
                && store.Find("Kartal SAP PRD").Username == "kullanıcı2" && store.Find("Kartal SAP PRD").Password == "test456");
            Expect("Kullanicisiz guncelleme kullanici adini bosaltiyor", store.Update("Kartal SAP PRD", "Kartal SAP PRD", "test789") == StoreResult.Ok
                && store.Find("Kartal SAP PRD").Username == "");
            Expect("Guncellemede ic satir sonu reddediliyor", store.Update("Kartal SAP PRD", "Kartal SAP PRD", "a\r\nb", "x") == StoreResult.MultiLine);
            store.Update("Kartal SAP PRD", "Kartal SAP PRD", "kullanıcı3", "test789");

            PasswordStore reopened = PasswordStore.Open(path, TestEntropy);
            Expect("Kaydet-ac: kullanici adi geri geliyor", reopened.Find("Kartal SAP PRD").Username == "kullanıcı3"
                && reopened.Find("Akasya VPN").Username == "");
            Expect("Yeni dosya bicim 2", FormatVersionOf(path) == 2);
        }

        private static void FileFormats(string work)
        {
            // What version 1.0.0 wrote: no user names. It opens, and the first save moves it to format 2 keeping the old file as .bak
            string old = WriteFile(work, "bicim1", RawFile(1, new[] { "Zeytin VPN", "z1" }, new[] { "Akasya VPN", "a1" }));
            PasswordStore store = PasswordStore.Open(old, TestEntropy);
            Expect("Bicim 1 okunuyor", Names(store) == "Akasya VPN|Zeytin VPN" && store.Find("Zeytin VPN").Password == "z1"
                && store.Find("Zeytin VPN").Username == "");
            Expect("Bicim 1 acilista kenara alinmiyor", store.UnreadableMovedTo == null && !store.RestoredFromBackup);
            store.Add("Kartal SAP PRD", "kullanici1", "test123");
            Expect("Ilk kayitta bicim 2", FormatVersionOf(old) == 2);
            Expect("Bicim 1 hali .bak olarak kaliyor ve okunuyor", FormatVersionOf(old + ".bak") == 1
                && Names(PasswordStore.Open(old + ".bak", TestEntropy)) == "Akasya VPN|Zeytin VPN");


            // A newer app's file: opening stops and the file stays exactly as it was
            string newer = WriteFile(work, "bicim3", RawFile(3));
            byte[] before = File.ReadAllBytes(newer);
            bool stopped = false;
            try { PasswordStore.Open(newer, TestEntropy); }
            catch (NewerFormatException) { stopped = true; }
            Expect("Daha yeni bicim acilisi durduruyor", stopped);
            Expect("Daha yeni bicim dosyasi yerinde ve ayni", SameBytes(File.ReadAllBytes(newer), before)
                && Directory.GetFiles(Path.GetDirectoryName(newer)).Length == 1);

            // Format 0 is no format at all: unreadable, set aside as before
            ExpectSetAside("Bicim 0", WriteFile(work, "bicim0", RawFile(0)));
        }

        // Version 1.0.0 opened after the update cannot read format 2: it renames the file (and an unreadable .bak) to
        // *.okunamadi-* and starts empty, or brings back a format 1 .bak. 1.1.0 must take the newer list back.
        private static void OldExeAfterUpdate(string work)
        {
            // Two saves in 1.1.0: both the file and .bak are format 2, so 1.0.0 sets both aside
            string path = Path.Combine(work, "eski-exe", "sifreler.dat");
            PasswordStore store = PasswordStore.Open(path, TestEntropy);
            store.Add("Kartal SAP PRD", "kullanici1", "test123");
            store.Add("Akasya VPN", "a1");
            File.Move(path, path + ".okunamadi-20261013-100000");
            File.Move(path + ".bak", path + ".bak.okunamadi-20261013-100000");
            PasswordStore back = PasswordStore.Open(path, TestEntropy);
            Expect("Iki kayit sonra eski exe: kayitlar geri geliyor", Names(back) == "Akasya VPN|Kartal SAP PRD"
                && back.Find("Kartal SAP PRD").Username == "kullanici1");
            Expect("Iki kayit sonra eski exe: neyin geri geldigi biliniyor", back.RecoveredFrom != null
                && Path.GetFileName(back.RecoveredFrom) == "sifreler.dat.okunamadi-20261013-100000" && back.PreviousMovedTo == null);
            Expect("Iki kayit sonra eski exe: ikinci acilista yeni bir sey yok", File.Exists(path)
                && PasswordStore.Open(path, TestEntropy).RecoveredFrom == null);

            // One save in 1.1.0: .bak is still format 1, which 1.0.0 brings back itself after setting the new file aside
            string one = WriteFile(work, "eski-exe-tek", RawFile(1, new[] { "Zeytin VPN", "z1" }));
            File.SetLastWriteTimeUtc(one, DateTime.UtcNow.AddDays(-1));
            PasswordStore updated = PasswordStore.Open(one, TestEntropy);
            updated.Add("Kartal SAP PRD", "kullanici1", "test123");
            File.Move(one, one + ".okunamadi-20261013-110000");
            File.Move(one + ".bak", one);
            PasswordStore again = PasswordStore.Open(one, TestEntropy);
            Expect("Tek kayit sonra eski exe: daha yeni liste geri geliyor", Names(again) == "Kartal SAP PRD|Zeytin VPN");
            Expect("Tek kayit sonra eski exe: eski liste silinmeden saklaniyor", again.PreviousMovedTo != null && File.Exists(again.PreviousMovedTo)
                && Names(PasswordStore.Open(again.PreviousMovedTo, TestEntropy)) == "Zeytin VPN");

            // A file nobody here can read stays aside: only lists this version can read come back
            string foreign = Path.Combine(work, "eski-exe-yabanci", "sifreler.dat");
            PasswordStore.Open(foreign, Encoding.UTF8.GetBytes("baska-bilgisayar")).Add("Akasya VPN", "a1");
            File.Move(foreign, foreign + ".okunamadi-20261013-120000");
            PasswordStore none = PasswordStore.Open(foreign, TestEntropy);
            Expect("Okunamayan kenar dosyasi geri gelmiyor", none.Entries.Count == 0 && none.RecoveredFrom == null
                && File.Exists(foreign + ".okunamadi-20261013-120000"));
        }

        // sifreler.dat as some app version wrote it: signature, format number, entry count, then each entry's strings
        private static byte[] RawFile(int format, params string[][] entries)
        {
            MemoryStream plain = new MemoryStream();
            using (BinaryWriter w = new BinaryWriter(plain, Encoding.UTF8))
            {
                w.Write(Encoding.ASCII.GetBytes("VPNS"));
                w.Write(format);
                w.Write(entries.Length);
                foreach (string[] fields in entries)
                    foreach (string field in fields) w.Write(field);
            }
            return ProtectedData.Protect(plain.ToArray(), TestEntropy, DataProtectionScope.CurrentUser);
        }

        private static int FormatVersionOf(string path)
        {
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), TestEntropy, DataProtectionScope.CurrentUser);
            return BitConverter.ToInt32(plain, 4);
        }

        private static string WriteFile(string work, string folder, byte[] content)
        {
            string path = Path.Combine(work, folder, "sifreler.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        // Only the object handed to the clipboard is checked: the real clipboard is never touched here
        private static void ClipboardData()
        {
            DataObject data = SecureClipboard.CreateDataObject("Gizli-Şifre 1");
            Expect("Pano metni", (string)data.GetData(DataFormats.UnicodeText) == "Gizli-Şifre 1");
            Expect("Izleme disi isareti var", data.GetDataPresent(SecureClipboard.ExcludeFormat));
            Expect("Pano gecmisine alinmaz (DWORD 0)", IsDwordZero(data.GetData(SecureClipboard.HistoryFormat)));
            Expect("Buluta gonderilmez (DWORD 0)", IsDwordZero(data.GetData(SecureClipboard.CloudFormat)));
            Expect("Ayni metin temizlenir", SecureClipboard.ShouldClear("abc", "abc"));
            Expect("Sonradan kopyalanan metne dokunulmaz", !SecureClipboard.ShouldClear("abcd", "abc"));
            Expect("Bos panoya dokunulmaz", !SecureClipboard.ShouldClear(null, "abc"));
            Expect("Kopyalama yoksa temizlenmez", !SecureClipboard.ShouldClear("abc", null));
            Expect("Temizleme 30 sn sonra", SecureClipboard.ClearAfterMs == 30000);
        }

        private static bool IsDwordZero(object value)
        {
            MemoryStream stream = value as MemoryStream;
            return stream != null && SameBytes(stream.ToArray(), new byte[4]);
        }

        // A stand-in clipboard: put may place the text and then fail, like a flush that another app interrupts
        private static void ClipboardCopy()
        {
            string board = "onceki";
            int clears = 0;
            bool placeThenFail = false, failWithoutPlacing = false;
            Action<DataObject> put = delegate(DataObject data)
            {
                if (failWithoutPlacing) throw new ExternalException("pano kilitli");
                board = (string)data.GetData(DataFormats.UnicodeText);
                if (placeThenFail) throw new ExternalException("kalicilastirilamadi");
            };
            Func<string> read = delegate { return board; };
            Action clear = delegate { clears++; board = null; };

            using (SecureClipboard clipboard = new SecureClipboard(put, read, clear))
            {
                clipboard.Copy("kullanici1\r\ntest123");
                Expect("Kopyalama temizlemeyi kuruyor", clipboard.HoldsCopy && board == "kullanici1\r\ntest123");
                board = "baska bir sey";
                clipboard.ClearIfUnchanged();
                Expect("Sonradan kopyalanana dokunulmuyor", clears == 0 && !clipboard.HoldsCopy);

                placeThenFail = true;
                clipboard.Copy("sifre-2");
                Expect("Panoya ulasan kopya hata verse de basarili", clipboard.HoldsCopy && board == "sifre-2");
                clipboard.ClearIfUnchanged();
                Expect("Panoya ulasan kopya temizleniyor", clears == 1 && board == null);

                placeThenFail = false;
                failWithoutPlacing = true;
                board = "onceki";
                bool threw = false;
                try { clipboard.Copy("sifre-3"); }
                catch (ExternalException) { threw = true; }
                Expect("Panoya ulasmayan kopya hata veriyor", threw && !clipboard.HoldsCopy && board == "onceki");
            }

            Expect("SAP bildirimi", TrayMenu.CopiedCaption(new Entry("Kartal SAP PRD", "kullanici1", "test123")) == "Kullanıcı adı ve şifre kopyalandı");
            Expect("VPN bildirimi", TrayMenu.CopiedCaption(new Entry("Akasya VPN", "a1")) == "Şifre kopyalandı");
        }

        private static void MenuItems(string work)
        {
            PasswordStore store = NewStore(work, "menu");
            MenuItem[] empty = TrayMenu.EntryItems(store.Entries, delegate { });
            Expect("Bos liste: tek, tiklanamayan oge", empty.Length == 1 && empty[0].Text == TrayMenu.EmptyText && !empty[0].Enabled);
            Expect("Bos liste metni", TrayMenu.EmptyText == "(Henüz kayıt yok)");

            store.Add("Üzüm", "u1");
            store.Add("B&B", "b1");
            store.Add("Çelik", "c1");
            Entry clicked = null;
            MenuItem[] items = TrayMenu.EntryItems(store.Entries, delegate(Entry entry) { clicked = entry; });
            // "&" would underline the next letter; "&&" shows one ampersand
            Expect("Menu alfabetik ve & kacisli", items.Length == 3 && items[0].Text == "B&&B" && items[1].Text == "Çelik" && items[2].Text == "Üzüm");
            items[1].PerformClick();
            Expect("Tiklanan kaydin sifresi kopyalaniyor", clicked != null && clicked.Name == "Çelik" && clicked.Password == "c1");
        }

        private static void Infrastructure(string work)
        {
            Expect("--autostart oturum acilisi sayiliyor", AutoStart.WasStartedAtSignIn(new[] { AutoStart.Flag }));
            Expect("Argumansiz acilis elle acilis sayiliyor", !AutoStart.WasStartedAtSignIn(new string[0]));
            Expect("Windows ile baslat durumu Run kaydini yansitiyor", AutoStart.IsEnabled() == RunEntryExists());

            // Tray settings store system folders as "{known folder id}\rest" and other paths as-is
            Expect("Program Files yolu cozuluyor",
                TrayPin.ResolveShellPath(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Test\Uygulama.exe")
                    == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Test\Uygulama.exe"));
            Expect("Windows yolu cozuluyor",
                TrayPin.ResolveShellPath(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe")
                    == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            Expect("Normal yol aynen kaliyor",
                TrayPin.ResolveShellPath(@"C:\Users\Test\VpnSifreleri.exe") == @"C:\Users\Test\VpnSifreleri.exe");
            Expect("Bozuk GUID aynen kaliyor", TrayPin.ResolveShellPath(@"{bozuk}\a.exe") == @"{bozuk}\a.exe");

            // Icon: exact sizes, transparent outside the rounded tile
            using (Bitmap small = AppIcon.Draw(16))
                Expect("Ikon 16 px, kose saydam, orta dolu", small.Width == 16 && small.GetPixel(0, 0).A == 0 && small.GetPixel(8, 8).A == 255);
            using (Icon tray = AppIcon.ForTray(24))
                Expect("Tepsi ikonu istenen boyutta", tray.Width == 24 && tray.Height == 24);
            using (Icon window = AppIcon.ForWindow())
                Expect("Pencere ikonu okunuyor", window.Width > 0);
            string ico = Path.Combine(work, "VpnSifreleri.ico");
            AppIcon.SaveIco(ico);
            byte[] header = File.ReadAllBytes(ico);
            Expect(".ico 8 boyut tasiyor", header.Length > 6 && header[2] == 1 && header[4] == 8);
        }

        // "Windows ile başlat" must not keep starting an older copy, which cannot read the newer file format
        private static void AutoStartFollows()
        {
            Version current = new Version(1, 1, 0);
            Func<string, Version> versionOf = delegate(string exe)
            {
                if (exe == @"C:\Eski\VpnSifreleri.exe") return new Version(1, 0, 0);
                if (exe == @"C:\Yeni\VpnSifreleri.exe") return new Version(1, 2, 0);
                return null; // no such file
            };
            string self = @"C:\Bu\VpnSifreleri.exe";
            Expect("Bu exe'yi gosteren kayit kaliyor", !AutoStart.ShouldFollow(@"""C:\Bu\VpnSifreleri.exe"" --autostart", self, current, versionOf));
            Expect("Harf buyuklugu farki ayni exe", !AutoStart.ShouldFollow(@"""c:\bu\vpnsifreleri.exe"" --autostart", self, current, versionOf));
            Expect("Eski surumu gosteren kayit buraya geciyor", AutoStart.ShouldFollow(@"""C:\Eski\VpnSifreleri.exe"" --autostart", self, current, versionOf));
            Expect("Silinmis exe'yi gosteren kayit buraya geciyor", AutoStart.ShouldFollow(@"""C:\Silinen\VpnSifreleri.exe"" --autostart", self, current, versionOf));
            Expect("Daha yeni surumu gosteren kayit kaliyor", !AutoStart.ShouldFollow(@"""C:\Yeni\VpnSifreleri.exe"" --autostart", self, current, versionOf));
            Expect("Tirnaksiz yol da okunuyor", AutoStart.ShouldFollow(@"C:\Eski\VpnSifreleri.exe --autostart", self, current, versionOf));
        }

        private static bool RunEntryExists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("VpnSifreleri") != null;
        }

        // The window is never shown: the tests call what its buttons and keys call, and answer its questions
        private static void Cockpit(string work)
        {
            PasswordStore store = NewStore(work, "kokpit");
            store.Add("Açelya", "a1");
            List<string> asked = new List<string>();
            bool answer = false;
            using (CockpitForm form = new CockpitForm(store))
            {
                form.Confirm = delegate(string question) { asked.Add(question); return answer; };
                form.ShowError = delegate(string message) { throw new Exception("Beklenmeyen hata: " + message); };

                Expect("Kokpit Ekle modunda aciliyor", form.SubmitButton.Text == "Ekle" && form.EditingName == null && !form.DeleteButton.Enabled);
                Expect("Sifreler gizli basliyor", form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[2].Text == CockpitForm.Hidden);
                // The window keeps its first layout when shown, so the column has to fit already
                Expect("Sifre sutunu listenin sonuna kadar uzaniyor",
                    form.EntryList.Columns[0].Width + form.EntryList.Columns[1].Width + form.EntryList.Columns[2].Width + 1 == form.EntryList.ClientSize.Width);

                // Add: trimmed, boxes emptied, status says so
                form.NameBox.Text = "  Köprü ";
                form.PasswordBox.Text = "k1";
                form.Submit();
                Expect("Ekle: kayit eklendi", store.Find("Köprü") != null && store.Find("Köprü").Password == "k1");
                Expect("Ekle: kutular temizlendi", form.NameBox.Text == "" && form.PasswordBox.Text == "");
                Expect("Ekle: durum satiri", form.StatusLabel.Text == "✓ Köprü eklendi");
                Expect("Ekle: liste alfabetik", ListNames(form) == "Açelya|Köprü");

                // Rejected input stays in the boxes
                form.NameBox.Text = " ";
                form.PasswordBox.Text = "x1";
                form.Submit();
                Expect("Bos ad uyarisi", form.StatusLabel.Text == "Müşteri adı boş olamaz" && form.PasswordBox.Text == "x1");
                form.NameBox.Text = "Yeni";
                form.PasswordBox.Text = "";
                form.Submit();
                Expect("Bos sifre uyarisi", form.StatusLabel.Text == "Şifre boş olamaz" && form.NameBox.Text == "Yeni");

                // An existing name: asks first, updates only on yes, keeps the stored spelling
                form.NameBox.Text = "KÖPRÜ";
                form.PasswordBox.Text = "k2";
                answer = false;
                form.Submit();
                Expect("Ayni ad soruluyor", asked.Count == 1 && asked[0] == "Köprü zaten var. Şifresi güncellensin mi?");
                Expect("Ayni ad, hayir: degismedi", store.Find("Köprü").Password == "k1" && form.NameBox.Text == "KÖPRÜ");
                answer = true;
                form.Submit();
                Expect("Ayni ad, evet: sifre guncellendi, ad ayni", store.Find("Köprü").Password == "k2" && store.Find("Köprü").Name == "Köprü");
                Expect("Ayni ad, evet: durum satiri", form.StatusLabel.Text == "✓ Köprü güncellendi");

                // Update mode: clash, rename, back to adding
                form.BeginEdit(store.Find("Köprü"));
                Expect("Duzenleme: kutular doldu", form.SubmitButton.Text == "Güncelle" && form.NameBox.Text == "Köprü"
                    && form.PasswordBox.Text == "k2" && form.DeleteButton.Enabled);
                form.NameBox.Text = "açelya";
                form.Submit();
                Expect("Duzenleme: cakisan ad", form.StatusLabel.Text == "Bu isimde başka bir kayıt var" && form.EditingName == "Köprü");
                form.NameBox.Text = "Köprü Test";
                form.PasswordBox.Text = "k3";
                form.Submit();
                Expect("Duzenleme: yeniden adlandirildi", store.Find("Köprü") == null && store.Find("Köprü Test").Password == "k3");
                Expect("Duzenleme sonrasi Ekle modu", form.SubmitButton.Text == "Ekle" && form.EditingName == null && form.NameBox.Text == "");

                form.BeginEdit(store.Find("Açelya"));
                form.HandleEscape();
                Expect("Esc duzenlemeyi birakiyor", form.EditingName == null && form.NameBox.Text == "" && !form.IsDisposed);

                // Delete asks first
                form.BeginEdit(store.Find("Açelya"));
                answer = false;
                form.DeleteSelected();
                Expect("Sil, hayir: kayit duruyor", store.Find("Açelya") != null);
                answer = true;
                form.DeleteSelected();
                Expect("Sil, evet: silindi", store.Find("Açelya") == null && form.StatusLabel.Text == "✓ Açelya silindi" && form.EditingName == null);

                // Göster: box and list
                form.RevealBox.Checked = true;
                Expect("Goster: sifreler acik", !form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[2].Text == "k3");
                form.RevealBox.Checked = false;
                Expect("Gizle: sifreler kapali", form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[2].Text == CockpitForm.Hidden);

                form.HandleEscape();
                Expect("Ekle modunda Esc pencereyi kapatiyor", form.IsDisposed);
            }

            // A failed save shows why and keeps what was typed
            string blocker = Path.Combine(work, "kokpit-engel");
            File.WriteAllText(blocker, "");
            PasswordStore blocked = PasswordStore.Open(Path.Combine(blocker, "sifreler.dat"), TestEntropy);
            using (CockpitForm form = new CockpitForm(blocked))
            {
                string shown = null;
                form.ShowError = delegate(string message) { shown = message; };
                form.NameBox.Text = "Köprü";
                form.PasswordBox.Text = "k1";
                form.Submit();
                Expect("Kaydedilemedi uyarisi", shown != null && shown.StartsWith("Kaydedilemedi: ", StringComparison.Ordinal));
                Expect("Kaydedilemedi: kutular korundu", form.NameBox.Text == "Köprü" && form.PasswordBox.Text == "k1");
            }
        }

        private static void CockpitUsernames(string work)
        {
            PasswordStore store = NewStore(work, "kokpit-kullanici");
            store.Add("Akasya VPN", "a1");
            List<string> asked = new List<string>();
            using (CockpitForm form = new CockpitForm(store))
            {
                form.Confirm = delegate(string question) { asked.Add(question); return true; };
                form.ShowError = delegate(string message) { throw new Exception("Beklenmeyen hata: " + message); };

                form.NameBox.Text = "Kartal SAP PRD";
                form.UsernameBox.Text = " kullanici1 ";
                form.PasswordBox.Text = "test123";
                form.Submit();
                Expect("Kokpit kullanici adiyla ekliyor", store.Find("Kartal SAP PRD") != null && store.Find("Kartal SAP PRD").Username == "kullanici1");
                Expect("Ekledikten sonra kullanici kutusu bos", form.UsernameBox.Text == "");
                Expect("Listede kullanici adi sutunu", form.EntryList.Items[1].SubItems[1].Text == "kullanici1"
                    && form.EntryList.Items[0].SubItems[1].Text == "" && form.EntryList.Items[1].SubItems[2].Text == CockpitForm.Hidden);

                form.BeginEdit(store.Find("Kartal SAP PRD"));
                Expect("Secince kullanici kutusu doluyor", form.UsernameBox.Text == "kullanici1");
                form.UsernameBox.Text = "kullanici2";
                form.Submit();
                Expect("Guncelleme kullanici adini yaziyor", store.Find("Kartal SAP PRD").Username == "kullanici2" && form.UsernameBox.Text == "");
                form.BeginEdit(store.Find("Kartal SAP PRD"));
                form.HandleEscape();
                Expect("Vazgecince kullanici kutusu bos", form.UsernameBox.Text == "");

                // The same name again: the question says what will change; yes writes what is in the boxes
                form.NameBox.Text = "KARTAL SAP PRD";
                form.UsernameBox.Text = "kullanici2";
                form.PasswordBox.Text = "test456";
                form.Submit();
                form.NameBox.Text = "Kartal SAP PRD";
                form.UsernameBox.Text = "kullanici3";
                form.PasswordBox.Text = "test789";
                form.Submit();
                Expect("Ayni kullanici adinda eski soru", asked.Count == 2 && asked[0] == "Kartal SAP PRD zaten var. Şifresi güncellensin mi?");
                Expect("Farkli kullanici adinda yeni soru", asked.Count == 2 && asked[1] == "Kartal SAP PRD zaten var. Kullanıcı adı ve şifresi güncellensin mi?");
                Expect("Evet: kutulardaki kullanici adi ve sifre", store.Find("Kartal SAP PRD").Username == "kullanici3"
                    && store.Find("Kartal SAP PRD").Password == "test789" && store.Find("Kartal SAP PRD").Name == "Kartal SAP PRD");

                // Both lines pasted into one box
                form.NameBox.Text = "Iki Satir";
                form.UsernameBox.Text = "kullanici1\r\ntest123";
                form.PasswordBox.Text = "x";
                form.Submit();
                Expect("Iki satir uyarisi", form.StatusLabel.Text == "Ad, kullanıcı adı ve şifre tek satır olmalı"
                    && form.UsernameBox.Text == "kullanici1\r\ntest123" && store.Find("Iki Satir") == null);

                Expect("Durum satirinda & kisayol degil", !form.StatusLabel.UseMnemonic);

                form.RevealBox.Checked = true;
                form.HidePasswords();
                Expect("One getirmede sifreler gizleniyor", !form.RevealBox.Checked && form.PasswordBox.UseSystemPasswordChar
                    && form.EntryList.Items[0].SubItems[2].Text == CockpitForm.Hidden);

                // A real paste message: the Windows box would keep only "kullanici1"; the cockpit refuses and says why
                form.StatusLabel.Text = "";
                form.PasswordBox.Text = "";
                form.PasswordBox.ReadClipboard = delegate { return "kullanici1\r\ntest123"; };
                SendMessage(form.PasswordBox.Handle, 0x0302, IntPtr.Zero, IntPtr.Zero);
                Expect("Iki satirlik yapistirma reddediliyor", form.PasswordBox.Text == ""
                    && form.StatusLabel.Text == "Ad, kullanıcı adı ve şifre tek satır olmalı");
            }

            Expect("Iki satir yapistirilmaz", SingleLineBox.Refuses("kullanici1\r\ntest123") && SingleLineBox.Refuses(" a \n b "));
            Expect("Sonda satir sonu olan tek satir yapistirilir", !SingleLineBox.Refuses("test123\r\n"));
            Expect("Tek satir ve bos pano yapistirilir", !SingleLineBox.Refuses("tek satir") && !SingleLineBox.Refuses(null));
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private static void UpdateTags()
        {
            string github = "{\"url\":\"https://api.github.com/repos/alperennkls/VpnSifreleri/releases/1\","
                + "\"html_url\":\"https://github.com/alperennkls/VpnSifreleri/releases/tag/v1.2.0\",\"id\":1,"
                + "\"tag_name\":\"v1.2.0\",\"target_commitish\":\"main\",\"name\":\"VPN Şifreleri 1.2.0\",\"draft\":false}";
            Expect("GitHub yanitindan surum", UpdateCheck.ParseTag(github) == new Version(1, 2, 0));
            Expect("v oneksiz ve iki haneli etiket", UpdateCheck.ParseTag("{\"tag_name\": \"1.3\"}") == new Version(1, 3, 0));
            Expect("Etiket yoksa surum yok", UpdateCheck.ParseTag("{\"message\":\"Not Found\"}") == null);
            Expect("Sayisal olmayan etiket", UpdateCheck.ParseTag("{\"tag_name\":\"latest\"}") == null);
            Expect("Bos yanit", UpdateCheck.ParseTag(null) == null && UpdateCheck.ParseTag("") == null);
            Expect("Asiri buyuk numara", UpdateCheck.ParseTag("{\"tag_name\":\"v99999999999.1.0\"}") == null);

            Version current = new Version(1, 1, 0);
            Expect("Buyuk surum yeni", UpdateCheck.IsNewer(new Version(1, 2, 0), current));
            Expect("Ayni surum yeni degil", !UpdateCheck.IsNewer(new Version(1, 1, 0), current));
            Expect("Kucuk surum yeni degil", !UpdateCheck.IsNewer(new Version(1, 0, 9), current));
            Expect("1.10 buyuktur 1.9", UpdateCheck.IsNewer(new Version(1, 10, 0), new Version(1, 9, 0)));
            Expect("Surum yoksa yeni degil", !UpdateCheck.IsNewer(null, current));
            Expect("Calisan surum uc haneli", UpdateCheck.Current.Revision == -1);
            // After sign-in the network often comes up late: a failed check is retried soon, a successful one in a day
            Expect("Ilk kontrol 10 sn sonra", UpdateCheck.FirstCheckMs == 10 * 1000);
            Expect("Basarisiz kontrolden sonra 15 dk", UpdateCheck.NextCheckMs(null) == 15 * 60 * 1000);
            Expect("Basarili kontrolden sonra 24 saat", UpdateCheck.NextCheckMs(new Version(1, 1, 0)) == 24 * 60 * 60 * 1000);

            // Nothing listens there: the check must give up quietly instead of throwing or hanging
            DateTime started = DateTime.Now;
            Expect("Erisilemeyen adres sessizce null", UpdateCheck.FetchLatest("https://127.0.0.1:9/") == null
                && (DateTime.Now - started).TotalSeconds < 20);
        }

        // Invisible characters in source look like tampering in a password app and hide from review: write them as escapes
        private static void SourceHygiene(string root)
        {
            char[] invisible = { (char)0x200B, (char)0x200C, (char)0x200D, (char)0x2060, (char)0xFEFF };
            List<string> files = new List<string>(Directory.GetFiles(root, "*.cs"));
            string tools = Path.Combine(root, "tools");
            if (Directory.Exists(tools)) files.AddRange(Directory.GetFiles(tools, "*.ps1"));
            Expect("Kaynak dosyalari bulundu", files.Count >= 10);
            foreach (string file in files)
                Expect("Kaynakta gorunmez karakter yok: " + Path.GetFileName(file), File.ReadAllText(file).IndexOfAny(invisible) < 0);
        }

        private static string ListNames(CockpitForm form)
        {
            List<string> names = new List<string>();
            foreach (ListViewItem item in form.EntryList.Items) names.Add(item.Text);
            return string.Join("|", names);
        }

        private static void Expect(string name, bool condition)
        {
            if (condition) return;
            failures++;
            Console.WriteLine("BASARISIZ: " + name);
        }

        private static PasswordStore NewStore(string work, string folder)
        {
            return PasswordStore.Open(Path.Combine(work, folder, "sifreler.dat"), TestEntropy);
        }

        private static string Names(PasswordStore store)
        {
            List<string> names = new List<string>();
            foreach (Entry entry in store.Entries) names.Add(entry.Name);
            return string.Join("|", names);
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
