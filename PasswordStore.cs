using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VpnSifreleri
{
    /// <summary>One saved login: a name the user picks, an optional user name and the password.</summary>
    internal sealed class Entry
    {
        private readonly string name;
        private readonly string username;
        private readonly string password;

        public Entry(string name, string password)
            : this(name, "", password)
        {
        }

        public Entry(string name, string username, string password)
        {
            this.name = name;
            this.username = username ?? "";
            this.password = password;
        }

        public string Name
        {
            get { return name; }
        }

        public string Username
        {
            get { return username; }
        }

        public string Password
        {
            get { return password; }
        }

        /// <summary>
        /// What a click copies: the password alone, or the user name and the password on two lines, which SAP's logon
        /// screen spreads over its two boxes when pasted.
        /// </summary>
        public string ClipboardText
        {
            get { return username.Length == 0 ? password : username + "\r\n" + password; }
        }
    }

    internal enum StoreResult { Ok, NameEmpty, PasswordEmpty, NameExists, NotFound, MultiLine }

    /// <summary>sifreler.dat was saved by a newer version of the app. It stays untouched; the app should be updated.</summary>
    internal sealed class NewerFormatException : Exception
    {
        public NewerFormatException(int format)
            : base("Bu dosya VPN Şifreleri'nin daha yeni bir sürümüyle kaydedilmiş (biçim " + format + "). Uygulamayı güncelle.")
        {
        }
    }

    /// <summary>
    /// The saved logins, always in Turkish alphabetical order. A change reaches the list only after it reached the
    /// disk, so a failed save leaves both as they were. The file is encrypted with DPAPI for the signed-in Windows
    /// user: nothing to type, and a copy taken to another PC or user cannot be read.
    /// </summary>
    internal sealed class PasswordStore
    {
        /// <summary>Turkish rules, ignoring case: sorting and the duplicate check agree (ÇAĞLA = çağla, I ≠ İ).</summary>
        public static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), true);

        private static readonly byte[] AppEntropy = Encoding.UTF8.GetBytes("VpnSifreleri/sifreler.dat");
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VPNS");
        // Format 1 (1.0.0): name + password. Format 2: name + user name + password.
        private const int FormatVersion = 2;

        private readonly string path;
        private readonly byte[] entropy;
        private List<Entry> entries = new List<Entry>();
        private string unreadableMovedTo;
        private string backupMovedTo;
        private bool restoredFromBackup;
        private string recoveredFrom;
        private string previousMovedTo;

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

        /// <summary>
        /// Reads the file if there is one. A file this user cannot decrypt is never written over: it is renamed to
        /// sifreler.dat.okunamadi-yyyyMMdd-HHmmss. When the file is missing or unreadable, the previous version in .bak
        /// takes its place (a crash between File.Replace's two renames leaves exactly that); an unreadable .bak is set
        /// aside the same way, and the list starts empty. A file from a newer app version throws NewerFormatException
        /// and stays where it is. Then a newer list that an older version set aside comes back (see
        /// TakeBackNewerSetAside). If a rename fails, this throws.
        /// </summary>
        internal static PasswordStore Open(string path, byte[] entropy)
        {
            PasswordStore store = new PasswordStore(path, entropy);
            store.LoadFileOrBackup();
            store.TakeBackNewerSetAside();
            return store;
        }

        private void LoadFileOrBackup()
        {
            if (File.Exists(path))
            {
                List<Entry> loaded = TryDecode(File.ReadAllBytes(path), entropy);
                if (loaded != null)
                {
                    entries = loaded;
                    return;
                }
                unreadableMovedTo = SetAside(path);
            }

            string backup = path + ".bak";
            if (!File.Exists(backup)) return;
            List<Entry> previous = TryDecode(File.ReadAllBytes(backup), entropy);
            if (previous == null)
            {
                backupMovedTo = SetAside(backup);
                return;
            }
            // The backup becomes the file again, so the next save keeps it as .bak instead of replacing it
            File.Move(backup, path);
            entries = previous;
            restoredFromBackup = true;
        }

        // Version 1.0.0 cannot read format 2. Opened after an update, it renames such files to *.okunamadi-* and starts
        // empty, or goes back to a format 1 .bak. Those files are readable here: the newest of them comes back when it
        // is newer than what was opened, and the file it replaces is kept as sifreler.dat.onceki-yyyyMMdd-HHmmss.
        private void TakeBackNewerSetAside()
        {
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder)) return;
            DateTime newest = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            string best = null;
            List<Entry> bestEntries = null;
            foreach (string candidate in Directory.GetFiles(folder, Path.GetFileName(path) + "*.okunamadi-*"))
            {
                DateTime written = File.GetLastWriteTimeUtc(candidate);
                if (written <= newest) continue;
                List<Entry> readable = ReadSetAside(candidate, entropy);
                if (readable == null) continue;
                best = candidate;
                bestEntries = readable;
                newest = written;
            }
            if (best == null) return;
            if (File.Exists(path)) previousMovedTo = MoveAside(path, ".onceki-");
            File.Move(best, path);
            entries = bestEntries;
            recoveredFrom = best;
        }

        // A set-aside file this version cannot use (another user's, damaged, locked, or from a newer version) stays aside
        private static List<Entry> ReadSetAside(string file, byte[] entropy)
        {
            try
            {
                return TryDecode(File.ReadAllBytes(file), entropy);
            }
            catch (NewerFormatException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>Where an unreadable file was set aside while opening, or null.</summary>
        public string UnreadableMovedTo
        {
            get { return unreadableMovedTo; }
        }

        /// <summary>Where an unreadable .bak was set aside while opening, or null.</summary>
        public string BackupMovedTo
        {
            get { return backupMovedTo; }
        }

        /// <summary>The file was missing or unreadable and the list came back from .bak.</summary>
        public bool RestoredFromBackup
        {
            get { return restoredFromBackup; }
        }

        /// <summary>A newer list that an older app version had set aside and that came back while opening, or null.</summary>
        public string RecoveredFrom
        {
            get { return recoveredFrom; }
        }

        /// <summary>Where the older file went when RecoveredFrom replaced it, or null.</summary>
        public string PreviousMovedTo
        {
            get { return previousMovedTo; }
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

        /// <summary>An entry without a user name: a click copies only the password.</summary>
        public StoreResult Add(string name, string password)
        {
            return Add(name, "", password);
        }

        public StoreResult Add(string name, string username, string password)
        {
            name = Clean(name);
            username = Clean(username);
            password = Clean(password);
            StoreResult invalid = Validate(name, username, password);
            if (invalid != StoreResult.Ok) return invalid;
            if (IndexOf(name) >= 0) return StoreResult.NameExists;

            List<Entry> next = new List<Entry>(entries);
            next.Add(new Entry(name, username, password));
            Commit(next);
            return StoreResult.Ok;
        }

        /// <summary>Same as the four-argument Update with an empty user name.</summary>
        public StoreResult Update(string oldName, string newName, string password)
        {
            return Update(oldName, newName, "", password);
        }

        /// <summary>Rewrites an entry; a name may change only its case, or to one not in use.</summary>
        public StoreResult Update(string oldName, string newName, string username, string password)
        {
            int index = IndexOf(Clean(oldName));
            if (index < 0) return StoreResult.NotFound;
            newName = Clean(newName);
            username = Clean(username);
            password = Clean(password);
            StoreResult invalid = Validate(newName, username, password);
            if (invalid != StoreResult.Ok) return invalid;
            int clash = IndexOf(newName);
            if (clash >= 0 && clash != index) return StoreResult.NameExists;

            List<Entry> next = new List<Entry>(entries);
            next[index] = new Entry(newName, username, password);
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

        /// <summary>Removes what pasting brings along at either end: spaces, line breaks and zero-width characters.</summary>
        internal static string Clean(string value)
        {
            if (value == null) return "";
            int start = 0, end = value.Length;
            while (start < end && IsInvisible(value[start])) start++;
            while (end > start && IsInvisible(value[end - 1])) end--;
            return value.Substring(start, end - start);
        }

        private static bool IsInvisible(char c)
        {
            return char.IsWhiteSpace(c) || c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\u2060' || c == '\uFEFF';
        }

        private static StoreResult Validate(string name, string username, string password)
        {
            if (name.Length == 0) return StoreResult.NameEmpty;
            if (password.Length == 0) return StoreResult.PasswordEmpty;
            // The copied text separates user name and password with a line break; a field must not bring its own
            if (HasLineBreak(name) || HasLineBreak(username) || HasLineBreak(password)) return StoreResult.MultiLine;
            return StoreResult.Ok;
        }

        private static bool HasLineBreak(string value)
        {
            return value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;
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
                    w.Write(entry.Username);
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
                if (!SameBytes(r.ReadBytes(Magic.Length), Magic)) throw new InvalidDataException("sifreler.dat biçimi tanınmadı");
                int format = r.ReadInt32();
                if (format > FormatVersion) throw new NewerFormatException(format);
                if (format < 1) throw new InvalidDataException("sifreler.dat biçim numarası geçersiz");
                int count = r.ReadInt32();
                List<Entry> list = new List<Entry>();
                for (int i = 0; i < count; i++)
                {
                    string name = r.ReadString();
                    string username = format >= 2 ? r.ReadString() : "";
                    list.Add(new Entry(name, username, r.ReadString()));
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("sifreler.dat sonunda fazladan veri var");
                list.Sort(CompareByName);
                return list;
            }
        }

        // null when the bytes are not a list this Windows user saved with this entropy. NewerFormatException is not
        // caught: such a file is readable, just not by this version, so it must not be set aside.
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

        // Renamed to <name>.okunamadi-yyyyMMdd-HHmmss next to itself; returns the new path
        private static string SetAside(string file)
        {
            return MoveAside(file, ".okunamadi-");
        }

        private static string MoveAside(string file, string label)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string aside = file + label + stamp;
            for (int n = 2; File.Exists(aside); n++) aside = file + label + stamp + "-" + n;
            File.Move(file, aside);
            return aside;
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
