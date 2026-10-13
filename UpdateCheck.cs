using System;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VpnSifreleri
{
    /// <summary>
    /// Asks GitHub for the number of the latest release. Nothing about the user or the saved entries is sent: a single
    /// GET whose User-Agent names the app and its version, which GitHub requires.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string ReleasesPage = "https://github.com/alperennkls/VpnSifreleri/releases/latest";
        private const string LatestRelease = "https://api.github.com/repos/alperennkls/VpnSifreleri/releases/latest";

        public const int FirstCheckMs = 10 * 1000;
        private const int RetryMs = 15 * 60 * 1000;
        private const int DailyMs = 24 * 60 * 60 * 1000;

        /// <summary>
        /// When to ask again: soon after a failed check (after sign-in the network often comes up late), otherwise in a
        /// day, for an app that stays in the tray for weeks.
        /// </summary>
        public static int NextCheckMs(Version latest)
        {
            return latest == null ? RetryMs : DailyMs;
        }

        /// <summary>This build's version in three parts, like the release tags.</summary>
        public static Version Current
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(version.Major, version.Minor, version.Build);
            }
        }

        /// <summary>The latest release's version, or null when GitHub cannot be reached or answers unexpectedly.</summary>
        public static Task<Version> FetchLatestAsync()
        {
            return Task.Run(() => FetchLatest(LatestRelease));
        }

        internal static Version FetchLatest(string url)
        {
            try
            {
                // GitHub speaks only TLS 1.2+, which apps built for .NET 4.0 do not offer unless asked
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient web = new WebClient())
                {
                    web.Encoding = Encoding.UTF8;
                    web.Headers[HttpRequestHeader.UserAgent] = "VpnSifreleri/" + Current;
                    web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    // Company proxies often want the signed-in Windows user
                    if (web.Proxy != null) web.Proxy.Credentials = CredentialCache.DefaultCredentials;
                    return ParseTag(web.DownloadString(url));
                }
            }
            catch (Exception)
            {
                return null; // offline, blocked, rate-limited, odd answer: the next check asks again
            }
        }

        /// <summary>Version in the release JSON's tag_name ("v1.2.0" or "1.2"), or null.</summary>
        internal static Version ParseTag(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+\\.\\d+(?:\\.\\d+)?)\"");
            Version version;
            if (!tag.Success || !Version.TryParse(tag.Groups[1].Value, out version)) return null;
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }

        internal static bool IsNewer(Version latest, Version current)
        {
            return latest != null && latest > current;
        }
    }
}
