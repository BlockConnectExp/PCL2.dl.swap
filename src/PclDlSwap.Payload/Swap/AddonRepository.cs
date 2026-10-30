using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// LSPosed-style module repository: a small JSON index lists .swapaddon packages. The platform
    /// downloads, verifies the advertised SHA-256 and then hands the file to the same installer
    /// the file picker uses (review/trust/permissions all apply).
    /// </summary>
    internal static class AddonRepository
    {
        public const string DefaultIndexUrl =
            "https://raw.githubusercontent.com/BlockConnectExp/PCL2.dl.swap/main/data/addons-index.json";

        private static readonly object Gate = new object();
        private static Snapshot _current = new Snapshot();

        public sealed class RepoModule
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public string Author { get; set; }
            public string Description { get; set; }
            public string Project { get; set; }
            public string Download { get; set; }
            public string Sha256 { get; set; }
            public long Size { get; set; }
        }

        public sealed class Snapshot
        {
            public List<RepoModule> Modules { get; set; } = new List<RepoModule>();
            public string Error { get; set; }
            public DateTime? LoadedAt { get; set; }
            public bool Loading { get; set; }
            public bool FromDisk { get; set; }
        }

        public static Snapshot Current
        {
            get { lock (Gate) return _current; }
        }

        /// <summary>Starts a background refresh; the callback runs on the caller's thread.</summary>
        public static void RefreshAsync(Action<Snapshot> done = null)
        {
            lock (Gate)
            {
                if (_current.Loading) return;
                _current = new Snapshot
                {
                    Modules = _current.Modules,
                    Loading = true,
                    FromDisk = _current.FromDisk
                };
            }

            var worker = new Thread(() =>
            {
                Snapshot next;
                try
                {
                    next = Load(DefaultIndexUrl);
                }
                catch (Exception ex)
                {
                    next = new Snapshot
                    {
                        Error = "仓库索引下载失败：" + ex.Message,
                        Modules = LoadBundled().Modules,
                        FromDisk = true
                    };
                }

                lock (Gate) _current = next;
                try { done?.Invoke(next); }
                catch (Exception ex) { SwapLog.Warn("[repo] 刷新回调失败：" + ex.Message); }
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.AddonRepo"
            };
            worker.Start();
        }

        /// <summary>Synchronous loader (also used by the self-test with a literal JSON body).</summary>
        public static Snapshot Load(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Pcl2Swap.Payload");
                    var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                    return Parse(json, url);
                }
            }
            catch (Exception ex)
            {
                var snapshot = LoadBundled();
                snapshot.Error = "仓库索引下载失败（已使用内置索引）：" + ex.Message;
                return snapshot;
            }
        }

        public static Snapshot Parse(string json, string source = null)
        {
            var snapshot = new Snapshot { LoadedAt = DateTime.UtcNow };
            try
            {
                var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                // JavaScriptSerializer hands nested arrays back as ArrayList (non-generic), so the
                // check must use the non-generic IEnumerable (the generic one always missed).
                if (parsed == null || !parsed.TryGetValue("modules", out var raw) ||
                    !(raw is IEnumerable list))
                    throw new InvalidOperationException("索引缺少 modules 数组");

                foreach (var item in list)
                {
                    if (!(item is IDictionary<string, object> map)) continue;
                    var module = new RepoModule
                    {
                        Id = Get(map, "id"),
                        Name = Get(map, "name"),
                        Version = Get(map, "version"),
                        Author = Get(map, "author"),
                        Description = Get(map, "description"),
                        Project = Get(map, "project"),
                        Download = Get(map, "download"),
                        Sha256 = Get(map, "sha256")
                    };
                    long.TryParse(Get(map, "size"), out var size);
                    module.Size = size;
                    if (!string.IsNullOrWhiteSpace(module.Id) &&
                        !string.IsNullOrWhiteSpace(module.Download))
                        snapshot.Modules.Add(module);
                }

                SwapLog.Info($"[repo] 已解析 {snapshot.Modules.Count} 个模块（{source ?? "内存"}）。");
            }
            catch (Exception ex)
            {
                snapshot.Error = "索引解析失败：" + ex.Message;
                SwapLog.Warn("[repo] 索引解析失败：" + ex);
            }

            return snapshot;
        }

        public static Snapshot LoadBundled()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("PclDlSwap.Payload.data.addons-index.json"))
                {
                    if (stream == null) return new Snapshot { Error = "内置索引缺失" };
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var snapshot = Parse(reader.ReadToEnd(), "内置索引");
                        snapshot.FromDisk = true;
                        return snapshot;
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[repo] 内置索引读取失败：" + ex);
                return new Snapshot { Error = "内置索引读取失败：" + ex.Message };
            }
        }

        /// <summary>Downloads one package and verifies the advertised hash.</summary>
        public static string Download(RepoModule module, out string error)
        {
            error = null;
            try
            {
                var root = Path.Combine(SwapPaths.Root, "repo");
                Directory.CreateDirectory(root);
                var file = Path.Combine(root,
                    SwapPaths.Sanitize(module.Id + "-" + (module.Version ?? "0")) + ".swapaddon");

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Pcl2Swap.Payload");
                    var bytes = http.GetByteArrayAsync(module.Download).GetAwaiter().GetResult();
                    File.WriteAllBytes(file, bytes);
                }

                if (!string.IsNullOrWhiteSpace(module.Sha256))
                {
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(file))
                    {
                        var actual = BitConverter.ToString(sha.ComputeHash(stream))
                            .Replace("-", string.Empty).ToLowerInvariant();
                        if (!string.Equals(actual, module.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                            error = $"SHA-256 不匹配（期望 {module.Sha256}，实际 {actual}）";
                            return null;
                        }
                    }
                }

                return file;
            }
            catch (Exception ex)
            {
                error = "下载失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>Downloads and installs one entry through the normal installer.</summary>
        public static AddonManager.AddonRecord Install(RepoModule module, out string error)
        {
            error = null;
            var path = Download(module, out error);
            if (path == null) return null;

            try
            {
                var record = SwapHost.Addons.Install(path);
                PageRegistry.RefreshSidebar();
                MorePageRegistry.RefreshSidebar();
                return record;
            }
            catch (Exception ex)
            {
                error = "安装失败：" + ex.Message;
                return null;
            }
        }

        private static string Get(IDictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
    }
}
