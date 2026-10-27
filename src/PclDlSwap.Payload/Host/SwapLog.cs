using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PclDlSwap.Payload.Host
{
    internal static class SwapLog
    {
        private static readonly object Sync = new object();
        private static volatile bool _mirrorDisabled;
        private static MethodInfo _hostLogger;
        private static bool _hostLoggerProbed;

        /// <summary>Displayed version of the tool, reused by the About page entry.</summary>
        public const string ToolVersion = "v26.0-Alpha 4";

        /// <summary>File-system friendly form of <see cref="ToolVersion"/>.</summary>
        public const string ToolVersionCompact = "v26.0-Alpha4";

        /// <summary>
        /// Primary log lives under %TEMP% so the injected form always has a writable, predictable
        /// location; a copy is mirrored next to the host when that directory is writable.
        /// </summary>
        public static string LogPath => Path.Combine(Path.GetTempPath(), "PCL2Swap", ToolVersionCompact, "pcl2swap.log");

        public static string MirrorLogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "pcl2swap.log");

        public static void Info(string message) => Write("info", message);
        public static void Warn(string message) => Write("warn", message);
        public static void Error(string message) => Write("error", message);
        public static void Error(string message, Exception exception) =>
            Write("error", exception == null ? message : message + " | " + exception);

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
                    if (!_mirrorDisabled && !string.IsNullOrEmpty(mirror))
                    {
                        Directory.CreateDirectory(mirror);
                        File.AppendAllText(MirrorLogPath, line + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch
                {
                    // the mirror is optional; the %TEMP% log is authoritative
                    _mirrorDisabled = true;
                }
                TryHostLogger(level, message);
            }
        }

        private static void TryHostLogger(string level, string message)
        {
            try
            {
                if (!_hostLoggerProbed)
                {
                    _hostLoggerProbed = true;
                    var host = Assembly.GetEntryAssembly();
                    var logger = host?.GetType("PCL.PclLogger", throwOnError: false);
                    _hostLogger = logger?.GetMethod("Info", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                }
                _hostLogger?.Invoke(null, new object[] { "[PCL2.dl.swap] " + message });
            }
            catch
            {
                // host logger shape varies per build; file log remains authoritative
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
