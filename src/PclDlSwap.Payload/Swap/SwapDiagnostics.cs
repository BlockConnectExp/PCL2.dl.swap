using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>Bounded in-memory mirror of host/module log events for API and UI diagnostics.</summary>
    internal static class SwapDiagnostics
    {
        private const int Capacity = 512;
        private static readonly ConcurrentQueue<SwapDiagnosticEvent> Events =
            new ConcurrentQueue<SwapDiagnosticEvent>();
        private static readonly Regex ModulePrefix = new Regex(
            @"^\[addon\]\s+\[(?<id>[^\]]+)\]\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static event EventHandler Changed;

        internal static void Record(string level, string message)
        {
            var module = string.Empty;
            var text = message ?? string.Empty;
            var match = ModulePrefix.Match(text);
            if (match.Success)
            {
                module = match.Groups["id"].Value;
                text = text.Substring(match.Length);
            }

            var item = new SwapDiagnosticEvent
            {
                TimestampUtc = DateTime.UtcNow,
                Level = level ?? "info",
                Severity = SeverityOf(level),
                Message = text,
                ModuleId = module
            };
            Events.Enqueue(item);
            while (Events.Count > Capacity) Events.TryDequeue(out _);

            var handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(null, EventArgs.Empty); }
                catch { }
            }
        }

        internal static IReadOnlyList<SwapDiagnosticEvent> Recent(int limit)
        {
            limit = Math.Max(1, Math.Min(Capacity, limit));
            return Events.Reverse().Take(limit).ToList();
        }

        internal static string Export(int limit)
        {
            var text = new StringBuilder();
            foreach (var item in Recent(limit).Reverse())
            {
                text.Append(item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(item.Level).Append("] ");
                if (!string.IsNullOrEmpty(item.ModuleId))
                    text.Append("[").Append(item.ModuleId).Append("] ");
                text.AppendLine(item.Message);
            }
            return text.ToString();
        }

        private static SwapHintType SeverityOf(string level)
        {
            switch ((level ?? string.Empty).ToLowerInvariant())
            {
                case "error": return SwapHintType.Error;
                case "warn":
                case "warning": return SwapHintType.Warning;
                case "success": return SwapHintType.Success;
                default: return SwapHintType.Info;
            }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
