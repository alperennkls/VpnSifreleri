# VPN Şifreleri — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Amaç:** Müşteri VPN şifrelerini saatin yanındaki ikondan tek tıkla panoya kopyalayan, kayıtları bir "kokpit" penceresinden yönetilen tepsi uygulaması.

**Mimari:** Test Veri Üretici'nin temeli: Windows'la gelen `csc.exe` ile tek exe, WinForms `NotifyIcon` + klasik `ContextMenu`. Kayıtlar `PasswordStore`'da Türkçe alfabeyle sıralı tutulur ve her değişiklik DPAPI (CurrentUser) ile şifrelenmiş `%APPDATA%\VpnSifreleri\sifreler.dat` dosyasına yazıldıktan sonra listeye yansır. Menü, pano (`SecureClipboard`) ve kokpit (`CockpitForm`) yalnızca bu depoyu kullanır.

**Teknoloji:** C# 5 / .NET Framework 4.x WinForms (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), `System.Security.Cryptography.ProtectedData`, PowerShell 5.1 (build ve doğrulama), UI Automation (yalnızca doğrulama).

**Spec:** `docs/superpowers/specs/2026-09-30-vpn-sifreleri-design.md`

## Genel Kısıtlar

- Proje kökü `C:\Users\Alperen\projects\VpnSifreleri`; yerel git deposu (`main`). Uzak depo yok, **push yok**.
- Kaynaklar **C# 5**: `$""`, `?.`, `nameof`, `=>` üyeler, exception filter (`when`), otomatik özellik başlatıcı yok.
- Referanslar yalnızca `System.Windows.Forms.dll`, `System.Drawing.dll`, `System.Security.dll`; hiçbir paket kurulmaz.
- `build.ps1` yalnızca **ASCII** (PowerShell 5.1 BOM'suz betiği ANSI okur). Türkçe metinler `.cs` ve `.md` dosyalarında (UTF-8, `/codepage:65001`).
- Test adları ASCII (konsol kod sayfası Türkçe harfleri bozar).
- Adlar: namespace `VpnSifreleri`, exe `VpnSifreleri.exe`, mutex `Local\VpnSifreleri`, olay `Local\VpnSifreleri.Kokpit`, Run değeri `VpnSifreleri`.
- Veri `%APPDATA%\VpnSifreleri\sifreler.dat` (DPAPI, CurrentUser). Depoda hiçbir zaman veri dosyası yok (`.gitignore`: `*.dat`, `*.dat.*`).
- Testler gerçek `sifreler.dat`'a, panoya ve Run kaydına **yazmaz**; her şey geçici bir klasörde olur.
- Bildirim ve menü **şifreyi asla göstermez**; yalnızca müşteri adını gösterir.
- Menü yalnızca sağ tıkla açılır; ikona çift tık kokpiti açar; "Windows ile başlat" yalnızca menüden elle açılır.
- Arayüz metinleri birebir şöyledir: "Şifreleri yönet…", "Windows ile başlat", "Çıkış", "(Henüz kayıt yok)", "Şifre kopyalandı", "Panoya kopyalanamadı, tekrar dene", "VPN Şifreleri çalışıyor", "Saatin yanındaki kilit ikonuna sağ tıkla", "Ayar kaydedilemedi", "Müşteri adı boş olamaz", "Şifre boş olamaz", "Bu isimde başka bir kayıt var", "Kayıt bulunamadı", "‹ad› zaten var. Şifresi güncellensin mi?", "‹ad› silinsin mi?", "✓ ‹ad› eklendi / güncellendi / silindi", "Kaydedilemedi: ‹neden›".
- Build (repo kökünde): `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1`. Testler ilk adımdır; başarısız test exe üretimini durdurur.
- Commit mesajları Türkçe ve `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` ile biter.

## İnceleme Odağı

1. **OneNote'tan yapıştırılan şifrenin sonunda satır sonu ya da boşluk** varsa VPN girişi sessizce başarısız olur; kırpılmalı → Task 1, "Bosluk ve satir sonu kirpiliyor" testi.
2. **Adında `&` olan müşteri** ("B&B") menüde altı çizili harf olarak değil, olduğu gibi görünmeli → Task 4, `B&&B` testi.
3. **Türkçe I/İ**: "İSTANBUL" = "istanbul", ama "Isparta" ≠ "isparta" (farklı harfler) → Task 1, I/İ testleri.
4. **Başka bilgisayardan/kullanıcıdan gelen, bozuk ya da kilitli şifre dosyası** hiçbir durumda üzerine yazılmamalı → Task 2 testleri.
5. **Kaydetme başarısız olursa** (disk dolu, izin yok) ne liste ne dosya değişmeli, kokpitte yazılanlar kalmalı → Task 1 "Kaydedilemeyince..." ve Task 6 "Kaydedilemedi..." testleri.

---

### Task 1: Kayıt deposu — kurallar ve şifreli dosya

**Files:**
- Create: `PasswordStore.cs`, `Tests.cs`, `build.ps1`

**Interfaces:**
- Produces:
  - `sealed class Entry` — `Entry(string name, string password)`, `string Name { get; }`, `string Password { get; }`
  - `enum StoreResult { Ok, NameEmpty, PasswordEmpty, NameExists, NotFound }`
  - `sealed class PasswordStore` — `static readonly StringComparer NameComparer`, `static string DefaultPath { get; }`, `static PasswordStore Open()`, `internal static PasswordStore Open(string path, byte[] entropy)`, `IList<Entry> Entries { get; }` (salt okunur, sıralı), `Entry Find(string name)`, `StoreResult Add(string name, string password)`, `StoreResult Update(string oldName, string newName, string password)`, `StoreResult Remove(string name)`, `event EventHandler Changed`. Kaydetme hatası `IOException`, `UnauthorizedAccessException` ya da `CryptographicException` olarak yükselir ve hiçbir şey değişmez.

- [ ] **Step 1: Failing test** — `build.ps1` (şimdilik yalnızca testler):

```powershell
# Builds VpnSifreleri.exe with the C# compiler that ships with Windows (.NET Framework 4.x),
# so nothing has to be installed. The tests run first; a failure stops the build.
# Keep this file ASCII: Windows PowerShell 5.1 reads scripts without a BOM as ANSI.
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$obj = Join-Path $root 'obj'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$common = '/nologo', '/codepage:65001', '/optimize+', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Security.dll'
# Every .cs file goes into both builds, except the other build's entry points
$sources = @(Get-ChildItem $root -Filter '*.cs' | Where-Object { $_.Extension -eq '.cs' } | ForEach-Object { $_.FullName })
$testSources = @($sources | Where-Object { (Split-Path $_ -Leaf) -notin 'TrayApp.cs', 'AssemblyInfo.cs' })

function Invoke-Csc([string[]] $arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Derleme hatasi' }
}

New-Item -ItemType Directory -Force $obj | Out-Null

# 1. Tests
Invoke-Csc (@('/target:exe', "/out:$obj\Tests.exe") + $testSources)
& "$obj\Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Testler basarisiz, exe uretilmedi' }
```

`Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        private static int Main()
        {
            string work = Path.Combine(Path.GetTempPath(), "VpnSifreleri-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                StoreRules(work);
                StoreFile(work);
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
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS0246: The type or namespace name 'PasswordStore' could not be found` ve "Derleme hatasi".

- [ ] **Step 3: Implement** — `PasswordStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VpnSifreleri
{
    /// <summary>One customer's VPN password.</summary>
    internal sealed class Entry
    {
        private readonly string name;
        private readonly string password;

        public Entry(string name, string password)
        {
            this.name = name;
            this.password = password;
        }

        public string Name
        {
            get { return name; }
        }

        public string Password
        {
            get { return password; }
        }
    }

    internal enum StoreResult { Ok, NameEmpty, PasswordEmpty, NameExists, NotFound }

    /// <summary>
    /// The customer list, always in Turkish alphabetical order. A change reaches the list only after it reached the
    /// disk, so a failed save leaves both as they were. The file is encrypted with DPAPI for the signed-in Windows
    /// user: nothing to type, and a copy taken to another PC or user cannot be read.
    /// </summary>
    internal sealed class PasswordStore
    {
        /// <summary>Turkish rules, ignoring case: sorting and the duplicate check agree (KÖPRÜ = köprü, I ≠ İ).</summary>
        public static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), true);

        private static readonly byte[] AppEntropy = Encoding.UTF8.GetBytes("VpnSifreleri/sifreler.dat");
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VPNS");
        private const int FormatVersion = 1;

        private readonly string path;
        private readonly byte[] entropy;
        private List<Entry> entries = new List<Entry>();

        /// <summary>Raised after every change that was saved.</summary>
        public event EventHandler Changed;

        private PasswordStore(string path, byte[] entropy)
        {
            this.path = path;
            this.entropy = entropy;
        }

        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VpnSifreleri", "sifreler.dat"); }
        }

        /// <summary>The user's own file under %APPDATA%.</summary>
        public static PasswordStore Open()
        {
            return Open(DefaultPath, AppEntropy);
        }

        /// <summary>Reads the file if there is one.</summary>
        internal static PasswordStore Open(string path, byte[] entropy)
        {
            PasswordStore store = new PasswordStore(path, entropy);
            if (File.Exists(path)) store.entries = Decode(File.ReadAllBytes(path), entropy);
            return store;
        }

        /// <summary>Alphabetical and read-only; every change goes through Add, Update or Remove.</summary>
        public IList<Entry> Entries
        {
            get { return entries.AsReadOnly(); }
        }

        public Entry Find(string name)
        {
            int index = IndexOf(Clean(name));
            return index < 0 ? null : entries[index];
        }

        // Add, Update and Remove save before they return. A failed save throws IOException,
        // UnauthorizedAccessException or CryptographicException and changes nothing.

        public StoreResult Add(string name, string password)
        {
            name = Clean(name);
            password = Clean(password);
            StoreResult invalid = Validate(name, password);
            if (invalid != StoreResult.Ok) return invalid;
            if (IndexOf(name) >= 0) return StoreResult.NameExists;

            List<Entry> next = new List<Entry>(entries);
            next.Add(new Entry(name, password));
            Commit(next);
            return StoreResult.Ok;
        }

        /// <summary>Changes the password and/or the name; a name may change only its case, or to one not in use.</summary>
        public StoreResult Update(string oldName, string newName, string password)
        {
            int index = IndexOf(Clean(oldName));
            if (index < 0) return StoreResult.NotFound;
            newName = Clean(newName);
            password = Clean(password);
            StoreResult invalid = Validate(newName, password);
            if (invalid != StoreResult.Ok) return invalid;
            int clash = IndexOf(newName);
            if (clash >= 0 && clash != index) return StoreResult.NameExists;

            List<Entry> next = new List<Entry>(entries);
            next[index] = new Entry(newName, password);
            Commit(next);
            return StoreResult.Ok;
        }

        public StoreResult Remove(string name)
        {
            int index = IndexOf(Clean(name));
            if (index < 0) return StoreResult.NotFound;

            List<Entry> next = new List<Entry>(entries);
            next.RemoveAt(index);
            Commit(next);
            return StoreResult.Ok;
        }

        // Pasted text often brings spaces or a line break along
        private static string Clean(string value)
        {
            return value == null ? "" : value.Trim();
        }

        private static StoreResult Validate(string name, string password)
        {
            if (name.Length == 0) return StoreResult.NameEmpty;
            if (password.Length == 0) return StoreResult.PasswordEmpty;
            return StoreResult.Ok;
        }

        private int IndexOf(string name)
        {
            for (int i = 0; i < entries.Count; i++)
                if (NameComparer.Equals(entries[i].Name, name)) return i;
            return -1;
        }

        private static int CompareByName(Entry a, Entry b)
        {
            return NameComparer.Compare(a.Name, b.Name);
        }

        private void Commit(List<Entry> next)
        {
            next.Sort(CompareByName);
            Save(next); // throws before the list changes
            entries = next;
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // Written beside the file and flushed to disk first, then swapped in: a power cut leaves the old or the new
        // file, never half of one. The replaced version stays as .bak.
        private void Save(List<Entry> list)
        {
            byte[] data = Encode(list, entropy);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            try
            {
                using (FileStream file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(data, 0, data.Length);
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
                else File.Move(tmp, path);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        private static byte[] Encode(List<Entry> list, byte[] entropy)
        {
            MemoryStream plain = new MemoryStream();
            using (BinaryWriter w = new BinaryWriter(plain, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(list.Count);
                foreach (Entry entry in list)
                {
                    w.Write(entry.Name);
                    w.Write(entry.Password);
                }
            }
            return ProtectedData.Protect(plain.ToArray(), entropy, DataProtectionScope.CurrentUser);
        }

        private static List<Entry> Decode(byte[] saved, byte[] entropy)
        {
            byte[] plain = ProtectedData.Unprotect(saved, entropy, DataProtectionScope.CurrentUser);
            using (BinaryReader r = new BinaryReader(new MemoryStream(plain), Encoding.UTF8))
            {
                if (!SameBytes(r.ReadBytes(Magic.Length), Magic) || r.ReadInt32() != FormatVersion)
                    throw new InvalidDataException("sifreler.dat biçimi tanınmadı");
                int count = r.ReadInt32();
                List<Entry> list = new List<Entry>();
                for (int i = 0; i < count; i++) list.Add(new Entry(r.ReadString(), r.ReadString()));
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("sifreler.dat sonunda fazladan veri var");
                list.Sort(CompareByName);
                return list;
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); }
            catch (IOException) { }                 // a leftover .tmp is overwritten by the next save
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: Commit**

```bash
git add build.ps1 PasswordStore.cs Tests.cs
git commit -F - <<'EOF'
Şifre deposu: Türkçe sıralı liste ve DPAPI ile şifreli dosya

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Okunamayan dosyayı kenara almak

**Files:**
- Modify: `PasswordStore.cs` (`Open` ve yeni yardımcılar), `Tests.cs`

**Interfaces:**
- Consumes: Task 1'deki `PasswordStore`.
- Produces: `string PasswordStore.UnreadableMovedTo { get; }` — açılışta kenara alınan dosyanın yolu, yoksa `null`. `Open(...)` dosyayı kenara bile alamazsa istisna fırlatır (dosyaya dokunmadan).

- [ ] **Step 1: Failing test** — `Tests.cs`'te `Main` içinde `StoreFile(work);` satırının altına `UnreadableFile(work);` ekle; `StoreFile` metodunun altına şunları ekle:

```csharp
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

        private static string WriteFile(string work, string folder, byte[] content)
        {
            string path = Path.Combine(work, folder, "sifreler.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS1061: 'VpnSifreleri.PasswordStore' does not contain a definition for 'UnreadableMovedTo'`.

- [ ] **Step 3: Implement** — `PasswordStore.cs`:

`private List<Entry> entries = new List<Entry>();` satırının altına:

```csharp
        private string unreadableMovedTo;
```

Task 1'deki `/// <summary>Reads the file if there is one.</summary>` ile başlayan `Open(string path, byte[] entropy)` metodunu tamamen şununla değiştir:

```csharp
        /// <summary>
        /// Reads the file if there is one. A file this user cannot decrypt is never written over: it is renamed to
        /// sifreler.dat.okunamadi-yyyyMMdd-HHmmss and the list starts empty. If even that rename fails, this throws.
        /// </summary>
        internal static PasswordStore Open(string path, byte[] entropy)
        {
            PasswordStore store = new PasswordStore(path, entropy);
            if (!File.Exists(path)) return store;
            List<Entry> loaded = TryDecode(File.ReadAllBytes(path), entropy);
            if (loaded != null)
            {
                store.entries = loaded;
                return store;
            }
            string aside = FreeAsideName(path);
            File.Move(path, aside);
            store.unreadableMovedTo = aside;
            return store;
        }

        /// <summary>Where an unreadable file was set aside while opening, or null.</summary>
        public string UnreadableMovedTo
        {
            get { return unreadableMovedTo; }
        }
```

`Decode` metodunun altına:

```csharp
        // null when the bytes are not a list this Windows user saved with this entropy
        private static List<Entry> TryDecode(byte[] saved, byte[] entropy)
        {
            try
            {
                return Decode(saved, entropy);
            }
            catch (CryptographicException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }     // cut short: EndOfStreamException
            catch (FormatException) { return null; } // broken length prefix
        }

        private static string FreeAsideName(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string aside = path + ".okunamadi-" + stamp;
            for (int n = 2; File.Exists(aside); n++) aside = path + ".okunamadi-" + stamp + "-" + n;
            return aside;
        }
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: Commit**

```bash
git add PasswordStore.cs Tests.cs
git commit -F - <<'EOF'
Okunamayan şifre dosyası kenara alınır, üzerine yazılmaz

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Güvenli pano

**Files:**
- Create: `SecureClipboard.cs`
- Modify: `Tests.cs`

**Interfaces:**
- Produces: `sealed class SecureClipboard : IDisposable` — `const int ClearAfterMs = 30000`, `internal const string ExcludeFormat / HistoryFormat / CloudFormat`, `SecureClipboard()`, `void Copy(string text)` (pano kilitliyse `ExternalException`), `void ClearIfUnchanged()`, `internal static bool ShouldClear(string current, string copiedText)`, `internal static DataObject CreateDataObject(string text)`, `void Dispose()`. UI iş parçacığında (STA, mesaj döngüsü) kullanılır.

- [ ] **Step 1: Failing test** — `Main` içinde `UnreadableFile(work);` altına `ClipboardData();` ekle; `WriteFile` metodunun altına:

```csharp
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
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS0103: The name 'SecureClipboard' does not exist in the current context`.

- [ ] **Step 3: Implement** — `SecureClipboard.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VpnSifreleri
{
    /// <summary>
    /// Copies a password the way password managers do: Windows keeps it out of the Win+V history and cloud sync,
    /// and it is removed after 30 seconds unless something else was copied since.
    /// </summary>
    internal sealed class SecureClipboard : IDisposable
    {
        public const int ClearAfterMs = 30000;

        // Formats Windows checks before recording clipboard content ("Cloud Clipboard and Clipboard History Formats")
        internal const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
        internal const string HistoryFormat = "CanIncludeInClipboardHistory";
        internal const string CloudFormat = "CanUploadToCloudClipboard";

        private readonly Timer clearTimer = new Timer();
        private string copied;

        public SecureClipboard()
        {
            clearTimer.Interval = ClearAfterMs;
            clearTimer.Tick += delegate { ClearIfUnchanged(); };
        }

        /// <summary>Throws ExternalException when another app keeps the clipboard locked through every retry.</summary>
        public void Copy(string text)
        {
            // Retries cover the moment another app holds the clipboard open
            Clipboard.SetDataObject(CreateDataObject(text), true, 10, 50);
            copied = text;
            clearTimer.Stop();
            clearTimer.Start();
        }

        /// <summary>Empties the clipboard if it still holds the copied password; runs after 30 s and on exit.</summary>
        public void ClearIfUnchanged()
        {
            clearTimer.Stop();
            if (copied == null) return;
            string expected = copied;
            copied = null;
            try
            {
                if (ShouldClear(Clipboard.ContainsText() ? Clipboard.GetText() : null, expected)) Clipboard.Clear();
            }
            catch (ExternalException)
            {
                // Another app holds the clipboard right now: leave it rather than wait
            }
        }

        internal static bool ShouldClear(string current, string copiedText)
        {
            return copiedText != null && current == copiedText;
        }

        internal static DataObject CreateDataObject(string text)
        {
            DataObject data = new DataObject();
            data.SetText(text);
            // A DWORD 0 in the last two keeps the text out of history and cloud sync; the first only has to be there
            data.SetData(ExcludeFormat, Dword(0));
            data.SetData(HistoryFormat, Dword(0));
            data.SetData(CloudFormat, Dword(0));
            return data;
        }

        private static MemoryStream Dword(int value)
        {
            return new MemoryStream(BitConverter.GetBytes(value));
        }

        public void Dispose()
        {
            clearTimer.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: Gerçek panoda bir kez doğrula** — WinForms'un `MemoryStream`'i panoya ham bayt olarak koyduğunu görmek için (panodaki metin birkaç saniyeliğine değişir, sonra geri konur). Repo kökünde, Windows PowerShell'de:

```powershell
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ClipFormats {
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr mem);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr mem);
    public static string Bytes(string format) {
        if (!OpenClipboard(IntPtr.Zero)) return "pano acilamadi";
        try {
            IntPtr mem = GetClipboardData(RegisterClipboardFormat(format));
            if (mem == IntPtr.Zero) return "yok";
            byte[] bytes = new byte[(int)GlobalSize(mem)];
            Marshal.Copy(GlobalLock(mem), bytes, 0, bytes.Length);
            GlobalUnlock(mem);
            return BitConverter.ToString(bytes);
        } finally { CloseClipboard(); }
    }
}
'@
$saved = if ([Windows.Forms.Clipboard]::ContainsText()) { [Windows.Forms.Clipboard]::GetText() } else { $null }
$type = [Reflection.Assembly]::LoadFile((Resolve-Path 'obj\Tests.exe').Path).GetType('VpnSifreleri.SecureClipboard')
$data = $type.GetMethod('CreateDataObject', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @('pano-denemesi'))
[Windows.Forms.Clipboard]::SetDataObject($data, $true, 10, 50)
'metin: ' + [Windows.Forms.Clipboard]::GetText()
foreach ($f in 'ExcludeClipboardContentFromMonitorProcessing', 'CanIncludeInClipboardHistory', 'CanUploadToCloudClipboard') { $f + ': ' + [ClipFormats]::Bytes($f) }
if ($saved) { [Windows.Forms.Clipboard]::SetText($saved) } else { [Windows.Forms.Clipboard]::Clear() }
```

Expected: `metin: pano-denemesi` ve üç biçimin her biri `00-00-00-00`. Başka bir şey çıkarsa (ör. baytların önünde .NET serileştirme başlığı) dur ve `CreateDataObject`'i ham `HGLOBAL` yazan bir yola çevirmeden ilerleme.

- [ ] **Step 6: Commit**

```bash
git add SecureClipboard.cs Tests.cs
git commit -F - <<'EOF'
Pano: Win+V geçmişine ve buluta gitmeden kopyala, 30 sn sonra temizle

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Tepsi menüsündeki müşteri öğeleri

