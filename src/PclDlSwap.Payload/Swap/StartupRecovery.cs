using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Persistent startup sentinel for the module platform. A normal start marks the journal
    /// unstable first and flips it to stable only after the host has survived a short window. Two
    /// consecutive unstable starts therefore mean a boot loop or an early crash; the next start
    /// enters safe mode and skips every addon for that process.
    /// </summary>
    internal sealed class StartupRecovery
    {
        private const int SchemaVersion = 1;
        private const int DefaultStableDelayMs = 12000;
        private const int MinStableDelayMs = 1000;
        private const int MaxStableDelayMs = 120000;

        private readonly object _gate = new object();
        private Journal _journal;

        private StartupRecovery(Journal journal, string journalPath)
        {
            _journal = journal;
            JournalPath = journalPath;
        }

        public string JournalPath { get; }
        public bool SafeMode => _journal?.SafeMode == true;
        public bool SafeModeExitPending { get; private set; }
        public string SafeModeReason => _journal?.SafeModeReason ?? string.Empty;
        public int UncleanStarts => _journal?.UncleanStarts ?? 0;
        public int SafeModeUncleanStarts => _journal?.SafeModeUncleanStarts ?? 0;
        public int Attempt => UncleanStarts + 1;
        public IReadOnlyList<string> PreviousModules =>
            _journal?.Modules ?? (IReadOnlyList<string>)Array.Empty<string>();

        public static StartupRecovery Begin(SwapShape shape, string hostVersion,
            IEnumerable<string> moduleIds)
        {
            var path = Path.Combine(SwapPaths.RecoveryDirectory, "startup-journal.json");
            var previous = Read(path);
            var unclean = previous != null && !previous.Stable
                ? Math.Max(0, previous.UncleanStarts) + 1
                : 0;
            var forced = string.Equals(
                Environment.GetEnvironmentVariable("PCL2SWAP_SAFE_MODE"), "1",
                StringComparison.OrdinalIgnoreCase);
            var safeMode = forced || previous?.SafeMode == true || unclean >= 2;
            var previousSafeCount = previous == null
                ? 0
                : Math.Max(previous.SafeModeUncleanStarts, previous.UncleanStarts);
            var displayedUnclean = previous?.SafeMode == true
                ? previousSafeCount
                : unclean;
            var reason = !safeMode
                ? null
                : forced
                    ? "PCL2SWAP_SAFE_MODE=1"
                    : !string.IsNullOrWhiteSpace(previous?.SafeModeReason)
                        ? previous.SafeModeReason
                        : previous?.SafeMode == true
                            ? "安全模式已在之前会话启用"
                            : $"连续 {unclean} 次启动未进入稳定窗口";

            var journal = new Journal
            {
                Schema = SchemaVersion,
                SessionId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow.ToString("o"),
                HostVersion = hostVersion ?? string.Empty,
                PlatformVersion = SwapLog.ToolVersion,
                Shape = shape.ToString(),
                Modules = (moduleIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Stable = false,
                UncleanStarts = unclean,
                SafeModeUncleanStarts = safeMode ? Math.Max(previousSafeCount, displayedUnclean) : 0,
                SafeMode = safeMode,
                SafeModeReason = reason
            };
            Write(path, journal);
            SwapLog.Info($"[recovery] journal 已写入：attempt={unclean + 1}, " +
                         $"safeMode={safeMode}, modules={journal.Modules.Count}, path={path}");
            return new StartupRecovery(journal, path);
        }

        /// <summary>Queues the stable heartbeat without blocking host startup.</summary>
        public void ScheduleStable()
        {
            var delay = StableDelay();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Thread.Sleep(delay); }
                catch { }
                MarkStableNow();
            });
        }

        /// <summary>Marks this exact session stable; safe mode remains active until explicitly cleared.</summary>
        public bool MarkStableNow()
        {
            lock (_gate)
            {
                if (_journal == null) return false;
                var current = Read(JournalPath);
                if (current != null &&
                    !string.Equals(current.SessionId, _journal.SessionId, StringComparison.Ordinal))
                    return false;

                _journal.Stable = true;
                _journal.UncleanStarts = 0;
                _journal.StableAtUtc = DateTime.UtcNow.ToString("o");
                Write(JournalPath, _journal);
                SwapLog.Info("[recovery] 本会话已进入稳定窗口。");
                return true;
            }
        }

        /// <summary>
        /// Clears the persistent safe-mode flag. The current process stays in safe mode; the next
        /// PCL launch loads addons again.
        /// </summary>
        public bool ClearSafeMode()
        {
            lock (_gate)
            {
                if (_journal == null) return false;
                _journal.SafeMode = false;
                _journal.SafeModeReason = null;
                _journal.UncleanStarts = 0;
                _journal.SafeModeUncleanStarts = 0;
                _journal.Stable = true;
                _journal.ClearedAtUtc = DateTime.UtcNow.ToString("o");
                Write(JournalPath, _journal);
                SafeModeExitPending = true;
                SwapLog.Info("[recovery] 安全模式旗标已清除；重启 PCL 后加载模块。");
                return true;
            }
        }

        public string Diagnostics()
        {
            var lines = new List<string>
            {
                "journal: " + JournalPath,
                "session: " + (_journal?.SessionId ?? "(none)"),
                "attempt: " + Attempt,
                "unclean starts: " + UncleanStarts,
                "safe-mode unclean starts: " + SafeModeUncleanStarts,
                "safe mode: " + SafeMode,
                "safe mode reason: " + (string.IsNullOrEmpty(SafeModeReason) ? "(none)" : SafeModeReason),
                "host: " + (_journal?.HostVersion ?? "(unknown)"),
                "platform: " + (_journal?.PlatformVersion ?? "(unknown)"),
                "shape: " + (_journal?.Shape ?? "(unknown)"),
                "modules: " + string.Join(", ", PreviousModules)
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static int StableDelay()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("PCL2SWAP_RECOVERY_STABLE_MS"),
                    out var parsed))
                return Math.Min(MaxStableDelayMs, Math.Max(MinStableDelayMs, parsed));
            return DefaultStableDelayMs;
        }

        private static Journal Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var journal = new JavaScriptSerializer().Deserialize<Journal>(File.ReadAllText(path));
                if (journal == null || journal.Schema != SchemaVersion) return null;
                return journal;
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[recovery] journal 读取失败，按首次启动处理：" + ex.Message);
                return null;
            }
        }

        private static void Write(string path, Journal journal)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? SwapPaths.RecoveryDirectory);
                var json = new JavaScriptSerializer().Serialize(journal);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                try
                {
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                catch
                {
                    File.Copy(temp, path, true);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[recovery] journal 写入失败：" + ex.Message);
            }
        }

        private sealed class Journal
        {
            public int Schema { get; set; }
            public string SessionId { get; set; }
            public string StartedAtUtc { get; set; }
            public string StableAtUtc { get; set; }
            public string ClearedAtUtc { get; set; }
            public string HostVersion { get; set; }
            public string PlatformVersion { get; set; }
            public string Shape { get; set; }
            public List<string> Modules { get; set; }
            public bool Stable { get; set; }
            public int UncleanStarts { get; set; }
            public int SafeModeUncleanStarts { get; set; }
            public bool SafeMode { get; set; }
            public string SafeModeReason { get; set; }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
