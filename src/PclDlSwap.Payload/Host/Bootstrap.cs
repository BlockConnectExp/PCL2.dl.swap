using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using PclDlSwap.Payload.Sandbox;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Entry points used by the native CLR profiler / loader:
    ///
    ///   Bootstrap(argument)   -> installs the managed logic and writes
    ///                            data\hook-pointers.bin  (int64[3]) and
    ///                            data\rewrite-plan.tsv   (target tokens + signatures)
    ///   DownloadHook/ OtaHook / PermissionHook -> the actual rewritten method bodies
    ///
    /// The hooks are plain static methods whose native entry points are handed to the
    /// profiler; the profiler rewrites the host's IL to `calli` into them, which keeps
    /// tokens out of the target assembly's metadata entirely.
    /// </summary>
    public static class HookEntryPoints
    {
        public static SwapConfig Config { get; private set; }
        private static DownloadSandbox _sandbox;

        public static int Bootstrap(string argument)
        {
            try
            {
                Config = SwapConfig.Load();
                _sandbox = new DownloadSandbox(Config);
                SwapLog.Info("Bootstrap 进入（参数：" + argument + "，PID=" + Process.GetCurrentProcess().Id + "）。");

                var watchdog = new UpdateLandingWatchdog(Config);
                watchdog.Start();
                SwapLog.Info("更新落地看门狗已启动。");

                InstallDownloadGate();

                if (string.Equals(argument, "disk-patch", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "bootstrap-only", StringComparison.OrdinalIgnoreCase))
                {
                    SwapLog.Info("磁盘补丁模式：跳过 profiler 钩子地址导出。");
                    return 0;
                }

                var table = new long[3];
                table[0] = GetAddress((Action<IEnumerable<string>, string, object, object, bool>)DownloadHook);
                table[1] = GetAddress((Action<bool, IEnumerable<string>>)OtaHook);
                table[2] = GetAddress((Func<bool>)PermissionHook);

                WritePointerTable(table);
                WriteRewritePlan();
                SwapLog.Info($"钩子地址：download=0x{table[0]:X} ota=0x{table[1]:X} permission=0x{table[2]:X}");
                return 0;
            }
            catch (Exception ex)
            {
                SwapLog.Error("Bootstrap 失败：" + ex);
                return -1;
            }
        }

        /// <summary>
        /// Restores the download page loader. 2.99.0 replaced the fifth argument of
        /// <c>PageDownloadInstall.PageLoaderInit(...)</c> with a throwing stub; the real loader
        /// (<c>ModDownload.DlClientListLoader</c>) is still present, so we clear the gated flag and
        /// let the host's own loader do the work. The actual IL rewiring happens in the patcher.
        /// </summary>
        private static void InstallDownloadGate()
        {
            try
            {
                var type = Assembly.GetEntryAssembly()?.GetType("PCL.ModDownload", throwOnError: false);
                var loader = type?.GetField("DlClientListLoader", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (loader == null)
                {
                    SwapLog.Warn("未找到 ModDownload.DlClientListLoader：该构建可能已删除下载模块，需要携带自持模块。");
                    return;
                }
                SwapLog.Info("下载模块在位：ModDownload.DlClientListLoader = " + (loader.GetValue(null)?.ToString() ?? "(null)"));
            }
            catch (Exception ex)
            {
                SwapLog.Warn("下载模块探测失败：" + ex.Message);
            }
        }

        /// <summary>Entry point used by the disk-patched <c>ModSecret.UpdateStart</c>.</summary>
        public static void HandleUpdateStart(bool silent, string[] urls)
        {
            try
            {
                OtaHook(silent, urls);
            }
            catch (Exception ex)
            {
                SwapLog.Error("HandleUpdateStart 异常，已按取消处理：" + ex);
            }
        }

        /// <summary>Entry point used by the disk-patched <c>ModSecret.HasUpdatePermission</c>.</summary>
        public static bool HandlePermissionQuery()
        {
            try
            {
                return PermissionHook();
            }
            catch (Exception ex)
            {
                SwapLog.Error("HandlePermissionQuery 异常，按拒绝处理：" + ex);
                return false;
            }
        }

        /// <summary>
        /// Entry point used by the disk-patched download path. The host's own download engine is left
        /// intact: the payload only gates entries whose URL belongs to the PCL cloud, stages them in
        /// the sandbox and scans them before they reach their destination.
        /// </summary>
        public static void HandleDownload(object urls, string localPath, object loader, object checker, bool simulateBrowserHeaders)
        {
            try
            {
                var list = Materialize(urls);
                if (!IsPclCloud(list) || !Config.InterceptPclCloudDownloads)
                {
                    SwapLog.Info("下载门放行非 PCL 云下载：" + localPath);
                    return;
                }

                var result = _sandbox.FetchAndPublish(list.ToArray(), localPath);
                SwapLog.Info("沙箱结果：" + (result.Success ? "通过" : result.Blocked ? "拦截" : "失败") + " | " + result.Summary);
                if (result.Success) return;

                if (result.Blocked)
                    Dialogs.OnStaThread(() => { Dialogs.BlockedDownload(localPath, result.Summary); return 0; }, 0);
                throw new InvalidOperationException("PCL2 DLL-Swap 已阻止未通过病毒检测的下载：" + result.Summary);
            }
            catch (Exception ex)
            {
                SwapLog.Error("HandleDownload 异常：" + ex);
                throw;
            }
        }

        private static List<string> Materialize(object urls)
        {
            var list = new List<string>();
            if (urls == null) return list;
            if (urls is string single)
            {
                list.Add(single);
                return list;
            }
            if (urls is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var text = item?.ToString();
                    if (!string.IsNullOrEmpty(text)) list.Add(text);
                }
            }
            return list;
        }

        /// <summary>Rewritten PCL.ModNet.NetDownloadByLoader body.</summary>
        public static void DownloadHook(IEnumerable<string> urls, string localPath, object loader, object checker, bool simulateBrowserHeaders)
        {
            var list = urls?.ToList() ?? new List<string>();
            bool isCloud = IsPclCloud(list);
            SwapLog.Info($"下载钩子：isCloud={isCloud} path={localPath} urls={(list.Count == 0 ? "(empty)" : string.Join(",", list))}");

            // Note: rewriting NetDownloadByLoader replaces the host's download engine wholesale,
            // so this hook performs both roles itself instead of forwarding: PCL cloud payloads go
            // through the sandbox scanner, everything else uses a plain HTTP download into the
            // caller's destination. That keeps behaviour correct without needing the original body.
            try
            {
                if (isCloud && Config.InterceptPclCloudDownloads)
                {
                    var result = _sandbox.FetchAndPublish(list.ToArray(), localPath);
                    SwapLog.Info("沙箱结果：" + (result.Success ? "通过" : result.Blocked ? "拦截" : "失败") + " | " + result.Summary);
                    if (result.Success) return;

                    if (result.Blocked)
                        Dialogs.OnStaThread(() => { Dialogs.BlockedDownload(localPath, result.Summary); return 0; }, 0);
                    throw new InvalidOperationException("PCL2 DLL-Swap 已阻止未通过病毒检测的下载：" + result.Summary);
                }

                DirectDownload(list, localPath);
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载钩子失败：" + ex);
                throw;
            }
        }

        /// <summary>Fallback downloader for non-cloud sources after the host engine is replaced.</summary>
        private static void DirectDownload(List<string> urls, string localPath)
        {
            if (urls.Count == 0) throw new ArgumentException("下载链接为空", nameof(urls));
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            Exception last = null;
            foreach (var url in urls)
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "PCL2/DLLSwap");
                        client.DownloadFile(url, localPath);
                    }
                    ZoneIdentifier.Stamp(localPath);
                    SwapLog.Info("直连下载完成：" + localPath);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    SwapLog.Warn("直连下载失败（尝试下一个源）：" + url + " -> " + ex.Message);
                }
            }
            throw last ?? new IOException("所有下载源均失败");
        }

        /// <summary>Rewritten PCL.ModSecret.UpdateStart body. Silent OTA is cancelled; explicit updates continue.</summary>
        public static void OtaHook(bool silent, IEnumerable<string> urlsOverride)
        {
            var urls = urlsOverride?.ToList() ?? new List<string>();
            if (!silent)
            {
                PclHostBridge.UpdateStartDirect(false, urlsOverride);
                return;
            }

            SwapLog.Warn("拦截到静默 OTA：" + string.Join(" | ", urls));
            var choice = Dialogs.OnStaThread(
                () => Dialogs.AskAboutSilentUpdate(urls.Count > 0 ? string.Join(Environment.NewLine, urls) : "（服务器未给出下载链接）"),
                Dialogs.OtaChoice.Cancel);

            if (choice != Dialogs.OtaChoice.Update)
            {
                SwapLog.Info("静默 OTA 已取消（用户选择：" + choice + "）。");
                return;
            }

            SwapLog.Info("用户允许更新一次，转交原始流程（下载会先过沙箱）。");
            PclHostBridge.UpdateStartDirect(false, urlsOverride);
        }

        /// <summary>Rewritten PCL.ModSecret.get_HasUpdatePermission body.</summary>
        public static bool PermissionHook()
        {
            if (Config.DenyUpdatePermission)
            {
                SwapLog.Info("更新权限门查询被拒绝（DenyUpdatePermission=true）。");
                return false;
            }
            return PclHostBridge.HasUpdatePermissionDirect();
        }

        private static long GetAddress(Delegate hook)
        {
            var pointer = Marshal.GetFunctionPointerForDelegate(hook);
            HookRoot.Add(hook);              // keep every delegate alive for the process lifetime
            return pointer.ToInt64();
        }

        internal static readonly List<Delegate> HookRoot = new List<Delegate>();

        private static void WritePointerTable(long[] table)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hook-pointers.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var value in table) writer.Write(value);
            }
            SwapLog.Info("已写出钩子地址表：" + path);
        }

        private static void WriteRewritePlan()
        {
            var manifest = OffsetManifest.All;
            var lines = new List<string>
            {
                "# moduleMvid\ttargetToken\thookIndex\thasThis\targCount\treturnsValue\tmethodName"
            };

            foreach (var target in manifest)
            {
                bool isPrimary = string.Equals(target.Role, "primary-injection-target", StringComparison.OrdinalIgnoreCase);
                bool isBaseline = string.Equals(target.Role, "extraction-baseline", StringComparison.OrdinalIgnoreCase);
                if (!isPrimary && !isBaseline) continue;

                AddPlan(lines, target.Mvid, target, "PCL.ModNet.NetDownloadByLoader", 0, true, 5, false,
                        "PCL.PageDownloadInstall.LoaderInit");
                AddPlan(lines, target.Mvid, target, "PCL.ModSecret.UpdateStart", 1, true, 2, false, null);
                AddPlan(lines, target.Mvid, target, "PCL.ModSecret.get_HasUpdatePermission", 2, true, 0, true, null);
            }

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "rewrite-plan.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            SwapLog.Info("已写出重写计划：" + path + "（" + (lines.Count - 1) + " 条）");
        }

        private static void AddPlan(List<string> lines, string mvid, HostTarget target, string methodKey,
            int hookIndex, bool hasThis, int argCount, bool returnsValue, string legacyTokenKey)
        {
            if (target.Tokens == null) return;
            string token;
            if (!target.Tokens.TryGetValue(methodKey, out token) && legacyTokenKey != null)
                target.Tokens.TryGetValue(legacyTokenKey, out token);
            if (string.IsNullOrEmpty(token)) return;

            lines.Add(string.Join("\t", new[]
            {
                (mvid ?? string.Empty).ToLowerInvariant(),
                token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token.Substring(2) : token,
                hookIndex.ToString(),
                hasThis ? "1" : "0",
                argCount.ToString(),
                returnsValue ? "1" : "0",
                methodKey
            }));
        }

        private static bool IsPclCloud(IEnumerable<string> urls)
        {
            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (url.IndexOf("pcl2-server", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (url.IndexOf("meloong.com", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (url.IndexOf("pclc.cc", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (url.IndexOf("pcl2.net", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover