using System;

namespace PclDlSwap.Payload.Sandbox
{
    /// <summary>
    /// Owner's scan policy (2026-10-26): game content - game files, mods, resource packs and
    /// shaders, i.e. everything that lands inside the Minecraft directory - skips the scanner
    /// entirely, while launcher files fetched from PCL's own servers keep it. Combined with the
    /// extension filter (see <see cref="ScannerSet.ShouldScan"/>) this leaves exactly one case to
    /// scan: a scannable file that lands outside the game directory.
    ///
    /// Both delivery shapes share this class so the policy cannot drift between them.
    /// </summary>
    public static class ScanPolicy
    {
        private static readonly string[] GameContentMarkers =
        {
            "\\.minecraft\\", "\\mods\\", "\\resourcepacks\\", "\\shaderpacks\\", "\\versions\\"
        };

        /// <summary>
        /// True when the download lands inside a Minecraft directory. The markers are matched as
        /// whole path segments ("\mods\" does not match "\mymods\"), so only actual game content is
        /// skipped.
        /// </summary>
        public static bool IsGameContentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var normalized = path.Replace('/', '\\');
            foreach (var marker in GameContentMarkers)
            {
                if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>True when a downloaded file should be offered to the scanners at all.</summary>
        public static bool ShouldScanDownload(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!ScannerSet.ShouldScan(path)) return false;
            return !IsGameContentPath(path);
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