**Files:**
- Create: `TrayMenu.cs`
- Modify: `Tests.cs`

**Interfaces:**
- Consumes: `Entry`, `PasswordStore.Entries` (Task 1).
- Produces: `static class TrayMenu` — `const string EmptyText = "(Henüz kayıt yok)"`, `static MenuItem[] EntryItems(IList<Entry> entries, Action<Entry> copy)`, `internal static string Label(string name)`.

- [ ] **Step 1: Failing test** — `Main` içinde `ClipboardData();` altına `MenuItems(work);` ekle; `IsDwordZero` metodunun altına:

```csharp
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
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS0103: The name 'TrayMenu' does not exist in the current context`.

- [ ] **Step 3: Implement** — `TrayMenu.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace VpnSifreleri
{
    /// <summary>The customer part of the tray menu, in the store's alphabetical order.</summary>
    internal static class TrayMenu
    {
        public const string EmptyText = "(Henüz kayıt yok)";

        public static MenuItem[] EntryItems(IList<Entry> entries, Action<Entry> copy)
        {
            if (entries.Count == 0)
            {
                MenuItem none = new MenuItem(EmptyText);
                none.Enabled = false;
                return new[] { none };
            }
            MenuItem[] items = new MenuItem[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                items[i] = new MenuItem(Label(entry.Name), delegate { copy(entry); });
            }
            return items;
        }

        /// <summary>A single ampersand would underline the next letter instead of showing up.</summary>
        internal static string Label(string name)
        {
            return name.Replace("&", "&&");
        }
    }
}
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: Commit**

```bash
git add TrayMenu.cs Tests.cs
git commit -F - <<'EOF'
Tepsi menüsü: müşteriler alfabetik, & doğru görünür

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Taşınan altyapı ve kilit ikonu

