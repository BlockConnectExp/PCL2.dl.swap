using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Parses a .swapaddon manifest.json into the SDK model. Kept deliberately tolerant: missing
    /// optional fields stay null and validation happens in AddonManager.
    /// </summary>
    internal static class AddonManifestReader
    {
        public static AddonManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var map = serializer.Deserialize<Dictionary<string, object>>(json);
            if (map == null) return null;

            var manifest = new AddonManifest
            {
                Schema = ToInt(Get(map, "schema"), 1),
                Id = Get(map, "id"),
                Name = Get(map, "name"),
                Version = Get(map, "version"),
                ApiVersion = Get(map, "apiVersion"),
                Entry = Get(map, "entry"),
                Author = Get(map, "author"),
                Description = Get(map, "description"),
                DefaultEnabled = ToBool(Get(map, "defaultEnabled"), false)
            };
            if (string.IsNullOrEmpty(manifest.ApiVersion)) manifest.ApiVersion = ">=1.0 <2.0";
            // Lists need the raw JSON value: Convert.ToString on an array yields "System.Object[]",
            // which is why shapes/permissions used to come back empty for every module.
            manifest.Shapes.AddRange(ToStrings(GetRaw(map, "shapes")));
            manifest.HostVersions.AddRange(ToStrings(GetRaw(map, "hostVersions")));
            if (manifest.HostVersions.Count == 0)
                manifest.HostVersions.AddRange(ToStrings(GetRaw(map, "hostVersion")));
            manifest.Permissions.AddRange(ToStrings(GetRaw(map, "permissions")));

            // scopes: ["download"] or
            // scopes: [{ "id":"download", "name":"下载恢复", "nameEn":"Download restore",
            //           "description":"…", "descriptionEn":"…", "defaultEnabled":true }]
            var rawScopes = GetRaw(map, "scopes");
            if (rawScopes is IEnumerable scopeList && !(rawScopes is string))
            {
                var seenScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in scopeList)
                {
                    AddonScopeDefinition scope = null;
                    if (item is Dictionary<string, object> entry)
                    {
                        var scopeId = Get(entry, "id");
                        if (string.IsNullOrWhiteSpace(scopeId)) continue;
                        scope = new AddonScopeDefinition
                        {
                            Id = scopeId.Trim(),
                            Name = Get(entry, "name"),
                            NameEnglish = Get(entry, "nameEn"),
                            Description = Get(entry, "description"),
                            DescriptionEnglish = Get(entry, "descriptionEn"),
                            DefaultEnabled = ToBool(Get(entry, "defaultEnabled"), true)
                        };
                    }
                    else
                    {
                        var scopeId = Convert.ToString(item);
                        if (string.IsNullOrWhiteSpace(scopeId)) continue;
                        scope = new AddonScopeDefinition
                        {
                            Id = scopeId.Trim(),
                            Name = scopeId.Trim(),
                            DefaultEnabled = true
                        };
                    }

                    if (!IsValidScopeId(scope.Id) || !seenScopes.Add(scope.Id)) continue;
                    if (string.IsNullOrWhiteSpace(scope.Name)) scope.Name = scope.Id;
                    if (string.IsNullOrWhiteSpace(scope.NameEnglish)) scope.NameEnglish = scope.Name;
                    manifest.Scopes.Add(scope);
                }
            }

            // files: [{ "path": "Addon.dll", "sha256": "…" }] - the payload integrity list.
            if (GetRaw(map, "files") is IEnumerable fileList)
            {
                foreach (var item in fileList)
                {
                    if (!(item is Dictionary<string, object> entry)) continue;
                    var path = Get(entry, "path");
                    var hash = Get(entry, "sha256");
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(hash)) continue;
                    manifest.Files.Add(new AddonFileEntry { Path = path, Sha256 = hash });
                }
            }
            return manifest;
        }

        private static string Get(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

        private static object GetRaw(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? value : null;

        private static int ToInt(string text, int fallback) =>
            int.TryParse(text, out var value) ? value : fallback;

        private static bool ToBool(string text, bool fallback) =>
            bool.TryParse(text, out var value) ? value : fallback;

        private static List<string> ToStrings(object value)
        {
            var list = new List<string>();
            // A single string is the common shorthand for a one-element list.
            if (value is string single)
            {
                if (!string.IsNullOrEmpty(single)) list.Add(single);
                return list;
            }
            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    var text = Convert.ToString(item);
                    if (!string.IsNullOrEmpty(text)) list.Add(text);
                }
            }
            return list;
        }

        private static bool IsValidScopeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96) return false;
            foreach (var ch in id)
                if (!(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ))
                    return false;
            return true;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
