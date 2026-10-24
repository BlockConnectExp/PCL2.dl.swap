using System.Collections.Generic;

namespace PclDlSwap.HookFixture
{
    /// <summary>
    /// Stand-in for PCL.ModSecret / PCL.ModNet: same shapes, no side effects.
    /// </summary>
    internal static class FixtureTarget
    {
        public static readonly List<string> UpdateCalls = new List<string>();
        public static readonly List<string> DownloadCalls = new List<string>();
        public static int PermissionQueries;

        private static bool _permission = true;

        public static bool HasUpdatePermission
        {
            get
            {
                PermissionQueries++;
                return _permission;
            }
        }

        private static void UpdateStart(bool silent, IEnumerable<string> urlsOverride)
        {
            UpdateCalls.Add($"silent={silent}");
        }

        public static void TriggerSilentUpdate() => UpdateStart(true, null);

        public static void TriggerExplicitUpdate() => UpdateStart(false, null);

        public static void DownloadByLoader(IEnumerable<string> urls, string localPath, object loader, object checker, bool simulateBrowserHeaders)
        {
            BodyHits++;
            DownloadCalls.Add("loader:" + localPath);
        }

        /// <summary>Counts executions of the real method body; the detour must prevent this from rising.</summary>
        public static int BodyHits;

        public static void DownloadByClient(string url, string localPath, bool simulateBrowserHeaders)
        {
            DownloadCalls.Add("client:" + localPath);
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover