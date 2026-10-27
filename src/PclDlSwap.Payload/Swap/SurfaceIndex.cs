using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Loads the embedded PclSurface dumps (data/pcl-surface-*.json) and serves them as the
    /// ISwapSurface index for the running host. The first query parses the embedded manifests,
    /// prefers the one whose MVID matches the host, and otherwise falls back to the newest
    /// version - so a future build still gets a useful (if approximate) index.
    /// </summary>
    internal sealed class SurfaceIndex : ISwapSurface
    {
        private readonly object _gate = new object();
        private Dictionary<string, SurfaceTypeInfo> _byName;
        private List<SurfaceTypeInfo> _types;
        private string _mvid = string.Empty;

        public string HostMvid { get { EnsureLoaded(); return _mvid; } }
        public int TypeCount { get { EnsureLoaded(); return _types.Count; } }
        public IReadOnlyList<SurfaceTypeInfo> Types { get { EnsureLoaded(); return _types; } }

        public SurfaceTypeInfo FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            EnsureLoaded();
            if (_byName.TryGetValue(fullName, out var direct)) return direct;
            var normalized = fullName.Replace('/', '+');
            return _byName.TryGetValue(normalized, out var alt) ? alt : null;
        }

        public SurfaceMethodInfo FindMethod(string typeFullName, string methodName, int parameterCount = -1)
        {
            var type = FindType(typeFullName);
            if (type?.Methods == null) return null;
            return type.Methods.FirstOrDefault(method =>
                method.Name == methodName &&
                (parameterCount < 0 || (method.Parameters?.Count ?? 0) == parameterCount));
        }

        public IReadOnlyList<SurfaceTypeInfo> Search(string text, int limit = 50)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(text)) return new List<SurfaceTypeInfo>();
            return _types.Where(type =>
                    (type.FullName?.IndexOf(text, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (type.Methods?.Any(method =>
                        (method.Name?.IndexOf(text, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0) ?? false))
                .Take(limit < 1 ? 50 : limit)
                .ToList();
        }

        // ------------------------------------------------------------------ loading

        private void EnsureLoaded()
        {
            lock (_gate)
            {
                if (_types != null) return;

                var hostMvid = string.Empty;
                try
                {
                    hostMvid = SwapHost.HostAssembly?.ManifestModule.ModuleVersionId
                        .ToString().ToLowerInvariant() ?? string.Empty;
                }
                catch
                {
                    // host may not be resolved yet; an empty MVID simply skips exact matching
                }

                var candidates = new List<Tuple<string, Version, List<SurfaceTypeInfo>>>();
                foreach (var name in typeof(SurfaceIndex).Assembly.GetManifestResourceNames()
                             .Where(n => n.IndexOf("pcl-surface", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    try
                    {
                        using (var stream = typeof(SurfaceIndex).Assembly.GetManifestResourceStream(name))
                        {
                            if (stream == null) continue;
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                var parsed = Parse(reader.ReadToEnd());
                                candidates.Add(parsed);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("Surface 清单解析失败：" + name + " | " + ex.Message);
                    }
                }

                Tuple<string, Version, List<SurfaceTypeInfo>> chosen = null;
                if (!string.IsNullOrEmpty(hostMvid))
                    chosen = candidates.FirstOrDefault(candidate =>
                        string.Equals(candidate.Item1, hostMvid, StringComparison.OrdinalIgnoreCase));
                if (chosen == null && candidates.Count > 0)
                    chosen = candidates.OrderByDescending(candidate => candidate.Item2 ?? new Version(0, 0)).First();

                _types = chosen?.Item3 ?? new List<SurfaceTypeInfo>();
                _mvid = chosen?.Item1 ?? string.Empty;
                _byName = new Dictionary<string, SurfaceTypeInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var type in _types)
                {
                    if (!string.IsNullOrEmpty(type.FullName)) _byName[type.FullName] = type;
                }

                SwapLog.Info($"Surface 索引就绪：清单 MVID {(_mvid.Length == 0 ? "(none)" : _mvid)}，" +
                             $"宿主 MVID {(hostMvid.Length == 0 ? "(unknown)" : hostMvid)}，" +
                             $"{_types.Count} 个类型（候选 {candidates.Count} 份）。");
            }
        }

        private static Tuple<string, Version, List<SurfaceTypeInfo>> Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            var types = new List<SurfaceTypeInfo>();
            var mvid = string.Empty;
            Version version = null;

            if (root == null) return Tuple.Create(mvid, version, types);

            if (root.TryGetValue("assembly", out var assemblyObj) &&
                assemblyObj is Dictionary<string, object> assembly)
            {
                mvid = (Str(assembly, "mvid") ?? string.Empty).ToLowerInvariant();
                Version.TryParse(Str(assembly, "version"), out version);
            }

            if (!root.TryGetValue("types", out var typesObj) || !(typesObj is IEnumerable enumerable))
                return Tuple.Create(mvid, version, types);

            foreach (var item in enumerable)
            {
                if (!(item is Dictionary<string, object> map)) continue;
                types.Add(new SurfaceTypeInfo
                {
                    FullName = Str(map, "fullName"),
                    Name = Str(map, "name"),
                    Namespace = Str(map, "namespace"),
                    Kind = Str(map, "kind"),
                    BaseType = Str(map, "base"),
                    Visibility = Str(map, "visibility"),
                    Methods = MapList(map, "methods", method => new SurfaceMethodInfo
                    {
                        Name = Str(method, "name"),
                        Static = Bool(method, "static"),
                        Virtual = Bool(method, "virtual"),
                        Abstract = Bool(method, "abstract"),
                        Visibility = Str(method, "visibility"),
                        ReturnType = Str(method, "return"),
                        Parameters = StringList(method, "params"),
                        Token = Str(method, "token")
                    }),
                    Properties = MapList(map, "properties", property => new SurfacePropertyInfo
                    {
                        Name = Str(property, "name"),
                        Type = Str(property, "type"),
                        Get = Bool(property, "get"),
                        Set = Bool(property, "set"),
                        Static = Bool(property, "static")
                    }),
                    Fields = MapList(map, "fields", field => new SurfaceFieldInfo
                    {
                        Name = Str(field, "name"),
                        Type = Str(field, "type"),
                        Static = Bool(field, "static"),
                        Visibility = Str(field, "visibility"),
                        Literal = Bool(field, "literal")
                    })
                });
            }

            return Tuple.Create(mvid, version, types);
        }

        private static string Str(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) && value != null ? Convert.ToString(value) : null;

        private static bool Bool(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) && value != null &&
            (value is bool flag ? flag : Convert.ToBoolean(value));

        private static IReadOnlyList<string> StringList(Dictionary<string, object> map, string key)
        {
            var list = new List<string>();
            if (!map.TryGetValue(key, out var value) || !(value is IEnumerable enumerable)) return list;
            foreach (var item in enumerable) list.Add(Convert.ToString(item));
            return list;
        }

        private static IReadOnlyList<T> MapList<T>(Dictionary<string, object> map, string key,
            Func<Dictionary<string, object>, T> selector)
        {
            var list = new List<T>();
            if (!map.TryGetValue(key, out var value) || !(value is IEnumerable enumerable)) return list;
            foreach (var item in enumerable)
            {
                if (item is Dictionary<string, object> entry) list.Add(selector(entry));
            }
            return list;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
