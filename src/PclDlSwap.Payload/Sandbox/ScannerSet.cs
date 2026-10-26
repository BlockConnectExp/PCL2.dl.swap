using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace PclDlSwap.Payload.Sandbox
{
    public sealed class ScanVerdict
    {
        public bool Clean { get; set; }
        public string Engine { get; set; }
        public string Detail { get; set; }
        public string Status { get; set; }

        /// <summary>
        /// False when the engine is simply not installed/configured on this machine. Such an engine
        /// is reported for transparency but does not block a commit by itself; an engine that was
        /// expected to answer and failed keeps this true and blocks.
        /// </summary>
        public bool Participating { get; set; } = true;

        public override string ToString() =>
            $"[{Status}{(Participating ? string.Empty : " / not installed")}] {Engine}: {Detail}";
    }

    /// <summary>
    /// Multi-engine scanning front: built-in local engines (Microsoft Defender via MpCmdRun,
    /// ClamAV if installed) plus optional remote reputation APIs (VirusTotal / MetaDefender Cloud)
    /// configured in config/swap.config.json.
    ///
    /// Policy: every configured engine must answer, and every answer must be clean. A failure to
    /// answer is treated as "unverified" and blocks the commit unless the owner explicitly allows
    /// degraded mode in the config.
    /// </summary>
    public sealed class ScannerSet
    {
        private readonly SwapConfig _config;
        private static readonly object PathCacheLock = new object();
        private static readonly Dictionary<string, string> PathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ScannerSet(SwapConfig config) => _config = config;

        /// <summary>
        /// Extensions whose files can execute or carry executable code. PCL fetches version indexes,
        /// icons, translations and fonts constantly while browsing - scanning each of those used to
        /// spawn a MpCmdRun process and produced the field report's "严重的性能损失". Everything not
        /// listed here passes straight through (the machine's real-time protection still sees it).
        /// </summary>
        private static readonly HashSet<string> ScannableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".msi", ".msp", ".com", ".bat", ".cmd", ".cpl", ".msc",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".scr",
            ".sys", ".ocx", ".drv",
            ".jar", ".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".cab", ".iso",
            ".bin", ".dat"
        };

        public static bool ShouldScan(string path)
        {
            var extension = Path.GetExtension(path);
            return !string.IsNullOrEmpty(extension) && ScannableExtensions.Contains(extension);
        }

        public IReadOnlyList<ScanVerdict> Scan(string path)
        {
            var verdicts = new List<ScanVerdict>
            {
                RunDefender(path),
                RunClamAv(path)
            };
            if (!string.IsNullOrEmpty(_config.VirusTotalApiKey) && _config.VirusTotalApiKey != "<none>")
                verdicts.Add(RunVirusTotal(path));
            if (!string.IsNullOrEmpty(_config.MetaDefenderApiKey) && _config.MetaDefenderApiKey != "<none>")
                verdicts.Add(RunMetaDefender(path));
            return verdicts;
        }

        public bool AllClean(System.Collections.Generic.IReadOnlyList<ScanVerdict> verdicts, out string summary)
        {
            var sb = new StringBuilder();
            bool clean = true;
            bool anyParticipating = false;
            foreach (var verdict in verdicts)
            {
                sb.AppendLine(verdict.ToString());
                if (verdict.Participating) anyParticipating = true;
                if (!verdict.Clean) clean = false;
            }
            if (!anyParticipating)
            {
                sb.AppendLine("[none] 没有任何可用的查毒引擎，按未验证处理。");
                clean = false;
            }
            summary = sb.ToString().TrimEnd();
            return clean;
        }

        private ScanVerdict RunDefender(string path)
        {
            var mp = CachedMpCmdRun();
            if (mp == null)
                return new ScanVerdict { Clean = false, Engine = "Microsoft Defender", Status = "unavailable", Detail = "未找到 MpCmdRun.exe", Participating = false };

            var result = RunProcess(mp, $"-Scan -ScanType 3 -File \"{path}\" -DisableRemediation", 180000);
            switch (result.ExitCode)
            {
                case 0:
                    return new ScanVerdict { Clean = true, Engine = "Microsoft Defender", Status = "clean", Detail = "无威胁" };
                case 2:
                    return new ScanVerdict { Clean = false, Engine = "Microsoft Defender", Status = "threat", Detail = Trim(result.Output) };
                default:
                    return new ScanVerdict { Clean = false, Engine = "Microsoft Defender", Status = "error(" + result.ExitCode + ")", Detail = Trim(result.Output) };
            }
        }

        private static string CachedMpCmdRun()
        {
            lock (PathCacheLock)
            {
                if (PathCache.TryGetValue("mpcmdrun", out var cached)) return cached;
                var found = FindMpCmdRun(
                    @"C:\Program Files\Windows Defender\MpCmdRun.exe",
                    @"C:\ProgramData\Microsoft\Windows Defender\Platform");
                PathCache["mpcmdrun"] = found;
                return found;
            }
        }

        private static string FindMpCmdRun(string direct, string platformRoot)
        {
            if (File.Exists(direct)) return direct;
            if (!Directory.Exists(platformRoot)) return null;
            string best = null;
            foreach (var dir in Directory.GetDirectories(platformRoot))
            {
                var candidate = Path.Combine(dir, "MpCmdRun.exe");
                if (!File.Exists(candidate)) continue;
                if (best == null || string.CompareOrdinal(dir, Path.GetDirectoryName(best)) > 0) best = candidate;
            }
            return best;
        }

        private ScanVerdict RunClamAv(string path)
        {
            var clam = CachedOnPath("clamscan.exe");
            if (clam == null)
                return new ScanVerdict { Clean = true, Engine = "ClamAV", Status = "not-installed", Detail = "未安装 clamscan，本机不参与判定", Participating = false };
            var result = RunProcess(clam, $"--no-summary --infected \"{path}\"", 300000);
            return result.ExitCode == 0
                ? new ScanVerdict { Clean = true, Engine = "ClamAV", Status = "clean", Detail = "无威胁" }
                : new ScanVerdict { Clean = false, Engine = "ClamAV", Status = "threat/error(" + result.ExitCode + ")", Detail = Trim(result.Output) };
        }

        private static string CachedOnPath(string exe)
        {
            lock (PathCacheLock)
            {
                var key = "path:" + exe;
                if (PathCache.TryGetValue(key, out var cached)) return cached;
                var found = FindOnPath(exe);
                PathCache[key] = found;
                return found;
            }
        }

        private ScanVerdict RunVirusTotal(string path)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("x-apikey", _config.VirusTotalApiKey);
                    var bytes = File.ReadAllBytes(path);
                    var sha = Sha256Hex(bytes);
                    var reportUrl = "https://www.virustotal.com/api/v3/files/" + sha;
                    try
                    {
                        var json = client.DownloadString(reportUrl);
                        var malicious = ExtractNumber(json, "\"malicious\":");
                        var suspicious = ExtractNumber(json, "\"suspicious\":");
                        bool clean = malicious == 0 && suspicious == 0;
                        return new ScanVerdict
                        {
                            Clean = clean,
                            Engine = "VirusTotal",
                            Status = clean ? "clean" : "flagged",
                            Detail = $"malicious={malicious} suspicious={suspicious}"
                        };
                    }
                    catch (WebException ex)
                    {
                        var response = ex.Response as HttpWebResponse;
                        if (response != null && (int)response.StatusCode == 404)
                            return new ScanVerdict { Clean = false, Engine = "VirusTotal", Status = "not-found", Detail = "样本尚未入库：" + sha };
                        return new ScanVerdict { Clean = false, Engine = "VirusTotal", Status = "error", Detail = ex.Message };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ScanVerdict { Clean = false, Engine = "VirusTotal", Status = "error", Detail = ex.Message };
            }
        }

        private ScanVerdict RunMetaDefender(string path)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("apikey", _config.MetaDefenderApiKey);
                    var bytes = File.ReadAllBytes(path);
                    string boundary = "----PclDlSwap" + Guid.NewGuid().ToString("N");
                    client.Headers.Add("Content-Type", "multipart/form-data; boundary=" + boundary);
                    var body = new MemoryStream();
                    void Write(string s) => body.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
                    Write("--" + boundary + "\r\n");
                    Write($"Content-Disposition: form-data; name=\"file\"; filename=\"{Path.GetFileName(path)}\"\r\n");
                    Write("Content-Type: application/octet-stream\r\n\r\n");
                    body.Write(bytes, 0, bytes.Length);
                    Write("\r\n--" + boundary + "--\r\n");
                    var response = client.UploadData("https://api.metadefender.com/v4/file", "POST", body.ToArray());
                    var json = Encoding.UTF8.GetString(response);
                    var detected = ExtractNumber(json, "\"detected_by\":");
                    bool clean = detected == 0;
                    return new ScanVerdict
                    {
                        Clean = clean,
                        Engine = "MetaDefender",
                        Status = clean ? "clean" : "flagged",
                        Detail = "detected_by=" + detected
                    };
                }
            }
            catch (Exception ex)
            {
                return new ScanVerdict { Clean = false, Engine = "MetaDefender", Status = "error", Detail = ex.Message };
            }
        }

        private static int ExtractNumber(string json, string marker)
        {
            int index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return -1;
            index += marker.Length;
            int end = index;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            return end > index && int.TryParse(json.Substring(index, end - index), out var value) ? value : -1;
        }

        private static string FindOnPath(string exe)
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';');
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // ignore malformed PATH entries
                }
            }
            return null;
        }

        private static (int ExitCode, string Output) RunProcess(string exe, string arguments, int timeoutMs)
        {
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) return (-1, "启动失败");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return (-1, "超时");
                }
                return (process.ExitCode, stdout + stderr);
            }
        }

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Trim();
            return text.Length <= 400 ? text : text.Substring(0, 400) + "…";
        }

        internal static string Sha256Hex(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}



// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
