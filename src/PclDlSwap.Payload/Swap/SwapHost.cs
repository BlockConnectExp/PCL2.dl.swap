using System;
using System.Reflection;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Runtime host for the Swap API platform: owns the API implementation, the addon list and the
    /// settings-page registration. Started by both delivery shapes - injection (ExLoaderEntry →
    /// CoreInstaller) and patch (static bootstrap inserted into PCL's early startup).
    /// </summary>
    public static class SwapHost
    {
        private static readonly object Gate = new object();
        private static bool _started;

        public static SwapShape Shape { get; private set; }
        public static ISwapApi Api { get; private set; }
        internal static AddonManager Addons { get; private set; }
        internal static Assembly HostAssembly { get; private set; }

        public static void Start(SwapShape shape, Assembly host)
        {
            try
            {
                lock (Gate)
                {
                    if (_started)
                    {
                        SwapLog.Info("SwapHost 已启动，忽略重复调用。");
                        return;
                    }
                    _started = true;
                    Shape = shape;
                    HostAssembly = host ?? Assembly.GetEntryAssembly();
                    Api = new HostApi(shape, HostAssembly);
                    SwapLog.Info($"SwapHost 启动（形状 {shape}，API {HostApi.ApiVersion}，宿主 " +
                                 $"{(HostAssembly?.Location ?? "(null)")}）。");

                    Addons = new AddonManager(Api);
                    Addons.RegisterBuiltIns();
                    PageRegistry.RegisterBuiltInPages(Api);   // built-ins own ids 100/101
                    Addons.LoadFromDisk();
                    Addons.EnableAll();                       // third-party pages register from 102 on

                    PageRegistry.Install(HostAssembly);
                    SelfTest.RunIfRequested(Api);
                    RunScenarioHooks();
                    SwapLog.Info("SwapHost 就绪。");
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("SwapHost 启动失败：" + ex);
            }
        }

        /// <summary>
        /// Remote-control hooks for the real-host end-to-end runs (docs/local-e2e.zh.md): the file
        /// picker cannot be driven from a script, so the harness asks the same manager entry points
        /// the Swap Addon page uses. Every use is logged; nothing happens without the env var.
        /// </summary>
        private static void RunScenarioHooks()
        {
            try
            {
                var install = Environment.GetEnvironmentVariable("PCL2SWAP_E2E_INSTALL");
                if (!string.IsNullOrWhiteSpace(install) && Addons != null)
                {
                    SwapLog.Info("[e2e] 安装模块（PCL2SWAP_E2E_INSTALL）：" + install);
                    var record = Addons.Install(install);
                    SwapLog.Info($"[e2e] 安装结果：{record.Id} {record.Version} 状态={record.Status}");
                    PageRegistry.RefreshSidebar();
                    var uninstall = Environment.GetEnvironmentVariable("PCL2SWAP_E2E_UNINSTALL");
                    if (!string.IsNullOrWhiteSpace(uninstall))
                    {
                        SwapLog.Info("[e2e] 立即卸载（PCL2SWAP_E2E_UNINSTALL）：" + uninstall);
                        SwapLog.Info("[e2e] 卸载结果：" + Addons.Uninstall(uninstall));
                        PageRegistry.RefreshSidebar();
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("[e2e] 场景钩子失败：" + ex);
            }
        }

        /// <summary>
        /// True when the named built-in addon is enabled. Hooks stay installed (nothing hot-unloads
        /// yet) but consult this before acting, so switching a module off behaves like removing it.
        /// Missing manager or unknown id keeps the feature on - a broken config must never disable
        /// the launcher's safety nets by accident.
        /// </summary>
        public static bool AddonEnabled(string addonId) => Addons?.IsEnabled(addonId) ?? true;

        // ---- host event bus -------------------------------------------------------------------
        // The platform's own pipelines call these; addons subscribe through IHooks. The bus is a
        // host facility, so it keeps publishing even while a module is switched off.

        internal static void RaiseDialogOpening(DialogEventArgs args) =>
            (Api as HostApi)?.RaiseDialogOpening(args);

        internal static void RaiseUpdateRequested(UpdateEventArgs args) =>
            (Api as HostApi)?.RaiseUpdateRequested(args);

        internal static void RaiseDownloadCompleted(DownloadEventArgs args) =>
            (Api as HostApi)?.RaiseDownloadCompleted(args);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
