using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Sandbox;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Entry point of the injected payload. Runs after the host CLR exists and after PCL's main
    /// assembly is loaded, then installs the OTA block, the update-permission block and the
    /// sandbox/scan gate for PCL cloud downloads.
    /// </summary>
    public static class CoreInstaller
    {
        private static readonly object Gate = new object();
        private static bool _installed;
        private static SwapConfig _config;
        private static DownloadSandbox _sandbox;
        private static HostTarget _target;

        // Delegates and detours must stay reachable for the process lifetime: the CLR will collect
        // a delegate whose only reference is the native function pointer.
        private static DetourInstaller _updateStart;
        private static DetourInstaller _permission;
        private static DetourInstaller _downloadByLoaderEnumerable;
        private static DetourInstaller _downloadByLoaderString;
        private static DetourInstaller _downloadByClient;

        private static UpdateStartDelegate _updateStartHook;
        private static PermissionDelegate _permissionHook;
        private static DownloadListDelegate _downloadListHook;
        private static DownloadSingleDelegate _downloadSingleHook;
        private static DownloadByClientDelegate _downloadByClientHook;

        private static bool _otaDecisionRemembered;
        private static bool _allowNextUpdate;

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void UpdateStartDelegate(bool silent, IEnumerable<string> urlsOverride);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate bool PermissionDelegate();
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void DownloadListDelegate(IEnumerable<string> urls, string localPath, object loader, object checker, bool simulateBrowserHeaders);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void DownloadSingleDelegate(string url, string localPath, object loader, object checker, bool simulateBrowserHeaders);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void DownloadByClientDelegate(string url, string localPath, bool simulateBrowserHeaders);

        /// <summary>Entry point invoked by the native shim through ICLRRuntimeHost::ExecuteInDefaultAppDomain.</summary>
        public static int Activate(string argument)
        {
            try
            {
                lock (Gate)
                {
                    if (_installed)
                    {
                        SwapLog.Info("Activate 忽略重复调用（已安装）。");
                        return 0;
                    }
                    Boot(argument ?? string.Empty);
                    _installed = true;
                }
                return 0;
            }
            catch (Exception ex)
            {
                SwapLog.Error("激活失败：" + ex);
                return -1;
            }
        }

        /// <summary>Fallback entry point for hosts that already run a CLR 4 runtime.</summary>
        public static void Initialize()
        {
            Activate(string.Empty);
        }

        /// <summary>Environment switch used by the fixtures so they can install against stand-in types.</summary>
        private static bool TestHookEnabled => Environment.GetEnvironmentVariable("PCL2SWAP_TEST") == "1";

        /// <summary>Type/method names the guard binds to; defaults target the real PCL layout.</summary>
        private sealed class InstallProfile
        {
            public string SecretType = "PCL.ModSecret";
            public string UpdateStart = "UpdateStart";
            public string PermissionProperty = "get_HasUpdatePermission";
            public string NetType = "PCL.ModNet";
            public string DownloadByLoader = "NetDownloadByLoader";
            public string DownloadByClient = "NetDownloadByClient";
            public bool AgainstTokenManifest = true;
        }

        private static readonly InstallProfile PclProfile = new InstallProfile();
        private static readonly InstallProfile FixtureProfile = new InstallProfile
        {
            SecretType = "PclDlSwap.HookFixture.FixtureTarget",
            NetType = "PclDlSwap.HookFixture.FixtureTarget",
            DownloadByLoader = "DownloadByLoader",
            DownloadByClient = "DownloadByClient",
            AgainstTokenManifest = false
        };

        private static void Boot(string argument)
        {
            _config = SwapConfig.Load();
            _sandbox = new DownloadSandbox(_config);
            SwapLog.Info($"PCL2.dl.swap payload 启动（参数：{argument}，进程：{System.Diagnostics.Process.GetCurrentProcess().Id}）。");

            var host = WaitForHostAssembly(TimeSpan.FromMinutes(2));
            if (host == null)
            {
                SwapLog.Error("未能在等待窗口内找到宿主程序集，放弃安装。");
                return;
            }

            _target = OffsetManifest.Match;
            if (_target == null)
                SwapLog.Warn("当前宿主不在偏移清单中，将退化为按名称解析（可能不兼容）。");
            else
                SwapLog.Info($"宿主匹配：{_target.Id}（{_target.BuildType} {_target.Version}，{_target.Role}）。");

            if (TestHookEnabled)
            {
                SwapLog.Info("测试钩子已启用：真实安装路径跳过。");
                return;
            }

            InstallOtaBlock(host, PclProfile);
            InstallPermissionBlock(host, PclProfile);
            InstallDownloadGate(host, PclProfile);
            SwapLog.Info("装配完成。");
        }

        /// <summary>
        /// Installs the same OTA / permission / download guards against caller-supplied stand-in types.
        /// Used by the fixture project to prove the detour engine on real binaries.
        /// </summary>
        public static void TryInstallFixtures(Assembly host, bool acceptTestWrites)
        {
            try
            {
                _config = SwapConfig.Load();
                _sandbox = new DownloadSandbox(_config);
                _target = null;
                InstallOtaBlock(host ?? Assembly.GetEntryAssembly(), FixtureProfile);
                InstallPermissionBlock(host ?? Assembly.GetEntryAssembly(), FixtureProfile);
                InstallDownloadGate(host ?? Assembly.GetEntryAssembly(), FixtureProfile);
            }
            catch (Exception ex)
            {
                SwapLog.Error("夹具安装失败：" + ex);
            }
        }

        private static Assembly WaitForHostAssembly(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                // PCL.ModSecret is only JIT'd late; wait for the type so MethodHandle resolution is real.
                var host = Assembly.GetEntryAssembly();
                if (host != null && host.GetType("PCL.ModSecret", throwOnError: false) != null)
                    return host;
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetType("PCL.ModSecret", throwOnError: false) != null);
                if (loaded != null) return loaded;
                Thread.Sleep(250);
            }
            return Assembly.GetEntryAssembly();
        }

        private static MethodInfo Resolve(Assembly host, InstallProfile profile, string manifestKey, string method, bool isStatic)
        {
            MethodInfo resolved = null;
            if (profile.AgainstTokenManifest && _target != null && _target.Tokens.TryGetValue(manifestKey, out var token))
                resolved = DetourInstaller.ResolveByToken(host, token);
            if (resolved == null)
            {
                resolved = DetourInstaller.ResolveTypeMethod(host, profile.SecretType, method, isStatic);
                if (resolved != null)
                    SwapLog.Warn($"方法 {profile.SecretType}.{method} 未按 metadata token 命中，已按名称解析（新版本请补充偏移清单）。");
            }
            return resolved;
        }

        private static void InstallOtaBlock(Assembly host, InstallProfile profile)
        {
            if (!_config.BlockSilentOta)
            {
                SwapLog.Info("OTA 拦截按配置跳过。");
                return;
            }
            var method = Resolve(host, profile, profile.SecretType + ".UpdateStart", profile.UpdateStart, true);
            if (method == null)
            {
                SwapLog.Error("找不到 PCL.ModSecret.UpdateStart，OTA 拦截未安装。");
                return;
            }

            _updateStartHook = OnUpdateStart;
            _updateStart = DetourInstaller.Install(method, _updateStartHook);
            SwapLog.Info($"OTA 拦截已安装：token=0x{method.MetadataToken:X8} target=0x{_updateStart.Target.ToInt64():X} trampoline=0x{_updateStart.Trampoline.ToInt64():X}");
        }

        private static void InstallPermissionBlock(Assembly host, InstallProfile profile)
        {
            if (!_config.DenyUpdatePermission)
            {
                SwapLog.Info("更新权限门按配置跳过。");
                return;
            }
            var method = Resolve(host, profile, profile.SecretType + ".get_HasUpdatePermission", profile.PermissionProperty, true);
            if (method == null)
            {
                SwapLog.Warn("找不到 PCL.ModSecret.get_HasUpdatePermission，跳过权限门。");
                return;
            }
            _permissionHook = () => false;
            _permission = DetourInstaller.Install(method, _permissionHook);
            SwapLog.Info("更新权限门已安装：HasUpdatePermission 恒为 false。");
        }

        private static void InstallDownloadGate(Assembly host, InstallProfile profile)
        {
            if (!_config.InterceptPclCloudDownloads)
            {
                SwapLog.Info("下载沙箱门按配置跳过。");
                return;
            }
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var type = host.GetType(profile.NetType, throwOnError: false);
                if (type == null)
                {
                    SwapLog.Error($"找不到 {profile.NetType}，下载沙箱门未安装。");
                    return;
                }

                var methods = type.GetMethods(flags).Where(m => m.Name == profile.DownloadByLoader).ToList();
                SwapLog.Info($"{profile.NetType}.{profile.DownloadByLoader} 重载数：{methods.Count}");
                var enumerable = methods.FirstOrDefault(m =>
                {
                    var parameters = m.GetParameters();
                    return parameters.Length >= 2 && parameters[0].ParameterType != typeof(string);
                });
                var single = methods.FirstOrDefault(m =>
                {
                    var parameters = m.GetParameters();
                    return parameters.Length >= 2 && parameters[0].ParameterType == typeof(string);
                });

                if (enumerable != null)
                {
                    _downloadListHook = OnDownloadByLoader;
                    _downloadByLoaderEnumerable = DetourInstaller.Install(enumerable, _downloadListHook);
                    SwapLog.Info($"云下载沙箱门已安装：{profile.DownloadByLoader}(IEnumerable)，token=0x{enumerable.MetadataToken:X8} | {_downloadByLoaderEnumerable.Verify()}");
                }
                if (single != null)
                {
                    _downloadSingleHook = OnDownloadByLoaderSingle;
                    _downloadByLoaderString = DetourInstaller.Install(single, _downloadSingleHook);
                    SwapLog.Info($"云下载沙箱门已安装：{profile.DownloadByLoader}(string)，token=0x{single.MetadataToken:X8} | {_downloadByLoaderString.Verify()}");
                }

                var client = DetourInstaller.ResolveTypeMethod(host, profile.NetType, profile.DownloadByClient, true);
                if (client != null)
                {
                    _downloadByClientHook = OnDownloadByClient;
                    _downloadByClient = DetourInstaller.Install(client, _downloadByClientHook);
                    SwapLog.Info($"云下载沙箱门已安装：{profile.DownloadByClient}，token=0x{client.MetadataToken:X8}");
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载沙箱门安装失败：" + ex);
            }
        }

        private static void OnUpdateStart(bool silent, IEnumerable<string> urlsOverride)
        {
            try
            {
                var urls = urlsOverride?.ToList();
                if (!silent && !_allowNextUpdate)
                {
                    // Owner asked for it explicitly: sandbox the payload, then let the original flow run.
                    _updateStart.Original<UpdateStartDelegate>().Invoke(false, urls);
                    return;
                }

                if (_allowNextUpdate)
                {
                    _allowNextUpdate = false;
                    SwapLog.Info("OTA：按用户授权放行一次更新。");
                    _updateStart.Original<UpdateStartDelegate>().Invoke(false, urls);
                    return;
                }

                var detail = urls != null && urls.Count > 0
                    ? string.Join(Environment.NewLine, urls)
                    : "（服务器未给出下载链接，或更新信息在宿主内部获取）";

                SwapLog.Warn("拦截到静默 OTA 更新请求，等待用户决定：" + string.Join(" | ", urls ?? Enumerable.Empty<string>()));
                var choice = Dialogs.OnStaThread(() => Dialogs.AskAboutSilentUpdate(detail), Dialogs.OtaChoice.Cancel);

                switch (choice)
                {
                    case Dialogs.OtaChoice.Update:
                        _allowNextUpdate = true;
                        SwapLog.Info("OTA：用户选择更新，转交原始流程（下载将被沙箱与查毒门拦截）。");
                        _updateStart.Original<UpdateStartDelegate>().Invoke(false, urls);
                        break;
                    case Dialogs.OtaChoice.AbortAutoUpdate:
                        _otaDecisionRemembered = true;
                        SwapLog.Info("OTA：用户选择取消更新，保持当前版本。");
                        break;
                    default:
                        SwapLog.Info("OTA：用户取消。");
                        break;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("OTA 钩子异常，已回退到原始行为：" + ex);
                try { _updateStart.Original<UpdateStartDelegate>().Invoke(silent, urlsOverride); } catch { }
            }
        }

        private static bool OnPermission()
        {
            if (_otaDecisionRemembered || _config.BlockSilentOta)
                return false;
            return _permission.Original<PermissionDelegate>().Invoke();
        }

        private static void OnDownloadByLoader(IEnumerable<string> urls, string localPath, object loader, object checker, bool simulateBrowserHeaders)
        {
            var list = urls?.ToList() ?? new List<string>();
            bool isCloud = IsPclCloud(list);
            SwapLog.Info($"下载门回调：isCloud={isCloud} path={localPath} urls={(list.Count == 0 ? "(empty)" : string.Join(",", list))}");
            if (Environment.GetEnvironmentVariable("PCL2SWAP_TEST_NOOP") == "1")
            {
                // Argument-correctness probe: log and forward without touching the sandbox.
                _downloadByLoaderEnumerable.Original<DownloadListDelegate>().Invoke(urls, localPath, loader, checker, simulateBrowserHeaders);
                return;
            }
            if (!isCloud)
            {
                _downloadByLoaderEnumerable.Original<DownloadListDelegate>().Invoke(urls, localPath, loader, checker, simulateBrowserHeaders);
                return;
            }

            SwapLog.Info("PCL 云下载进入沙箱：" + localPath);
            var result = _sandbox.FetchAndPublish(list.ToArray(), localPath);
            SwapLog.Info("沙箱结果：" + (result.Success ? "通过" : result.Blocked ? "拦截" : "失败") + " | " + result.Summary);

            if (!result.Success)
            {
                if (result.Blocked)
                    Dialogs.OnStaThread(() => { Dialogs.BlockedDownload(localPath, result.Summary); return 0; }, 0);
                // Never let a blocked payload reach the caller: the host's own error handling will surface it.
                throw new InvalidOperationException("PCL2 DLL-Swap 已阻止未通过病毒检测的下载：" + result.Summary);
            }
        }

        private static void OnDownloadByLoaderSingle(string url, string localPath, object loader, object checker, bool simulateBrowserHeaders)
        {
            if (!IsPclCloud(new[] { url }))
            {
                _downloadByLoaderString.Original<DownloadSingleDelegate>().Invoke(url, localPath, loader, checker, simulateBrowserHeaders);
                return;
            }

            SwapLog.Info("PCL 云下载进入沙箱（单链接重载）：" + localPath);
            var result = _sandbox.FetchAndPublish(new[] { url }, localPath);
            SwapLog.Info("沙箱结果：" + (result.Success ? "通过" : result.Blocked ? "拦截" : "失败") + " | " + result.Summary);
            if (!result.Success)
            {
                if (result.Blocked)
                    Dialogs.OnStaThread(() => { Dialogs.BlockedDownload(localPath, result.Summary); return 0; }, 0);
                throw new InvalidOperationException("PCL2 DLL-Swap 已阻止未通过病毒检测的下载：" + result.Summary);
            }
        }

        private static void OnDownloadByClient(string url, string localPath, bool simulateBrowserHeaders)
        {
            if (!IsPclCloud(new[] { url }))
            {
                _downloadByClient.Original<DownloadByClientDelegate>().Invoke(url, localPath, simulateBrowserHeaders);
                return;
            }

            SwapLog.Info("PCL 云下载（单链接）进入沙箱：" + localPath);
            var result = _sandbox.FetchAndPublish(new[] { url }, localPath);
            if (!result.Success)
            {
                if (result.Blocked)
                    Dialogs.OnStaThread(() => { Dialogs.BlockedDownload(localPath, result.Summary); return 0; }, 0);
                throw new InvalidOperationException("PCL2 DLL-Swap 已阻止未通过病毒检测的下载：" + result.Summary);
            }
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