using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PclDlSwap.Payload.Host
{
    internal static class SwapLog
    {
        private static readonly object Sync = new object();

        public static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "pcl2swap.log");

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