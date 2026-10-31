using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;

namespace PclDlSwap.PclAddonPack
{
    /// <summary>
    /// Packs a built addon project into a <c>.swapaddon</c> archive:
    ///   PclAddonPack --project &lt;dir&gt; --id &lt;id&gt; --name &lt;name&gt; --version &lt;ver&gt;
    ///                --entry &lt;type&gt; [--api "&gt;=1.0 &lt;2.0"] [--description "..."]
    ///                [--shapes "injected,patched"] [--host-versions "&gt;=2.99.0"]
    ///                [--permissions "host.hooks.attach"] [--scope "id;Name;Description;true;NameEn;DescriptionEn"]
    ///                [--assets &lt;dir&gt;] [--out &lt;file&gt;]
    /// The entry type is validated with dnlib (no addon code is executed here), so a typo in
    /// --entry fails at packaging time instead of inside the launcher.
    /// </summary>
    internal static class Program
    {
        private const string AddonInterface = "PCL2Swap.Api.ISwapAddon";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string project = null, id = null, name = null, version = null, entry = null;
            string api = ">=1.0 <2.0", description = null, assets = null, output = null;
            string shapes = null, hostVersions = null, permissions = null;
            string signKey = null, genKey = null;
            var scopeSpecs = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--project": project = args[++i]; break;
                    case "--id": id = args[++i]; break;
                    case "--name": name = args[++i]; break;
                    case "--version": version = args[++i]; break;
                    case "--entry": entry = args[++i]; break;
                    case "--api": api = args[++i]; break;
                    case "--description": description = args[++i]; break;
                    case "--shapes": shapes = args[++i]; break;
                    case "--host-versions": hostVersions = args[++i]; break;
                    case "--permissions": permissions = args[++i]; break;
                    case "--scope": scopeSpecs.Add(args[++i]); break;
                    case "--sign": signKey = args[++i]; break;
                    case "--gen-key": genKey = args[++i]; break;
                    case "--assets": assets = args[++i]; break;
                    case "--out": output = args[++i]; break;
                    default:
                        Console.Error.WriteLine("未知参数：" + args[i]);
                        return 2;
                }
            }

            if (genKey != null)
            {
                using (var rsa = RSA.Create())
                {
                    rsa.KeySize = 2048;
                    var privatePath = Path.GetFullPath(genKey);
                    var directory = Path.GetDirectoryName(privatePath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(privatePath, rsa.ToXmlString(true), new UTF8Encoding(false));

                    var publicPath = privatePath + ".pub.xml";
                    File.WriteAllText(publicPath, rsa.ToXmlString(false), new UTF8Encoding(false));

                    Console.WriteLine("已生成密钥对（RSA 2048）：");
                    Console.WriteLine("  私钥（签名用，务必自己保管）：" + privatePath);
                    Console.WriteLine("  公钥（复制到 <数据根>\\trusted-keys\\ 才会被信任）：" + publicPath);
                    Console.WriteLine("  keyId：" + KeyIdFor(rsa));
                }
                return 0;
            }

            if (project == null || id == null || name == null || version == null || entry == null)
            {
                Console.Error.WriteLine(
                    "用法：PclAddonPack --project <dir> --id <id> --name <name> --version <ver> --entry <type>\n" +
                    "                  [--api \">=1.0 <2.0\"] [--description \"...\"]\n" +
                    "                  [--shapes \"injected,patched\"] [--host-versions \">=2.99.0\"]\n" +
                    "                  [--permissions \"host.hooks.attach,host.settings.write\"]\n" +
                    "                  [--scope \"id;Name;Description;true;NameEn;DescriptionEn\"]   （可重复）\n" +
                    "                  [--assets <dir>] [--out <file>] [--sign <private-key.xml>]\n" +
                    "       PclAddonPack --gen-key <private-key.xml>   （生成签名密钥对）");
                return 2;
            }
            if (!Directory.Exists(project))
            {
                Console.Error.WriteLine("项目目录不存在：" + project);
                return 2;
            }

            // The SDK is provided by the host process: shipping a second copy would load a distinct
            // ISwapAddon identity and break the entry discovery (the loader checks against its own).
            var dlls = Directory.GetFiles(project, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(file => !string.Equals(Path.GetFileName(file), "PCL2Swap.Api.dll",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count == 0)
            {
                Console.Error.WriteLine("项目目录下没有 dll：" + project);
                return 2;
            }

            if (!ValidateEntry(dlls, entry))
            {
                Console.Error.WriteLine($"在 {dlls.Count} 个 dll 里没有找到实现 {AddonInterface} 的 {entry}");
                return 3;
            }

            var outputPath = output ?? Path.Combine(project, id + ".swapaddon");
            outputPath = Path.GetFullPath(outputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);

            // Payload inventory: every file the package ships, under the name it gets inside the zip.
            var payload = dlls
                .Select(file => new KeyValuePair<string, string>(Path.GetFileName(file), file))
                .ToList();
            if (assets != null && Directory.Exists(assets))
            {
                var assetRoot = Path.GetFullPath(assets);
                payload.AddRange(Directory.GetFiles(assets, "*", SearchOption.AllDirectories)
                    .Select(file => new KeyValuePair<string, string>(
                        "assets/" + file.Substring(assetRoot.Length + 1).Replace('\\', '/'), file)));
            }

            var fields = new List<string>
            {
                "  \"schema\": 1",
                "  \"id\": " + Quote(id),
                "  \"name\": " + Quote(name),
                "  \"version\": " + Quote(version),
                "  \"apiVersion\": " + Quote(api),
                "  \"entry\": " + Quote(entry)
            };
            if (description != null) fields.Add("  \"description\": " + Quote(description));
            AddList(fields, "shapes", shapes);
            AddList(fields, "hostVersions", hostVersions);
            AddList(fields, "permissions", permissions);
            AddScopes(fields, scopeSpecs);

            // files: [{ path, sha256 }] - the integrity list; signing covers the manifest bytes, so
            // the payload hashes are covered too.
            var fileLines = payload
                .Select(item => "    {\"path\": " + Quote(item.Key) + ", \"sha256\": " +
                                Quote(HashFile(item.Value)) + "}")
                .ToList();
            fields.Add("  \"files\": [" + Environment.NewLine +
                       string.Join("," + Environment.NewLine, fileLines) + Environment.NewLine + "  ]");

            var manifestJson = new StringBuilder();
            manifestJson.AppendLine("{");
            for (var index = 0; index < fields.Count; index++)
                manifestJson.AppendLine(fields[index] + (index == fields.Count - 1 ? string.Empty : ","));
            manifestJson.AppendLine("}");
            var manifestBytes = new UTF8Encoding(false).GetBytes(manifestJson.ToString());

            byte[] signatureBytes = null;
            var keyId = string.Empty;
            if (signKey != null)
            {
                if (!File.Exists(signKey))
                {
                    Console.Error.WriteLine("找不到签名私钥：" + signKey);
                    return 2;
                }
                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(File.ReadAllText(signKey));
                    signatureBytes = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    keyId = KeyIdFor(rsa);
                }
            }

            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", manifestBytes);
                if (signatureBytes != null)
                {
                    var signatureJson = "{" + Environment.NewLine +
                                        "  \"algorithm\": \"RSA-SHA256\"," + Environment.NewLine +
                                        "  \"keyId\": " + Quote(keyId) + "," + Environment.NewLine +
                                        "  \"signature\": " + Quote(Convert.ToBase64String(signatureBytes)) +
                                        Environment.NewLine + "}";
                    WriteEntry(archive, "signature.json", new UTF8Encoding(false).GetBytes(signatureJson));
                }

                foreach (var item in payload) archive.CreateEntryFromFile(item.Value, item.Key);
            }

            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(outputPath))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();

            Console.WriteLine("已打包：" + outputPath);
            Console.WriteLine("  条目：" + string.Join(", ", payload.Select(item => item.Key)
                .Concat(new[] { "manifest.json" })
                .Concat(signatureBytes == null ? new string[0] : new[] { "signature.json" })));
            Console.WriteLine("  签名：" + (signatureBytes == null ? "未签名" : "已签名（密钥 " + keyId + "）"));
            Console.WriteLine("  大小：" + new FileInfo(outputPath).Length + " 字节");
            Console.WriteLine("  SHA256：" + hash);
            return 0;
        }

        private static bool ValidateEntry(IEnumerable<string> dlls, string entry)
        {
            foreach (var dll in dlls)
            {
                try
                {
                    using (var module = ModuleDefMD.Load(dll))
                    {
                        var type = module.Types
                            .SelectMany(Flatten)
                            .FirstOrDefault(candidate => candidate.FullName == entry);
                        if (type == null) continue;

                        var implements = type.Interfaces.Any(impl =>
                                             impl.Interface?.FullName == AddonInterface) ||
                                         type.GetBaseType()?.FullName == AddonInterface;
                        if (implements) return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("跳过无法读取的 dll " + Path.GetFileName(dll) + "：" + ex.Message);
                }
            }
            return false;
        }

        private static IEnumerable<TypeDef> Flatten(TypeDef type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes.SelectMany(Flatten))
                yield return nested;
        }

        private static string Quote(string value) =>
            "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
        {
            var entry = archive.CreateEntry(name);
            using (var stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty)
                    .ToLowerInvariant();
        }

        /// <summary>
        /// Stable key id: first 16 hex of SHA-256 over the public key XML. The host computes the same
        /// value from every key in its trusted-keys folder, which is how a signature is matched to a
        /// trusted key without shipping a separate id registry.
        /// </summary>
        private static string KeyIdFor(RSA key)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToXmlString(false)));
                var text = new StringBuilder(16);
                for (var index = 0; index < 8; index++) text.Append(hash[index].ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary>Adds a JSON string-array field from a comma-separated command-line value.</summary>
        private static void AddList(List<string> fields, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var items = value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Select(Quote);
            fields.Add("  \"" + key + "\": [" + string.Join(", ", items) + "]");
        }

        /// <summary>
        /// Adds rich feature-scope objects. Spec format:
        /// id;Name;Description;defaultEnabled;NameEnglish;DescriptionEnglish.
        /// Only the id is required; empty optional fields are omitted.
        /// </summary>
        private static void AddScopes(List<string> fields, IReadOnlyList<string> specs)
        {
            if (specs == null || specs.Count == 0) return;
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in specs)
            {
                var parts = (raw ?? string.Empty).Split(new[] { ';' }, 6);
                var id = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                if (id.Length == 0 || !seen.Add(id)) continue;
                var name = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : id;
                var description = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                var enabled = parts.Length <= 3 || !bool.TryParse(parts[3], out var parsed) || parsed;
                var nameEnglish = parts.Length > 4 ? parts[4].Trim() : string.Empty;
                var descriptionEnglish = parts.Length > 5 ? parts[5].Trim() : string.Empty;
                var line = "    {\"id\": " + Quote(id) + ", \"name\": " + Quote(name);
                if (nameEnglish.Length > 0)
                    line += ", \"nameEn\": " + Quote(nameEnglish);
                line += ", \"description\": " + Quote(description);
                if (descriptionEnglish.Length > 0)
                    line += ", \"descriptionEn\": " + Quote(descriptionEnglish);
                line += ", \"defaultEnabled\": " + (enabled ? "true" : "false") + "}";
                lines.Add(line);
            }
            if (lines.Count == 0) return;
            fields.Add("  \"scopes\": [" + Environment.NewLine +
                       string.Join("," + Environment.NewLine, lines) + Environment.NewLine + "  ]");
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
