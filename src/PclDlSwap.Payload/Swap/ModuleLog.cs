using System;
using System.IO;
using System.Text;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Per-module log file - the module platform's version of LSPosed's per-module log tab. Every
    /// line a module writes through <c>Api.Log / LogWarning / LogError</c>, plus every hook the
    /// platform attaches or refuses on its behalf, lands in
    /// <c>&lt;data root&gt;\logs\modules\&lt;id&gt;.log</c> next to the global swap log. The file
    /// rotates once at 256 KB so a chatty module cannot fill the disk.
    /// </summary>
    internal static class ModuleLog
    {
        private const long MaxBytes = 256 * 1024;
        private static readonly object Gate = new object();

        public static string LogDirectory => Path.Combine(SwapPaths.DataRoot, "logs", "modules");

        public static string PathFor(string addonId) =>
            Path.Combine(LogDirectory,
                SwapPaths.Sanitize(string.IsNullOrEmpty(addonId) ? "unnamed" : addonId) + ".log");

        public static void Write(string addonId, string level, string message, Exception error = null)
        {
            if (string.IsNullOrEmpty(addonId)) return;
            try
            {
                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(LogDirectory);
                    var path = PathFor(addonId);
                    Rotate(path);

                    var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                               " [" + level + "] " + message;
                    if (error != null)
                        line += Environment.NewLine + "    " + error.GetType().FullName + ": " + error.Message;
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // A log write must never take a module (or the launcher) down: the global swap log
                // still carries the same line.
            }
        }

        private static void Rotate(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < MaxBytes) return;

                var backup = path + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }
            catch
            {
                // best effort - worst case the file grows past the soft cap
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
