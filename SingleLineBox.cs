using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VpnSifreleri
{
    /// <summary>
    /// A one-line box that refuses to paste several lines. The plain Windows box keeps only the first line, so pasting
    /// a user name and a password together into the password box would silently save the user name as the password.
    /// </summary>
    internal sealed class SingleLineBox : TextBox
    {
        private const int WM_PASTE = 0x0302;

        /// <summary>Text on the clipboard; tests answer it themselves.</summary>
        internal Func<string> ReadClipboard = ReadClipboardText;

        /// <summary>Raised instead of pasting text with more than one line.</summary>
        internal event EventHandler PasteRefused;

        /// <summary>Several lines even after trimming what copying brings along at the ends (a trailing line break).</summary>
        internal static bool Refuses(string text)
        {
            string clean = PasswordStore.Clean(text);
            return clean.IndexOf('\r') >= 0 || clean.IndexOf('\n') >= 0;
        }

        // Ctrl+V, Shift+Insert and the box's own context menu all arrive as WM_PASTE
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PASTE && Refuses(ReadClipboard()))
            {
                EventHandler handler = PasteRefused;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        private static string ReadClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                return null;
            }
        }
    }
}
