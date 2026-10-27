using System.Windows;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap.Views
{
    /// <summary>
    /// "Swap Settings" page: host facts and the current scan policy, laid out with PCL's own
    /// MyCard / MyButton so it reads like a shipped settings page. Editable switches arrive in
    /// the P1 pass; today the page documents exactly what the runtime does.
    /// </summary>
    internal static class SwapSettingsView
    {
        public static FrameworkElement Build(ISwapApi api)
        {
            var page = SwapUi.Page();

            page.Children.Add(SwapUi.Card("宿主信息",
                SwapUi.InfoRow("宿主版本", api?.Host.SwapVersion),
                SwapUi.InfoRow("交付形状", api?.Host.Shape.ToString()),
                SwapUi.InfoRow("API 版本", api?.Host.ApiVersion),
                SwapUi.InfoRow("进程 PID", api?.Host.ProcessId.ToString()),
                SwapUi.InfoRow("日志目录", api?.Host.LogDirectory),
                SwapUi.InfoRow("Addon 目录", api?.Host.AddonDirectory)));

            page.Children.Add(SwapUi.Card("查毒策略",
                SwapUi.Note("只对启动器从 PCL 服务器拉取的文件做本地查毒（可执行/压缩类扩展名）；" +
                            "游戏文件、Mod、资源包、光影与元数据（JSON/图片等）不扫描。" +
                            "命中威胁时删除文件并中止下载。")));

            page.Children.Add(SwapUi.Card("查毒引擎",
                SwapUi.InfoRow("Microsoft Defender", "内置，默认启用（本机扫描，不联网）"),
                SwapUi.InfoRow("ClamAV", "检测到本机安装即参与"),
                SwapUi.InfoRow("VirusTotal / MetaDefender Cloud", "需要 API Key，默认关闭")));

            page.Children.Add(SwapUi.Card("目录",
                SwapUi.Note("日志与 Addon 都放在临时目录内，随 PCL 进程重建；Addon 包可随时放入。"),
                SwapUi.Buttons(
                    SwapUi.Button("打开日志目录", () => SwapAddonView.OpenFolder(api?.Host.LogDirectory)),
                    SwapUi.Button("打开 Addon 目录", () => SwapAddonView.OpenFolder(api?.Host.AddonDirectory)))));

            return SwapAddonView.Wrap(page);
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
