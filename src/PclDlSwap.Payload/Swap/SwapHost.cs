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
                    SwapLog.Info("SwapHost 就绪。");
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("SwapHost 启动失败：" + ex);
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
