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

        private readonly Action<DataObject> put;
        private readonly Func<string> read;
        private readonly Action clear;
        private readonly Timer clearTimer = new Timer();
        private string copied;

        public SecureClipboard()
            : this(PutOnClipboard, ReadClipboard, Clipboard.Clear)
        {
        }

        /// <summary>The clipboard calls can be swapped, so tests never touch the real clipboard.</summary>
        internal SecureClipboard(Action<DataObject> put, Func<string> read, Action clear)
        {
            this.put = put;
            this.read = read;
            this.clear = clear;
            clearTimer.Interval = ClearAfterMs;
            clearTimer.Tick += delegate { ClearIfUnchanged(); };
        }

        /// <summary>A copied text is waiting to be cleared.</summary>
        internal bool HoldsCopy
        {
            get { return copied != null; }
        }

        /// <summary>Throws ExternalException when the text could not be put on the clipboard at all.</summary>
        public void Copy(string text)
        {
            try
            {
                put(CreateDataObject(text));
            }
            catch (ExternalException)
            {
                // Placing and flushing are two steps that fail separately: if only the flush failed, the text is on
                // the clipboard after all and has to be cleared like any other copy
                if (!ShouldClear(ReadOrNull(), text)) throw;
            }
            copied = text;
            clearTimer.Stop();
            clearTimer.Start();
        }

        /// <summary>Empties the clipboard if it still holds the copied text; runs after 30 s and on exit.</summary>
        public void ClearIfUnchanged()
        {
            clearTimer.Stop();
            if (copied == null) return;
            string expected = copied;
            copied = null;
            try
            {
                if (ShouldClear(read(), expected)) clear();
            }
            catch (ExternalException)
            {
                // Another app holds the clipboard right now: leave it rather than wait
            }
        }

        private string ReadOrNull()
        {
            try
            {
                return read();
            }
            catch (ExternalException)
            {
                return null;
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

        public void Dispose()
        {
            clearTimer.Dispose();
        }

        // Retries cover the moment another app holds the clipboard open
        private static void PutOnClipboard(DataObject data)
        {
            Clipboard.SetDataObject(data, true, 10, 50);
        }

        private static string ReadClipboard()
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }

        private static MemoryStream Dword(int value)
        {
            return new MemoryStream(BitConverter.GetBytes(value));
        }
    }
}
