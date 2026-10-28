using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PclDlSwap.Patcher;

namespace PclDlSwap.Loader
{
    /// <summary>
    /// Prepares and launches PCL with the swap applied.
    ///
    /// The loader patches a *copy* of the official executable next to the original (the original is
    /// never touched), then starts that copy. The patched copy calls into PclDlSwap.Payload during
    /// startup, which installs the download gate restore, the silent-OTA prompt, the permission gate
    /// and the cloud-download sandbox watchdog. Starting the original executable directly still runs
    /// vanilla PCL.
    /// </summary>
    internal static class Program
    {
        private const string PatchedFileName = "PclDlSwap.Patched.exe";

        /// <summary>
        /// Files the patched executable needs at *its own* base directory (the CLR probes the app
        /// base for the AssemblyRef the runtime bootstrap introduces, not the loader's folder).
        /// </summary>
        private static readonly string[] RuntimeFiles =
        {
            "PclDlSwap.Payload.dll",
            "PCL2Swap.Api.dll",
            "MonoMod.Utils.dll",
            "Gee.External.Capstone.dll",
            "capstone.dll",
            "Mono.Cecil.dll"
        };

        private static int Main(string[] args)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var payloadPath = Path.Combine(baseDirectory, "PclDlSwap.Payload.dll");
            if (!File.Exists(payloadPath))
            {
                Console.Error.WriteLine("缺少 PclDlSwap.Payload.dll（应与本程序同目录）。");
                return 2;
            }

            var target = args.Length > 0
                ? args[0]
                : Path.Combine(baseDirectory, "Plain Craft Launcher 2.exe");
            if (!File.Exists(target))
            {
                Console.Error.WriteLine("找不到目标程序：" + target);
                Console.Error.WriteLine("用法：PclDlSwap.Loader.exe [目标 PCL 可执行文件] [-repatch]");
                return 2;
            }

            bool forceRepatch = Array.Exists(args, a => string.Equals(a, "-repatch", StringComparison.OrdinalIgnoreCase));
            bool watchUpdates = Array.Exists(args, a => string.Equals(a, "-watch", StringComparison.OrdinalIgnoreCase));
            var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? baseDirectory;
            var patchedPath = Path.Combine(targetDirectory, PatchedFileName);

            Console.WriteLine("PCL2 DLL-Swap loader (v26.1-Alpha 1)");
            Console.WriteLine("  目标    : " + target);
            Console.WriteLine("  原版哈希: " + ShortHash(target));

            var sourceHash = ShortHash(target, full: true);
            var stampPath = patchedPath + ".source.sha256";
            bool needsPatch = forceRepatch
                              || !File.Exists(patchedPath)
                              || !File.Exists(stampPath)
                              || !string.Equals(File.ReadAllText(stampPath).Trim(), sourceHash, StringComparison.OrdinalIgnoreCase);

            if (needsPatch)
            {
                try
                {
                    var patcher = new IlPatcher(payloadPath, message => Console.WriteLine("  " + message));
                    var result = patcher.Patch(target, patchedPath);
                    File.WriteAllText(stampPath, sourceHash, new UTF8Encoding(false));
                    Console.WriteLine("  补丁    : " + result.Rewrites + " 处重写 -> " + patchedPath);
                    Console.WriteLine("  输出哈希: " + result.Sha256.Substring(0, 16) + "…");
                    foreach (var note in result.Notes) Console.WriteLine("    · " + note);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("补丁失败：" + ex.Message);
                    return 3;
                }
            }
            else
            {
                Console.WriteLine("  补丁    : 已是最新（哈希一致），直接启动。");
            }

            Console.WriteLine("  运行时  : " + EnsureRuntimeFiles(baseDirectory, targetDirectory));

            var startInfo = new ProcessStartInfo(patchedPath)
            {
                UseShellExecute = false,
                WorkingDirectory = targetDirectory,
            };
            startInfo.EnvironmentVariables["PCL2SWAP_HOME"] = baseDirectory;
            startInfo.EnvironmentVariables["PCL2SWAP_ORIGINAL"] = target;

            try
            {
                var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.Error.WriteLine("启动失败。");
                    return 4;
                }
                Console.WriteLine("  已启动  : pid=" + process.Id);
                Console.WriteLine("  日志    : " + Path.Combine(targetDirectory, "logs") + "\\");

                if (watchUpdates)
                {
                    Console.WriteLine("  看门狗  : 已启用（拦截 PCL 暂存的更新包并查毒）");
                    using (var watchdog = new PclDlSwap.Payload.Host.UpdateLandingWatchdog(
                               PclDlSwap.Payload.Sandbox.SwapConfig.Load()))
                    {
                        watchdog.Start();
                        process.WaitForExit();
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("启动异常：" + ex.Message);
                return 4;
            }
        }

        /// <summary>
        /// Copies the runtime files next to the patched executable: the runtime bootstrap introduces
        /// an AssemblyRef to <c>PclDlSwap.Payload</c>, and the CLR resolves that against the patched
        /// executable's own base directory, not the loader's folder.
        /// </summary>
        private static string EnsureRuntimeFiles(string sourceDirectory, string targetDirectory)
        {
            if (string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
                return "已在同一目录（无需复制）";

            var copied = 0;
            var missing = new List<string>();
            foreach (var name in RuntimeFiles)
            {
                var source = Path.Combine(sourceDirectory, name);
                if (!File.Exists(source))
                {
                    missing.Add(name);
                    continue;
                }

                var destination = Path.Combine(targetDirectory, name);
                try
                {
                    // Compare contents, not timestamps: a same-sized copy from another build would
                    // otherwise be kept and the patched launcher would bind a mismatched runtime.
                    if (File.Exists(destination) && SameContent(source, destination)) continue;

                    File.Copy(source, destination, true);
                    copied++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("    复制 " + name + " 失败：" + ex.Message);
                }
            }

            var text = copied == 0 ? "已就位" : copied + " 个文件已复制到目标目录";
            return missing.Count == 0 ? text : text + "（缺少 " + string.Join(", ", missing) + "）";
        }

        /// <summary>Byte-level comparison used to decide whether a runtime file needs refreshing.</summary>
        private static bool SameContent(string left, string right)
        {
            try
            {
                if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
                using (var sha = SHA256.Create())
                using (var first = File.OpenRead(left))
                using (var second = File.OpenRead(right))
                {
                    var firstHash = sha.ComputeHash(first);
                    sha.Initialize();
                    var secondHash = sha.ComputeHash(second);
                    for (var i = 0; i < firstHash.Length; i++)
                    {
                        if (firstHash[i] != secondHash[i]) return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ShortHash(string path, bool full = false)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    var hash = sha.ComputeHash(stream);
                    var text = new StringBuilder();
                    foreach (var b in hash) text.Append(b.ToString("x2"));
                    var value = text.ToString();
                    return full ? value : value.Substring(0, 16) + "…";
                }
            }
            catch
            {
                return "(unavailable)";
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