**Files:**
- Create: `Toast.cs` (Test Veri Üretici'den kopya), `TrayPin.cs`, `AutoStart.cs`, `AppIcon.cs`
- Modify: `Tests.cs`

**Interfaces:**
- Produces:
  - `Toast.Popup(string caption, string text, bool success, int visibleMs = 1800)` — odak çalmayan bildirim (üst satır küçük, alt satır büyük).
  - `TrayPin.PinWhenRegistered()`, `internal static string TrayPin.ResolveShellPath(string stored)`.
  - `AutoStart.Flag` (`"--autostart"`), `AutoStart.WasStartedAtSignIn(string[] args)`, `AutoStart.IsEnabled()`, `AutoStart.SetEnabled(bool enabled)`.
  - `AppIcon.ForTray(int size)`, `AppIcon.ForWindow()`, `AppIcon.SaveIco(string path)`, `AppIcon.Draw(int size)`.

- [ ] **Step 1: Failing test** — `Main` içinde `MenuItems(work);` altına `Infrastructure(work);` ekle; `MenuItems` metodunun altına:

```csharp
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

        private static bool RunEntryExists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("VpnSifreleri") != null;
        }
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS0103: The name 'AutoStart' does not exist in the current context` (ve `TrayPin`, `AppIcon` için benzerleri).

- [ ] **Step 3: Implement**

`Toast.cs` — Test Veri Üretici'den birebir, yalnızca namespace değişir (Git Bash, repo kökünde):

```bash
sed 's/^namespace TestVeriUretici\b/namespace VpnSifreleri/' ../TestVeriUretici/Toast.cs > Toast.cs
grep -n '^namespace' Toast.cs   # Expected: 8:namespace VpnSifreleri
```

`AutoStart.cs`:

```csharp
using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VpnSifreleri
{
    /// <summary>"Windows ile başlat": a per-user Run entry, only ever switched on from the menu.</summary>
    internal static class AutoStart
    {
        /// <summary>Added to the Run entry so a start at sign-in shows no notice.</summary>
        public const string Flag = "--autostart";

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "VpnSifreleri";

        public static bool WasStartedAtSignIn(string[] args)
        {
            return Array.IndexOf(args, Flag) >= 0;
        }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(ValueName) != null;
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" " + Flag);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
```

`TrayPin.cs` (Test Veri Üretici'deki sürümün Store'a özel kısımları çıkarılmış hali):

```csharp
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
```

`AppIcon.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VpnSifreleri
{
    /// <summary>
    /// Draws the app icon in code: the tray gets an exact-size icon at any DPI, and build.ps1 asks for
    /// a multi-size .ico to embed in the .exe, so there is no image file to maintain.
    /// </summary>
    internal static class AppIcon
    {
        private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        // The Icon class does not read PNG frames on every .NET Framework version, so the window icon stops at 64 px
        private static readonly int[] WindowSizes = { 16, 20, 24, 32, 40, 48, 64 };
        private static readonly Color Top = Color.FromArgb(16, 185, 129);
        private static readonly Color Bottom = Color.FromArgb(4, 120, 87);

        public static Icon ForTray(int size)
        {
            using (MemoryStream ico = new MemoryStream(BuildIco(new[] { size })))
                return new Icon(ico, size, size);
        }

        /// <summary>Cockpit title bar and taskbar button; Windows picks the size it needs.</summary>
        public static Icon ForWindow()
        {
            using (MemoryStream ico = new MemoryStream(BuildIco(WindowSizes)))
                return new Icon(ico);
        }

        public static void SaveIco(string path)
        {
            File.WriteAllBytes(path, BuildIco(IcoSizes));
        }

        /// <summary>Green rounded tile with a white padlock.</summary>
        public static Bitmap Draw(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size;

                RectangleF tile = new RectangleF(0, 0, s, s);
                using (GraphicsPath shape = RoundedRect(tile, s * 0.22f))
                using (LinearGradientBrush fill = new LinearGradientBrush(RectangleF.Inflate(tile, 1, 1), Top, Bottom, LinearGradientMode.Vertical))
                    g.FillPath(fill, shape);

                // Shackle: an upside-down U whose legs disappear into the body
                RectangleF arc = new RectangleF(s * 0.33f, s * 0.16f, s * 0.34f, s * 0.34f);
                float legTop = arc.Top + arc.Height / 2f;
                using (Pen shackle = new Pen(Color.White, Math.Max(1.5f, s * 0.1f)))
                using (GraphicsPath u = new GraphicsPath())
                {
                    u.AddLine(arc.Left, s * 0.5f, arc.Left, legTop);
                    u.AddArc(arc, 180, 180);
                    u.AddLine(arc.Right, legTop, arc.Right, s * 0.5f);
                    g.DrawPath(shackle, u);
                }

                RectangleF body = new RectangleF(s * 0.21f, s * 0.46f, s * 0.58f, s * 0.40f);
                using (GraphicsPath block = RoundedRect(body, s * 0.08f))
                    g.FillPath(Brushes.White, block);

                // Keyhole in the darker green; the slot under it only where there are pixels for it
                using (SolidBrush hole = new SolidBrush(Bottom))
                {
                    float d = Math.Max(1.5f, s * 0.12f);
                    float cy = body.Top + body.Height * 0.38f;
                    g.FillEllipse(hole, s / 2f - d / 2f, cy - d / 2f, d, d);
                    if (size >= 24) g.FillRectangle(hole, s / 2f - d * 0.2f, cy, d * 0.4f, body.Height * 0.32f);
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ICO container: small sizes as 32-bit DIBs (read by every Windows API), 256 px as PNG
        private static byte[] BuildIco(int[] sizes)
        {
            List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
                using (Bitmap bmp = Draw(size))
                    images.Add(size >= 256 ? ToPng(bmp) : ToDib(bmp));

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((short)0);
                w.Write((short)1);                        // type: icon
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 means 256
                    w.Write(dimension);
                    w.Write(dimension);
                    w.Write((byte)0);                     // palette size
                    w.Write((byte)0);                     // reserved
                    w.Write((short)1);                    // planes
                    w.Write((short)32);                   // bits per pixel
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) w.Write(image);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] ToDib(Bitmap bmp)
        {
            int width = bmp.Width, height = bmp.Height;
            int maskStride = (width + 31) / 32 * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40);                              // BITMAPINFOHEADER size
                w.Write(width);
                w.Write(height * 2);                      // color rows + mask rows
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);                               // BI_RGB
                w.Write(width * height * 4 + maskStride * height);
                w.Write(0L);                              // resolution
                w.Write(0L);                              // palette counts
                for (int y = height - 1; y >= 0; y--)     // rows bottom-up, BGRA
                    for (int x = 0; x < width; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        w.Write(new[] { c.B, c.G, c.R, c.A });
                    }
                for (int y = height - 1; y >= 0; y--)     // AND mask, 1 = transparent
                {
                    byte[] row = new byte[maskStride];
                    for (int x = 0; x < width; x++)
                        if (bmp.GetPixel(x, y).A == 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    w.Write(row);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] ToPng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: İkona bak** — 16/20/24/32 px hallerini koyu (görev çubuğu) ve açık zemin üzerinde 6 kat büyütülmüş ve gerçek boyutta çizip PNG'yi Read ile aç:

```powershell
Add-Type -AssemblyName System.Drawing
$draw = [Reflection.Assembly]::LoadFile('C:\Users\Alperen\projects\VpnSifreleri\obj\Tests.exe').GetType('VpnSifreleri.AppIcon').GetMethod('Draw')
$sheet = New-Object System.Drawing.Bitmap(760, 520)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
foreach ($row in @(@{ Y = 0; Back = [System.Drawing.Color]::FromArgb(32, 32, 32) }, @{ Y = 260; Back = [System.Drawing.Color]::FromArgb(243, 243, 243) })) {
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($row.Back)), 0, $row.Y, 760, 260)
    $x = 10
    foreach ($size in 16, 20, 24, 32) {
        $icon = $draw.Invoke($null, @([int]$size))
        $g.DrawImage($icon, $x, $row.Y + 10, $size * 6, $size * 6)
        $g.DrawImage($icon, $x, $row.Y + 220, $size, $size)
        $x += $size * 6 + 20
        $icon.Dispose()
    }
}
$g.Dispose()
$sheet.Save('C:\Users\Alperen\AppData\Local\Temp\claude\C--Users-Alperen\27e5c754-d46c-4609-8e23-30686ca9c7e4\scratchpad\ikon-onizleme.png', [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
```

Beklenen: yeşil yuvarlatılmış kare içinde, 16 px'te bile kilit olarak okunan beyaz asma kilit (üstte kulp, altta gövde, gövdede koyu yeşil anahtar deliği). Kulp gövdeden kopuk ya da anahtar deliği kaybolmuş görünüyorsa yalnızca `Draw` içindeki oranları (`arc`, `body`, `d`) düzelt; testleri ve önizlemeyi yeniden çalıştır.

- [ ] **Step 6: Commit**

```bash
git add Toast.cs TrayPin.cs AutoStart.cs AppIcon.cs Tests.cs
git commit -F - <<'EOF'
Bildirim, ikon yerleştirme ve Windows ile başlat taşındı; kilit ikonu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Kokpit penceresi

**Files:**
- Create: `CockpitForm.cs`
- Modify: `Tests.cs`

**Interfaces:**
- Consumes: `PasswordStore` (`Entries`, `Find`, `Add`, `Update`, `Remove`, `Changed`, `NameComparer`), `Entry`, `StoreResult`, `AppIcon.ForWindow()`.
- Produces: `sealed class CockpitForm : Form` — `CockpitForm(PasswordStore store)`. Testler için `internal`: `const string Hidden = "••••••••"`, alanlar `NameBox`, `PasswordBox`, `SubmitButton`, `CancelEditButton`, `RevealBox`, `EntryList`, `DeleteButton`, `StatusLabel`; `Func<string, bool> Confirm`, `Action<string> ShowError`; `string EditingName { get; }`; `void Submit()`, `void BeginEdit(Entry entry)`, `void ResetToAdd()`, `void DeleteSelected()`, `void HandleEscape()`.

- [ ] **Step 1: Failing test** — `Main` içinde `Infrastructure(work);` altına `Cockpit(work);` ekle; `RunEntryExists` metodunun altına:

```csharp
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
                Expect("Sifreler gizli basliyor", form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[1].Text == CockpitForm.Hidden);

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
                Expect("Goster: sifreler acik", !form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[1].Text == "k3");
                form.RevealBox.Checked = false;
                Expect("Gizle: sifreler kapali", form.PasswordBox.UseSystemPasswordChar && form.EntryList.Items[0].SubItems[1].Text == CockpitForm.Hidden);

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

        private static string ListNames(CockpitForm form)
        {
            List<string> names = new List<string>();
            foreach (ListViewItem item in form.EntryList.Items) names.Add(item.Text);
            return string.Join("|", names);
        }
```

- [ ] **Step 2: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: FAIL, `error CS0246: The type or namespace name 'CockpitForm' could not be found`.

- [ ] **Step 3: Implement** — `CockpitForm.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace VpnSifreleri
{
    /// <summary>
    /// The cockpit: customer name and password boxes on top, the list below. Enter adds, or updates the entry picked
    /// from the list. The rules live in PasswordStore; this window only reports what they decided.
    /// </summary>
    internal sealed class CockpitForm : Form
    {
        /// <summary>A hidden password in the list; the same for every password, so lengths do not show.</summary>
        internal const string Hidden = "••••••••";

        private static readonly Color OkColor = Color.FromArgb(15, 123, 15);
        private static readonly Color ErrorColor = Color.FromArgb(196, 43, 28);

        internal readonly TextBox NameBox = new TextBox();
        internal readonly TextBox PasswordBox = new TextBox();
        internal readonly Button SubmitButton = new Button();
        internal readonly Button CancelEditButton = new Button();
        internal readonly CheckBox RevealBox = new CheckBox();
        internal readonly ListView EntryList = new ListView();
        internal readonly Button DeleteButton = new Button();
        internal readonly Label StatusLabel = new Label();

        /// <summary>Asks a yes/no question; tests answer it themselves.</summary>
        internal Func<string, bool> Confirm;
        /// <summary>Reports a failed save; tests collect the message.</summary>
        internal Action<string> ShowError;

        private readonly PasswordStore store;
        private readonly float scale;
        private readonly Font uiFont = SystemFonts.MessageBoxFont;
        private readonly Icon windowIcon = AppIcon.ForWindow();
        private readonly ColumnHeader nameColumn;
        private readonly ColumnHeader passwordColumn;
        private string editing;  // name of the entry in the boxes; null while adding
        private bool refreshing; // selection changes made by code, not by the user

        public CockpitForm(PasswordStore store)
        {
            this.store = store;
            Confirm = AskYesNo;
            ShowError = ShowErrorBox;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = screen.DpiX / 96f;

            Text = "VPN Şifreleri";
            Icon = windowIcon;
            Font = uiFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(520), Px(440));
            MinimumSize = new Size(Px(420), Px(320));

            NameBox.AccessibleName = "Müşteri";
            NameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            PasswordBox.AccessibleName = "Şifre";
            PasswordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            PasswordBox.UseSystemPasswordChar = true;
            SetUpButton(SubmitButton, "Ekle");
            SetUpButton(CancelEditButton, "Vazgeç");
            CancelEditButton.Visible = false;
            RevealBox.Text = "Göster";
            RevealBox.AutoSize = true;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.Margin = Padding.Empty;
            buttons.Controls.Add(SubmitButton);
            buttons.Controls.Add(CancelEditButton);

            TableLayoutPanel inputs = new TableLayoutPanel();
            inputs.Dock = DockStyle.Fill;
            inputs.AutoSize = true;
            inputs.ColumnCount = 3;
            inputs.RowCount = 3;
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputs.Controls.Add(Caption("Müşteri"), 0, 0);
            inputs.Controls.Add(Caption("Şifre"), 1, 0);
            inputs.Controls.Add(NameBox, 0, 1);
            inputs.Controls.Add(PasswordBox, 1, 1);
            inputs.Controls.Add(buttons, 2, 1);
            inputs.Controls.Add(RevealBox, 1, 2);

            EntryList.AccessibleName = "Kayıtlar";
            EntryList.Dock = DockStyle.Fill;
            EntryList.Margin = new Padding(3, Px(8), 3, Px(8));
            EntryList.View = View.Details;
            EntryList.FullRowSelect = true;
            EntryList.MultiSelect = false;
            EntryList.HideSelection = false;
            EntryList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            nameColumn = EntryList.Columns.Add("Müşteri", Px(240));
            passwordColumn = EntryList.Columns.Add("Şifre", Px(200));

            StatusLabel.AutoSize = true;
            StatusLabel.Anchor = AnchorStyles.Left;
            SetUpButton(DeleteButton, "Sil");

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(StatusLabel, 0, 0);
            footer.Controls.Add(DeleteButton, 1, 0);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(Px(12));
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(inputs, 0, 0);
            root.Controls.Add(EntryList, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);

            AcceptButton = SubmitButton;
            SubmitButton.Click += delegate { Submit(); };
            CancelEditButton.Click += delegate { ResetToAdd(); NameBox.Focus(); };
            DeleteButton.Click += delegate { DeleteSelected(); };
            RevealBox.CheckedChanged += delegate { ApplyReveal(); };
            EntryList.SelectedIndexChanged += OnListSelectionChanged;
            EntryList.KeyDown += OnListKeyDown;
            EntryList.ClientSizeChanged += delegate { FitPasswordColumn(); };
            store.Changed += OnStoreChanged;

            RefreshList();
            ResetToAdd();
        }

        /// <summary>Name of the entry being updated, or null while adding.</summary>
        internal string EditingName
        {
            get { return editing; }
        }

        /// <summary>Ekle / Güncelle, also Enter.</summary>
        internal void Submit()
        {
            string name = NameBox.Text, password = PasswordBox.Text;
            string original = editing;
            string done = original == null ? "eklendi" : "güncellendi";
            StoreResult result;
            if (!TryChange(() => original == null ? store.Add(name, password) : store.Update(original, name, password), out result)) return;

            if (result == StoreResult.NameExists && original == null)
            {
                // Adding a name that is already there: offer to replace its password, keep its spelling
                Entry existing = store.Find(name);
                if (!Confirm(existing.Name + " zaten var. Şifresi güncellensin mi?")) return;
                name = existing.Name;
                done = "güncellendi";
                if (!TryChange(() => store.Update(existing.Name, existing.Name, password), out result)) return;
            }

            switch (result)
            {
                case StoreResult.Ok:
                    Entry saved = store.Find(name);
                    ResetToAdd();
                    ShowStatus("✓ " + saved.Name + " " + done, false);
                    ScrollTo(saved);
                    NameBox.Focus();
                    break;
                case StoreResult.NameEmpty:
                    ShowStatus("Müşteri adı boş olamaz", true);
                    NameBox.Focus();
                    break;
                case StoreResult.PasswordEmpty:
                    ShowStatus("Şifre boş olamaz", true);
                    PasswordBox.Focus();
                    break;
                case StoreResult.NameExists:
                    ShowStatus("Bu isimde başka bir kayıt var", true);
                    NameBox.Focus();
                    break;
                default:
                    ResetToAdd();
                    ShowStatus("Kayıt bulunamadı", true);
                    break;
            }
        }

        /// <summary>Puts a listed entry in the boxes for updating.</summary>
        internal void BeginEdit(Entry entry)
        {
            editing = entry.Name;
            NameBox.Text = entry.Name;
            PasswordBox.Text = entry.Password;
            SubmitButton.Text = "Güncelle";
            CancelEditButton.Visible = true;
            DeleteButton.Enabled = true;
            ShowStatus("", false);
        }

        /// <summary>Back to adding: empty boxes, nothing picked in the list. Leaves the focus where it is.</summary>
        internal void ResetToAdd()
        {
            editing = null;
            NameBox.Text = "";
            PasswordBox.Text = "";
            SubmitButton.Text = "Ekle";
            CancelEditButton.Visible = false;
            DeleteButton.Enabled = false;
            refreshing = true;
            try
            {
                EntryList.SelectedItems.Clear();
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>Sil, also Delete in the list; asks first.</summary>
        internal void DeleteSelected()
        {
            string name = editing;
            if (name == null || !Confirm(name + " silinsin mi?")) return;
            StoreResult result;
            if (!TryChange(() => store.Remove(name), out result)) return;
            ResetToAdd();
            ShowStatus("✓ " + name + " silindi", false);
            NameBox.Focus();
        }

        /// <summary>Esc: stops updating, or closes the window when already adding.</summary>
        internal void HandleEscape()
        {
            if (editing == null)
            {
                Close();
                return;
            }
            ResetToAdd();
            NameBox.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            HandleEscape();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) store.Changed -= OnStoreChanged;
            base.Dispose(disposing);
            if (disposing)
            {
                windowIcon.Dispose();
                uiFont.Dispose();
            }
        }

        // Runs one store change. A failed save is shown and the boxes keep what was typed.
        private bool TryChange(Func<StoreResult> change, out StoreResult result)
        {
            result = StoreResult.NotFound;
            try
            {
                result = change();
                return true;
            }
            catch (IOException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            catch (CryptographicException ex) { ShowError("Kaydedilemedi: " + ex.Message); }
            return false;
        }

        private void RefreshList()
        {
            refreshing = true;
            EntryList.BeginUpdate();
            try
            {
                EntryList.Items.Clear();
                foreach (Entry entry in store.Entries)
                {
                    ListViewItem item = new ListViewItem(new[] { entry.Name, PasswordText(entry) });
                    item.Tag = entry;
                    item.Selected = editing != null && PasswordStore.NameComparer.Equals(entry.Name, editing);
                    EntryList.Items.Add(item);
                }
            }
            finally
            {
                EntryList.EndUpdate();
                refreshing = false;
            }
        }

        private void ApplyReveal()
        {
            PasswordBox.UseSystemPasswordChar = !RevealBox.Checked;
            foreach (ListViewItem item in EntryList.Items)
                item.SubItems[1].Text = PasswordText((Entry)item.Tag);
        }

        private string PasswordText(Entry entry)
        {
            return RevealBox.Checked ? entry.Password : Hidden;
        }

        private void ScrollTo(Entry entry)
        {
            foreach (ListViewItem item in EntryList.Items)
            {
                if (item.Tag != entry) continue;
                item.EnsureVisible();
                return;
            }
        }

        private void ShowStatus(string text, bool error)
        {
            StatusLabel.Text = text;
            StatusLabel.ForeColor = error ? ErrorColor : OkColor;
        }

        private void OnStoreChanged(object sender, EventArgs e)
        {
            RefreshList();
        }

        private void OnListSelectionChanged(object sender, EventArgs e)
        {
            if (refreshing) return;
            if (EntryList.SelectedItems.Count == 1) BeginEdit((Entry)EntryList.SelectedItems[0].Tag);
            else ResetToAdd();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            DeleteSelected();
            e.Handled = true;
        }

        // The password column takes the width the name column leaves
        private void FitPasswordColumn()
        {
            passwordColumn.Width = Math.Max(Px(80), EntryList.ClientSize.Width - nameColumn.Width - 1);
        }

        private bool AskYesNo(string question)
        {
            return MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowErrorBox(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetUpButton(Button button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(Px(84), 0);
            button.UseVisualStyleBackColor = true;
        }

        private static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(3, 3, 3, 0);
            return label;
        }

        private int Px(int logical)
        {
            return (int)Math.Round(logical * scale);
        }
    }
}
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.`

- [ ] **Step 5: Commit**

```bash
git add CockpitForm.cs Tests.cs
git commit -F - <<'EOF'
Kokpit penceresi: ekle, güncelle, sil, göster

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Tepsi uygulaması ve exe

**Files:**
- Create: `TrayApp.cs`, `AssemblyInfo.cs`
- Modify: `build.ps1` (tamamen yeni hali)

**Interfaces:**
- Consumes: bütün önceki görevlerin çıktıları (`PasswordStore.Open()`, `PasswordStore.DefaultPath`, `UnreadableMovedTo`, `Entries`, `Changed`; `TrayMenu.EntryItems`; `SecureClipboard`; `CockpitForm`; `Toast.Popup`; `TrayPin.PinWhenRegistered`; `AutoStart`; `AppIcon.ForTray`, `AppIcon.SaveIco`).
- Produces: `VpnSifreleri.exe` (`--make-icon <yol>` derleme adımı için; `--autostart` oturum açılışı için).

- [ ] **Step 1: `AssemblyInfo.cs`**

```csharp
using System.Reflection;

// Shown as the file description in Explorer, Task Manager and the Windows startup apps list
[assembly: AssemblyTitle("VPN Şifreleri")]
[assembly: AssemblyDescription("Müşterilerin VPN şifrelerini tepsiden tek tıkla panoya kopyalar")]
[assembly: AssemblyProduct("VPN Şifreleri")]
[assembly: AssemblyCopyright("Copyright © 2026 Alperen")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
```

- [ ] **Step 2: `TrayApp.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
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
                    // Unreadable and could not even be set aside: stop without touching it
                    MessageBox.Show("Şifre dosyası açılamadı. Dosyaya dokunulmadı:\n" + PasswordStore.DefaultPath + "\n\n" + ex.Message,
                        "VPN Şifreleri", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(new TrayContext(store, showCockpit, startedAtSignIn));
            }
        }
    }

    /// <summary>Tray icon whose right-click menu copies a customer's VPN password.</summary>
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
        private readonly Control invoker = new Control(); // brings the second-launch signal onto this thread
        private readonly RegisteredWaitHandle showCockpitWait;
        private CockpitForm cockpit;

        public TrayContext(PasswordStore store, WaitHandle showCockpit, bool startedAtSignIn)
        {
            this.store = store;
            manageItem = new MenuItem("Şifreleri yönet…", delegate { OpenCockpit(); });
            manageItem.DefaultItem = true; // bold: what a double-click on the icon does
            startupItem = new MenuItem("Windows ile başlat", ToggleStartup);
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

            TrayPin.PinWhenRegistered();
            if (store.UnreadableMovedTo != null) WarnUnreadable(store.UnreadableMovedTo);
            // Opened by hand: say where it went, and with nothing saved yet go straight to adding. At sign-in: stay quiet.
            if (!startedAtSignIn)
            {
                Toast.Popup("VPN Şifreleri çalışıyor", IconHint, true, 4000);
                if (store.Entries.Count == 0) OpenCockpit();
            }
        }

        protected override void ExitThreadCore()
        {
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

        // Customers first, in the store's alphabetical order, then the fixed items
        private void BuildMenu()
        {
            menu.MenuItems.Clear();
            menu.MenuItems.AddRange(TrayMenu.EntryItems(store.Entries, Copy));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(manageItem);
            menu.MenuItems.Add(startupItem);
            menu.MenuItems.Add(exitItem);
        }

        // The notice names the customer, never the password: the screen may be shared
        private void Copy(Entry entry)
        {
            try
            {
                clipboard.Copy(entry.Password);
                Toast.Popup("Şifre kopyalandı", entry.Name, true);
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
            else if (cockpit.WindowState == FormWindowState.Minimized)
            {
                cockpit.WindowState = FormWindowState.Normal;
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

        private static void WarnUnreadable(string movedTo)
        {
            MessageBox.Show("Şifre dosyası okunamadı; başka bir bilgisayardan ya da Windows kullanıcısından gelmiş olabilir.\n\n"
                + "Dosya silinmedi, şu adla saklandı:\n" + movedTo + "\n\nUygulama boş listeyle açılıyor.",
                "VPN Şifreleri", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
```

- [ ] **Step 3: `build.ps1`'in son hali** (tamamını değiştir):

```powershell
# Builds VpnSifreleri.exe with the C# compiler that ships with Windows (.NET Framework 4.x),
# so nothing has to be installed. The tests run first; a failure stops the build.
# Keep this file ASCII: Windows PowerShell 5.1 reads scripts without a BOM as ANSI.
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$obj = Join-Path $root 'obj'
$exe = Join-Path $root 'VpnSifreleri.exe'
$ico = Join-Path $obj 'VpnSifreleri.ico'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$common = '/nologo', '/codepage:65001', '/optimize+', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Security.dll'
# Every .cs file goes into both builds, except the other build's entry points
$sources = @(Get-ChildItem $root -Filter '*.cs' | Where-Object { $_.Extension -eq '.cs' } | ForEach-Object { $_.FullName })
$testSources = @($sources | Where-Object { (Split-Path $_ -Leaf) -notin 'TrayApp.cs', 'AssemblyInfo.cs' })
$appSources = @($sources | Where-Object { (Split-Path $_ -Leaf) -ne 'Tests.cs' })

function Invoke-Csc([string[]] $arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Derleme hatasi' }
}

New-Item -ItemType Directory -Force $obj | Out-Null

# 1. Tests
Invoke-Csc (@('/target:exe', "/out:$obj\Tests.exe") + $testSources)
& "$obj\Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Testler basarisiz, exe uretilmedi' }

# 2. The app draws its own icon: build once without it and export the .ico
Invoke-Csc (@('/target:winexe', "/out:$obj\IconMaker.exe") + $appSources)
$maker = Start-Process -FilePath "$obj\IconMaker.exe" -ArgumentList '--make-icon', "`"$ico`"" -Wait -PassThru
if ($maker.ExitCode -ne 0) { throw 'Ikon uretilemedi' }

# 3. Final exe; a running copy would keep the file locked
$running = Get-Process -Name 'VpnSifreleri' -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 5
}
Invoke-Csc (@('/target:winexe', "/win32icon:$ico", "/out:$exe") + $appSources)
Write-Host "Hazir: $exe"
```

- [ ] **Step 4: Run** `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — Expected: `Tum testler gecti.` ve `Hazir: C:\Users\Alperen\projects\VpnSifreleri\VpnSifreleri.exe`.

- [ ] **Step 5: Duman testi** — Önce kullanıcı verisi olmadığını doğrula; varsa **dur ve kullanıcıya sor**:

```powershell
Test-Path "$env:APPDATA\VpnSifreleri"   # Expected: False
Start-Process 'C:\Users\Alperen\projects\VpnSifreleri\VpnSifreleri.exe'
Start-Sleep -Seconds 3
Get-Process VpnSifreleri | Select-Object Id, Responding   # Expected: tek süreç, Responding True
Stop-Process -Name VpnSifreleri
Test-Path "$env:APPDATA\VpnSifreleri"   # Expected: False (kayıt eklenmeden dosya yazılmaz)
```

- [ ] **Step 6: Commit**

```bash
git add TrayApp.cs AssemblyInfo.cs build.ps1
git commit -F - <<'EOF'
Tepsi uygulaması ve exe derlemesi

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Uçtan uca doğrulama ve temizlik

Gerçek exe, gerçek tepsi, gerçek pano. Doğrulama sırasında panodaki metin birkaç kez test şifresiyle değişir ve temizlenir; ekranda kokpit ve menü açılıp kapanır. Başlamadan kullanıcıya bunu söyle. Kayıtlar `Deneme` adlı sahte müşterilerdir; sonunda `%APPDATA%\VpnSifreleri` tamamen silinir.

**Files:**
- Create (repo dışı, scratchpad): `C:\Users\Alperen\AppData\Local\Temp\claude\C--Users-Alperen\27e5c754-d46c-4609-8e23-30686ca9c7e4\scratchpad\verify.ps1`

- [ ] **Step 1: Ön koşullar** — `Test-Path "$env:APPDATA\VpnSifreleri"` → `False`; `Get-Process VpnSifreleri -ErrorAction SilentlyContinue` → boş; `(Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).VpnSifreleri` → boş. Biri sağlanmıyorsa dur ve kullanıcıya sor.

- [ ] **Step 2: Yardımcı betik** — `verify.ps1`:

```powershell
# End-to-end helpers for VPN Sifreleri; not part of the repo. Dot-source from Windows PowerShell 5.1 (STA).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
if (-not ('VerifyNative' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class VerifyNative
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);

    // What a right-click on the tray icon delivers: NotifyIcon's callback message (WM_USER + 1024) with WM_RBUTTONUP,
    // posted to the app's untitled WinForms windows (NotifyIcon's hidden window is one of them)
    public static void RightClickTray(uint processId)
    {
        List<IntPtr> targets = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            StringBuilder cls = new StringBuilder(256), title = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            GetWindowText(hwnd, title, title.Capacity);
            if (pid == processId && cls.ToString().StartsWith("WindowsForms10.") && title.Length == 0) targets.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        foreach (IntPtr hwnd in targets) PostMessage(hwnd, 0x0400 + 1024, IntPtr.Zero, (IntPtr)0x0205);
    }

    // BM_CLICK without waiting: a button that opens a message box would block a synchronous UIA Invoke
    public static void PostClick(IntPtr button)
    {
        PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
    }

    public static string ForegroundTitle()
    {
        StringBuilder text = new StringBuilder(256);
        GetWindowText(GetForegroundWindow(), text, text.Capacity);
        return text.ToString();
    }

    // Raw bytes of a registered clipboard format, like "00-00-00-00", or "yok"
    public static string ClipboardBytes(string format)
    {
        if (!OpenClipboard(IntPtr.Zero)) return "pano acilamadi";
        try
        {
            IntPtr mem = GetClipboardData(RegisterClipboardFormat(format));
            if (mem == IntPtr.Zero) return "yok";
            byte[] bytes = new byte[(int)GlobalSize(mem)];
            Marshal.Copy(GlobalLock(mem), bytes, 0, bytes.Length);
            GlobalUnlock(mem);
            return BitConverter.ToString(bytes);
        }
        finally { CloseClipboard(); }
    }
}
'@
}
[void][VerifyNative]::SetProcessDPIAware()

$A = [Windows.Automation.AutomationElement]
$Exe = 'C:\Users\Alperen\projects\VpnSifreleri\VpnSifreleri.exe'
$DataDir = Join-Path $env:APPDATA 'VpnSifreleri'
$Shots = 'C:\Users\Alperen\AppData\Local\Temp\claude\C--Users-Alperen\27e5c754-d46c-4609-8e23-30686ca9c7e4\scratchpad'

function Get-App { Get-Process -Name 'VpnSifreleri' -ErrorAction SilentlyContinue | Select-Object -First 1 }

function Wait-For([scriptblock] $probe, [int] $timeoutMs = 8000) {
    $deadline = [DateTime]::Now.AddMilliseconds($timeoutMs)
    do {
        $found = & $probe
        if ($found) { return $found }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::Now -lt $deadline)
    return $null
}

function New-Cond($property, $value) { New-Object Windows.Automation.PropertyCondition($property, $value) }

function Find-Named($parent, [string] $name, $controlType) {
    $cond = New-Object Windows.Automation.AndCondition((New-Cond $A::NameProperty $name), (New-Cond $A::ControlTypeProperty $controlType))
    $parent.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
}

# The cockpit: the app's top-level window titled "VPN Şifreleri" that is not a message box
function Get-Cockpit {
    $app = Get-App
    if (-not $app) { return $null }
    $cond = New-Object Windows.Automation.AndCondition((New-Cond $A::ProcessIdProperty $app.Id), (New-Cond $A::NameProperty 'VPN Şifreleri'))
    foreach ($window in $A::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $cond)) {
        if ($window.Current.ClassName -ne '#32770') { return $window }
    }
    return $null
}

# A message box of the app: top level, or owned by the cockpit
function Get-Dialog {
    $app = Get-App
    if (-not $app) { return $null }
    $cond = New-Object Windows.Automation.AndCondition((New-Cond $A::ProcessIdProperty $app.Id), (New-Cond $A::ClassNameProperty '#32770'))
    $dialog = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
    if (-not $dialog) {
        $cockpit = Get-Cockpit
        if ($cockpit) { $dialog = $cockpit.FindFirst([Windows.Automation.TreeScope]::Children, $cond) }
    }
    return $dialog
}

function Get-DialogText($dialog) {
    $texts = $dialog.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Cond $A::ControlTypeProperty ([Windows.Automation.ControlType]::Text)))
    ($texts | ForEach-Object { $_.Current.Name }) -join ' '
}

function Press($parent, [string] $name) {
    $button = Find-Named $parent $name ([Windows.Automation.ControlType]::Button)
    if (-not $button) { throw "Dugme yok: $name" }
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# For buttons that open a message box
function Press-Async($parent, [string] $name) {
    $button = Find-Named $parent $name ([Windows.Automation.ControlType]::Button)
    if (-not $button) { throw "Dugme yok: $name" }
    [VerifyNative]::PostClick([IntPtr]$button.Current.NativeWindowHandle)
}

function Set-Box($cockpit, [string] $name, [string] $text) {
    $box = Find-Named $cockpit $name ([Windows.Automation.ControlType]::Edit)
    if (-not $box) { throw "Kutu yok: $name" }
    $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Get-ListNames($cockpit) {
    $list = Find-Named $cockpit 'Kayıtlar' ([Windows.Automation.ControlType]::List)
    $items = $list.FindAll([Windows.Automation.TreeScope]::Children, (New-Cond $A::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem)))
    @($items | ForEach-Object { $_.Current.Name })
}

function Select-ListItem($cockpit, [string] $name) {
    $item = Find-Named $cockpit $name ([Windows.Automation.ControlType]::ListItem)
    if (-not $item) { throw "Listede yok: $name" }
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Open-TrayMenu {
    [VerifyNative]::RightClickTray([uint32](Get-App).Id)
    $menu = Wait-For { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Cond $A::ClassNameProperty '#32768')) }
    if (-not $menu) { throw 'Tepsi menusu acilmadi' }
    return $menu
}

function Get-MenuNames($menu) {
    $items = $menu.FindAll([Windows.Automation.TreeScope]::Children, (New-Cond $A::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))
    @($items | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}

function Invoke-MenuItem($menu, [string] $name) {
    $item = Find-Named $menu $name ([Windows.Automation.ControlType]::MenuItem)
    if (-not $item) { throw "Menude yok: $name" }
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Save-Shot([string] $name, [System.Drawing.Rectangle] $area) {
    $bmp = New-Object System.Drawing.Bitmap($area.Width, $area.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($area.Location, [System.Drawing.Point]::Empty, $area.Size)
    $g.Dispose()
    $path = Join-Path $Shots ($name + '.png')
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

function Save-WindowShot([string] $name, $element) {
    $r = $element.Current.BoundingRectangle
    Save-Shot $name (New-Object System.Drawing.Rectangle([int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height))
}

# The notice sits above the tray, bottom right of the primary screen
function Save-ToastShot([string] $name) {
    $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    Save-Shot $name (New-Object System.Drawing.Rectangle(($work.Right - 520), ($work.Bottom - 150), 520, 150))
}
```

Her adımdaki PowerShell komutu `. 'C:\Users\Alperen\AppData\Local\Temp\claude\C--Users-Alperen\27e5c754-d46c-4609-8e23-30686ca9c7e4\scratchpad\verify.ps1'` ile başlar (kabuk durumu komutlar arasında korunmaz). Aşağıda bu satır `. verify.ps1` diye kısaltılmıştır.

- [ ] **Step 3: İlk açılış** — boş listeyle elle açılış kokpiti açar ve bildirim gösterir:

```powershell
. verify.ps1
Start-Process $Exe
$cockpit = Wait-For { Get-Cockpit }
'kokpit acildi: ' + [bool]$cockpit
Save-ToastShot 'acilis-bildirimi'
$tray = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Cond $A::ClassNameProperty 'Shell_TrayWnd'))
'tepside ikon: ' + [bool](Wait-For { Find-Named $tray 'VPN Şifreleri' ([Windows.Automation.ControlType]::Button) } 12000)
```

Expected: `kokpit acildi: True`, `tepside ikon: True`. `acilis-bildirimi.png`'de "VPN Şifreleri çalışıyor / Saatin yanındaki kilit ikonuna sağ tıkla".

- [ ] **Step 4: Kokpitten ekleme**

```powershell
. verify.ps1
$cockpit = Get-Cockpit
Set-Box $cockpit 'Müşteri' 'Üzüm Deneme'; Set-Box $cockpit 'Şifre' 'Uzum-Sifre-1'; Press $cockpit 'Ekle'
Set-Box $cockpit 'Müşteri' 'Açelya Deneme'; Set-Box $cockpit 'Şifre' 'Acelya-Sifre-2'; Press $cockpit 'Ekle'
'liste: ' + ((Get-ListNames $cockpit) -join ' | ')
Save-WindowShot 'kokpit-eklendi' $cockpit
```

Expected: `liste: Açelya Deneme | Üzüm Deneme`. Görüntüde: üstte kutular + Ekle + Göster, listede iki kayıt ve `••••••••`, altta yeşil "✓ Açelya Deneme eklendi", sağda Sil. (Şifre kutusuna `SetValue` reddedilirse: kutuya `SetFocus()` ve `[System.Windows.Forms.SendKeys]::SendWait('Uzum-Sifre-1')`.)

- [ ] **Step 5: Menü, kopyalama ve bildirim**

```powershell
. verify.ps1
$menu = Open-TrayMenu
'menu: ' + ((Get-MenuNames $menu) -join ' | ')
Invoke-MenuItem $menu 'Üzüm Deneme'
Start-Sleep -Milliseconds 400
'pano: ' + (Get-Clipboard)
foreach ($f in 'ExcludeClipboardContentFromMonitorProcessing', 'CanIncludeInClipboardHistory', 'CanUploadToCloudClipboard') { $f + ': ' + [VerifyNative]::ClipboardBytes($f) }
Save-ToastShot 'kopyalandi-bildirimi'
```

Expected: `menu: Açelya Deneme | Üzüm Deneme | Şifreleri yönet… | Windows ile başlat | Çıkış`, `pano: Uzum-Sifre-1`, üç biçim `00-00-00-00`. Görüntüde "Şifre kopyalandı / Üzüm Deneme"; `Uzum-Sifre-1` **görünmemeli**.

- [ ] **Step 6: 30 saniye sonra temizleme**

```powershell
Start-Sleep -Seconds 31
'pano: [' + (Get-Clipboard) + ']'
```

Expected: `pano: []`.

- [ ] **Step 7: Aynı ad, gerçek onay penceresiyle**

```powershell
. verify.ps1
$cockpit = Get-Cockpit
Set-Box $cockpit 'Müşteri' 'ÜZÜM DENEME'; Set-Box $cockpit 'Şifre' 'Uzum-Sifre-3'
Press-Async $cockpit 'Ekle'
$dialog = Wait-For { Get-Dialog }
'soru: ' + (Get-DialogText $dialog)
Press $dialog 'Evet'
Start-Sleep -Milliseconds 300
$menu = Open-TrayMenu
Invoke-MenuItem $menu 'Üzüm Deneme'
Start-Sleep -Milliseconds 400
'pano: ' + (Get-Clipboard)
```

Expected: `soru: Üzüm Deneme zaten var. Şifresi güncellensin mi?`, `pano: Uzum-Sifre-3`.

- [ ] **Step 8: Güncelle, yeniden adlandır, sil**

```powershell
. verify.ps1
$cockpit = Get-Cockpit
Select-ListItem $cockpit 'Açelya Deneme'
Save-WindowShot 'kokpit-guncelle' $cockpit
Set-Box $cockpit 'Müşteri' 'Açelya Test'
Press $cockpit 'Güncelle'
'liste: ' + ((Get-ListNames $cockpit) -join ' | ')
Select-ListItem $cockpit 'Açelya Test'
Press-Async $cockpit 'Sil'
$dialog = Wait-For { Get-Dialog }
'soru: ' + (Get-DialogText $dialog)
Press $dialog 'Evet'
Start-Sleep -Milliseconds 300
'liste: ' + ((Get-ListNames $cockpit) -join ' | ')
```

Expected: `kokpit-guncelle.png`'de kutular dolu, "Güncelle" ve "Vazgeç" görünür; ardından `liste: Açelya Test | Üzüm Deneme`, `soru: Açelya Test silinsin mi?`, `liste: Üzüm Deneme`.

- [ ] **Step 9: Göster**

```powershell
. verify.ps1
$cockpit = Get-Cockpit
$reveal = Find-Named $cockpit 'Göster' ([Windows.Automation.ControlType]::CheckBox)
$reveal.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
Save-WindowShot 'kokpit-goster' $cockpit
$reveal.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
```

Expected: `kokpit-goster.png`'de listede `Uzum-Sifre-3` açık yazılı.

- [ ] **Step 10: İkinci açılış kokpiti öne getirir**

```powershell
. verify.ps1
(Get-Cockpit).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Milliseconds 300
Start-Process $Exe
$cockpit = Wait-For { Get-Cockpit }
Start-Sleep -Milliseconds 300
'kokpit: ' + [bool]$cockpit + ', onde: ' + [VerifyNative]::ForegroundTitle() + ', surec sayisi: ' + @(Get-Process VpnSifreleri).Count
```

Expected: `kokpit: True, onde: VPN Şifreleri, surec sayisi: 1`.

- [ ] **Step 11: Windows ile başlat**

```powershell
. verify.ps1
Invoke-MenuItem (Open-TrayMenu) 'Windows ile başlat'
'acik: ' + (Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).VpnSifreleri
Invoke-MenuItem (Open-TrayMenu) 'Windows ile başlat'
'kapali: [' + (Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).VpnSifreleri + ']'
```

Expected: `acik: "C:\Users\Alperen\projects\VpnSifreleri\VpnSifreleri.exe" --autostart`, `kapali: []`.

- [ ] **Step 12: Okunamayan dosya açılışta kenara alınır**

```powershell
. verify.ps1
Invoke-MenuItem (Open-TrayMenu) 'Çıkış'
Wait-For { -not (Get-App) } | Out-Null
[IO.File]::WriteAllBytes((Join-Path $DataDir 'sifreler.dat'), [byte[]](7, 7, 7, 7))
Start-Process $Exe
$dialog = Wait-For { Get-Dialog }
'uyari: ' + (Get-DialogText $dialog)
Press $dialog 'Tamam'
$aside = Get-ChildItem $DataDir -Filter 'sifreler.dat.okunamadi-*'
'kenarda: ' + $aside.Name + ' baytlar: ' + (([IO.File]::ReadAllBytes($aside.FullName)) -join ',')
```

Expected: uyarı metni "Şifre dosyası okunamadı; …" ile başlar; `kenarda: sifreler.dat.okunamadi-… baytlar: 7,7,7,7`; ardından boş listeli kokpit açılır.

- [ ] **Step 13: Çıkış ve temizlik**

```powershell
. verify.ps1
Invoke-MenuItem (Open-TrayMenu) 'Çıkış'
Wait-For { -not (Get-App) } | Out-Null
'surec: ' + [bool](Get-App)
Remove-Item $DataDir -Recurse -Force
'veri klasoru: ' + (Test-Path $DataDir) + ', Run: [' + (Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).VpnSifreleri + ']'
```

Expected: `surec: False`, `veri klasoru: False, Run: []`.

- [ ] **Step 14: Kullanıcıya teslim** — Bulunan sorunlar ayrı commit'lerle düzeltildiyse build'i yeniden çalıştır ve ilgili adımları tekrarla. Uygulamayı kullanıcı için başlat (`Start-Process $Exe`; boş listeyle kokpit açılır). Kullanıcıya Türkçe olarak: exe'nin yeri, "Windows ile başlat"ı menüden açabileceği, OneNote'u şimdilik yedek tutması ve kendi elle denemesi gerekenler (gerçek VPN programına `Ctrl+V`, Win+V geçmişinde şifrenin görünmemesi).
