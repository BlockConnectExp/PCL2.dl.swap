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
    ///                [--permissions "host.hooks.attach"]
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
                    case "--assets": assets = args[++i]; break;
                    case "--out": output = args[++i]; break;
                    default:
                        Console.Error.WriteLine("未知参数：" + args[i]);
                        return 2;
                }
            }

            if (project == null || id == null || name == null || version == null || entry == null)
            {
                Console.Error.WriteLine(
                    "用法：PclAddonPack --project <dir> --id <id> --name <name> --version <ver> --entry <type>\n" +
                    "                  [--api \">=1.0 <2.0\"] [--description \"...\"]\n" +
                    "                  [--shapes \"injected,patched\"] [--host-versions \">=2.99.0\"]\n" +
                    "                  [--permissions \"host.hooks.attach,host.settings.write\"]\n" +
                    "                  [--assets <dir>] [--out <file>]");
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

            var manifest = new StringBuilder();
            manifest.AppendLine("{");
            for (var index = 0; index < fields.Count; index++)
                manifest.AppendLine(fields[index] + (index == fields.Count - 1 ? string.Empty : ","));
            manifest.AppendLine("}");

            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                var entryStream = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(entryStream.Open(), new UTF8Encoding(false)))
                    writer.Write(manifest.ToString());

                foreach (var dll in dlls)
                    archive.CreateEntryFromFile(dll, Path.GetFileName(dll));

                if (assets != null && Directory.Exists(assets))
                {
                    foreach (var file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
                    {
                        var relative = "assets/" + file.Substring(Path.GetFullPath(assets).Length + 1)
                            .Replace('\\', '/');
                        archive.CreateEntryFromFile(file, relative);
                    }
                }
            }

            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(outputPath))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();

            Console.WriteLine("已打包：" + outputPath);
            Console.WriteLine("  条目：" + string.Join(", ", dlls.Select(Path.GetFileName).Concat(new[] { "manifest.json" })));
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
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
