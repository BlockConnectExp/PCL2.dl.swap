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
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
