using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PclDlSwap.Payload.Sandbox
{
    public sealed class SwapConfig
    {
        /// <summary>Directory used for staged (never-executed) downloads.</summary>
        public string SandboxDirectory { get; set; } = @"%TEMP%\PclDlSwap\sandbox";

        /// <summary>Query remote reputation APIs in addition to local engines.</summary>
        public bool UseRemoteScanners { get; set; } = true;

        /// <summary>Keep staged files after a verdict, for audit.</summary>
        public bool KeepSandboxFiles { get; set; } = true;

        /// <summary>Allow committing when some engine could not answer (degraded mode).</summary>
        public bool AllowDegradedCommit { get; set; }

        /// <summary>Replace PCL cloud downloads with the sandboxed pipeline.</summary>
        public bool InterceptPclCloudDownloads { get; set; } = true;

        /// <summary>Turn silent OTA into an explicit, cancellable prompt.</summary>
        public bool BlockSilentOta { get; set; } = true;

        /// <summary>Deny the host's HasUpdatePermission gate outright.</summary>
        public bool DenyUpdatePermission { get; set; } = true;

        public string VirusTotalApiKey { get; set; } = "<none>";
        public string MetaDefenderApiKey { get; set; } = "<none>";

        public string ResolvedSandboxDirectory =>
            Environment.ExpandEnvironmentVariables(SandboxDirectory ?? string.Empty);

        public static SwapConfig Load()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "swap.config.json");
            if (!File.Exists(path)) return new SwapConfig();
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var map = serializer.Deserialize<Dictionary<string, object>>(json);
                var config = new SwapConfig();
                foreach (var property in typeof(SwapConfig).GetProperties())
                {
                    if (!map.TryGetValue(property.Name, out var value) || value == null) continue;
                    if (property.PropertyType == typeof(bool)) property.SetValue(config, Convert.ToBoolean(value));
                    else if (property.PropertyType == typeof(string)) property.SetValue(config, Convert.ToString(value));
                }
                return config;
            }
            catch
            {
                return new SwapConfig();
            }
        }
    }
}



<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->