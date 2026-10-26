using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PclDlSwap.Payload.Host
{
    internal static class SwapLog
    {
        private static readonly object Sync = new object();

        /// <summary>Displayed version of the tool, reused by the About page entry.</summary>
        public const string ToolVersion = "v26.0-Alpha 5";

        /// <summary>File-system friendly form of <see cref="ToolVersion"/>.</summary>
        public const string ToolVersionCompact = "v26.0-Alpha5";

        /// <summary>
        /// Primary log lives under %TEMP% so the injected form always has a writable, predictable
        /// location; a copy is mirrored next to the host when that directory is writable.
        /// </summary>
        public static string LogPath => Path.Combine(Path.GetTempPath(), "PCL2Swap", ToolVersionCompact, "pcl2swap.log");

        public static string MirrorLogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "pcl2swap.log");

        public static void Info(string message) => Write("info", message);
        public static void Warn(string message) => Write("warn", message);
        public static void Error(string message) => Write("error", message);

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (Sync)
            {
                try
                {
                    var directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // logging must never break the host
                }
                try
                {
                    var mirror = Path.GetDirectoryName(MirrorLogPath);
                    if (!string.IsNullOrEmpty(mirror))
                    {
                        Directory.CreateDirectory(mirror);
                        File.AppendAllText(MirrorLogPath, line + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch
                {
                    // the mirror is optional; the %TEMP% log is authoritative
                }
                TryHostLogger(level, message);
            }
        }

        private static void TryHostLogger(string level, string message)
        {
            try
            {
                var host = Assembly.GetEntryAssembly();
                var logger = host?.GetType("PCL.PclLogger", throwOnError: false);
                if (logger == null) return;
                var method = logger.GetMethod("Info", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                method?.Invoke(null, new object[] { "[PCL2.dl.swap] " + message });
            }
            catch
            {
                // host logger shape varies per build; file log remains authoritative
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
