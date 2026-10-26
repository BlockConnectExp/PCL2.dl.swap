using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PclDlSwap.Payload.Host
{
    internal sealed class HostTarget
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string BuildType { get; set; }
        public string File { get; set; }
        public string Sha256 { get; set; }
        public string Mvid { get; set; }
        public string Role { get; set; }
        public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> Rva { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Loads data/offset-manifest.json and matches the running host by MVID first, then by file
    /// SHA-256. Matching by MVID is what allows "adapt only the offsets" for later builds:
    /// new MVID + new RVAs, same method identities.
    ///
    /// In the loader shape the manifest sits next to the payload on disk; in the ExLoader shape the
    /// payload ships alone, so the same manifest travels as an embedded resource. When neither is
    /// available the installer degrades to resolving methods by name.
    /// </summary>
    internal static class OffsetManifest
    {
        private static readonly object SyncRoot = new object();
        private static List<HostTarget> _targets;
        private static HostTarget _match;

        public static HostTarget Match
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_targets == null) _targets = Load();
                    if (_match == null) _match = Resolve();
                    return _match;
                }
            }
        }

        /// <summary>
        /// Matches an explicit host assembly. The injected shape must use the assembly that actually
        /// carries PCL (the injector's own launcher wrapper can be the EntryAssembly), otherwise the
        /// lookup would miss builds that are in the manifest.
        /// </summary>
        public static HostTarget ResolveFor(Assembly host)
        {
            lock (SyncRoot)
            {
                if (_targets == null) _targets = Load();
                if (host == null) return null;

                string mvid;
                try
                {
                    mvid = host.ManifestModule.ModuleVersionId.ToString("D").ToLowerInvariant();
                }
                catch
                {
                    return null;
                }

                var byMvid = _targets.FirstOrDefault(t =>
                    string.Equals(t.Mvid, mvid, StringComparison.OrdinalIgnoreCase));
                if (byMvid != null) return byMvid;

                string sha;
                try
                {
                    var location = host.Location;
                    if (string.IsNullOrEmpty(location) || !File.Exists(location)) return null;
                    using (var sha256 = SHA256.Create())
                    using (var stream = File.OpenRead(location))
                        sha = BitConverter.ToString(sha256.ComputeHash(stream))
                            .Replace("-", string.Empty).ToLowerInvariant();
                }
                catch
                {
                    return null;
                }

                return _targets.FirstOrDefault(t =>
                    string.Equals(t.Sha256, sha, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static IReadOnlyList<HostTarget> All
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_targets == null) _targets = Load();
                    return _targets;
                }
            }
        }

        private static string ManifestPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "offset-manifest.json");

        private static List<HostTarget> Load()
        {
            var list = new List<HostTarget>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in ReadManifestCandidates())
            {
                try
                {
                    var parsed = ParseTargets(candidate.Value);
                    int added = 0, duplicates = 0;
                    foreach (var target in parsed)
                    {
                        if (string.IsNullOrEmpty(target.Id) || !seen.Add(target.Id))
                        {
                            duplicates++;
                            continue;
                        }
                        list.Add(target);
                        added++;
                    }
                    SwapLog.Info($"偏移清单[{candidate.Key}]：{parsed.Count} 个目标，并入 {added} 个，重复/无效 {duplicates} 个。");
                }
                catch (Exception ex)
                {
                    SwapLog.Warn($"偏移清单[{candidate.Key}]解析失败（已忽略）：" + ex.Message);
                }
            }

            if (list.Count == 0)
                SwapLog.Warn("偏移清单不可用或为空（磁盘与内嵌副本都没有可解析的目标），钩子将按名称解析。");
            return list;
        }

        /// <summary>
        /// Returns every manifest copy we can see, disk first. The injected shape ships the manifest
        /// as an embedded resource, but the disk file used to shadow it completely (single-source,
        /// disk-first), so a stale "data\offset-manifest.json" next to the host made every retained
        /// build report "宿主不在偏移清单中". Now both copies are parsed and merged by target id,
        /// with the disk copy winning on conflicts.
        /// </summary>
        private static List<KeyValuePair<string, string>> ReadManifestCandidates()
        {
            var candidates = new List<KeyValuePair<string, string>>();

            try
            {
                if (File.Exists(ManifestPath))
                    candidates.Add(new KeyValuePair<string, string>(
                        "disk:" + ManifestPath, File.ReadAllText(ManifestPath, Encoding.UTF8)));
            }
            catch (Exception ex)
            {
                SwapLog.Warn("磁盘偏移清单读取失败：" + ex.Message);
            }

            try
            {
                var assembly = typeof(OffsetManifest).Assembly;
                var name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("offset-manifest.json", StringComparison.OrdinalIgnoreCase));
                if (name != null)
                {
                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream != null)
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                                candidates.Add(new KeyValuePair<string, string>("embedded:" + name, reader.ReadToEnd()));
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("内嵌偏移清单读取失败：" + ex.Message);
            }

            return candidates;
        }

        private static List<HostTarget> ParseTargets(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            var list = new List<HostTarget>();
            if (root == null || !root.TryGetValue("targets", out var targetsObj))
            {
                SwapLog.Warn("偏移清单缺少 targets 节点。");
                return list;
            }

            // JavaScriptSerializer hands JSON arrays back as object[] on the runtimes we have seen;
            // accept any IEnumerable so an ArrayList (or a future shape) cannot silently empty the list.
            if (!(targetsObj is IEnumerable targets))
            {
                SwapLog.Warn("偏移清单的 targets 不是数组（" + targetsObj.GetType().Name + "）。");
                return list;
            }

            foreach (var item in targets)
            {
                if (!(item is Dictionary<string, object> map)) continue;
                var target = new HostTarget
                {
                    Id = Str(map, "id"),
                    Version = Str(map, "version"),
                    BuildType = Str(map, "buildType"),
                    File = Str(map, "file"),
                    Sha256 = Str(map, "sha256"),
                    Mvid = Str(map, "mvid"),
                    Role = Str(map, "role")
                };
                if (map.TryGetValue("tokens", out var tk) && tk is Dictionary<string, object> tokenMap)
                    foreach (var kv in tokenMap) target.Tokens[kv.Key] = Convert.ToString(kv.Value);
                if (map.TryGetValue("rva", out var rv) && rv is Dictionary<string, object> rvaMap)
                    foreach (var kv in rvaMap) target.Rva[kv.Key] = Convert.ToString(kv.Value);
                list.Add(target);
            }
            return list;
        }

        private static string Str(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

        private static HostTarget Resolve()
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry == null) return null;
            var mvid = entry.ManifestModule.ModuleVersionId.ToString("D").ToLowerInvariant();
            var byMvid = _targets.FirstOrDefault(t => string.Equals(t.Mvid, mvid, StringComparison.OrdinalIgnoreCase));
            if (byMvid != null) return byMvid;

            string sha;
            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(entry.Location))
                    sha = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch
            {
                return null;
            }
            return _targets.FirstOrDefault(t => string.Equals(t.Sha256, sha, StringComparison.OrdinalIgnoreCase));
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
