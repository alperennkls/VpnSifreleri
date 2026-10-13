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

        /// <summary>Notice caption after a copy; the notice itself shows only the entry's name.</summary>
        internal static string CopiedCaption(Entry entry)
        {
            return entry.Username.Length == 0 ? "Şifre kopyalandı" : "Kullanıcı adı ve şifre kopyalandı";
        }
    }
}
