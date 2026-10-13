using System;
using System.Diagnostics;
using System.IO;
using System.Security;
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
                if (enabled) key.SetValue(ValueName, RunValue());
                else key.DeleteValue(ValueName, false);
            }
        }

        /// <summary>
        /// An update saved somewhere else would leave Windows starting the old copy at sign-in, and an older version
        /// cannot read the newer file format. When the Run entry points at a missing or older exe, it moves to this one.
        /// </summary>
        public static void FollowThisExe()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string value = key == null ? null : key.GetValue(ValueName) as string;
                    if (value == null || !ShouldFollow(value, Application.ExecutablePath, UpdateCheck.Current, VersionOf)) return;
                    key.SetValue(ValueName, RunValue());
                }
            }
            // Only a convenience; never let it take the app down
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        internal static bool ShouldFollow(string runValue, string exePath, Version thisVersion, Func<string, Version> versionOf)
        {
            string target = TargetOf(runValue);
            if (string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase)) return false;
            Version other = versionOf(target);
            return other == null || other < thisVersion;
        }

        // The exe path in a Run value: "C:\path\app.exe" --autostart, or unquoted up to the first space
        private static string TargetOf(string runValue)
        {
            string value = runValue.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = value.IndexOf('"', 1);
                return end > 0 ? value.Substring(1, end - 1) : value.Substring(1);
            }
            int space = value.IndexOf(' ');
            return space > 0 ? value.Substring(0, space) : value;
        }

        // null when the file is missing or tells no version
        private static Version VersionOf(string exe)
        {
            try
            {
                if (!File.Exists(exe)) return null;
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string RunValue()
        {
            return "\"" + Application.ExecutablePath + "\" " + Flag;
        }
    }
}
