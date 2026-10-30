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
            var i18n = SwapI18n.Current;

            page.Children.Add(SwapUi.Card(i18n.T("界面语言", "Language"),
                SwapUi.Combo(i18n.T("界面语言", "Language"),
                    new[] { i18n.T("跟随系统", "Follow system"), i18n.T("中文", "Chinese"), "English" },
                    (int)i18n.Language,
                    index =>
                    {
                        i18n.SetLanguage(index == 2 ? PCL2Swap.Api.SwapLanguage.English
                            : index == 1 ? PCL2Swap.Api.SwapLanguage.Chinese
                            : PCL2Swap.Api.SwapLanguage.Auto);
                        PageRegistry.InvalidatePage("swap-settings");
                    }),
                SwapUi.Buttons(
                    SwapUi.Button(i18n.T("立即翻译宿主界面", "Translate launcher now"), () =>
                    {
                        var hits = api?.Pcl?.Ui?.Translate(api.Pcl.Ui.MainWindow) ?? 0;
                        api?.Ui.Hint((i18n.IsEnglish ? "Translated " : "已翻译 ") + hits +
                                     (i18n.IsEnglish ? " strings." : " 处文本。"));
                    }),
                    SwapUi.Button(i18n.T("恢复原始中文", "Restore original Chinese"), () =>
                    {
                        var hits = api?.Pcl?.Ui?.Restore(api.Pcl.Ui.MainWindow) ?? 0;
                        api?.Ui.Hint((i18n.IsEnglish ? "Restored " : "已恢复 ") + hits +
                                     (i18n.IsEnglish ? " strings." : " 处文本。"));
                    }))));

            page.Children.Add(SwapUi.Card(i18n.T("宿主信息", "Host information"),
                SwapUi.InfoRow(i18n.T("宿主版本", "Host version"), api?.Host.SwapVersion),
                SwapUi.InfoRow(i18n.T("交付形状", "Delivery shape"), api?.Host.Shape.ToString()),
                SwapUi.InfoRow(i18n.T("API 版本", "API version"), api?.Host.ApiVersion),
                SwapUi.InfoRow(i18n.T("进程 PID", "Process ID"), api?.Host.ProcessId.ToString()),
                SwapUi.InfoRow(i18n.T("日志目录", "Log directory"), api?.Host.LogDirectory),
                SwapUi.InfoRow(i18n.T("Addon 目录", "Addon directory"), api?.Host.AddonDirectory)));

            page.Children.Add(SwapUi.Card(i18n.T("查毒策略", "Scan policy"),
                SwapUi.Note(i18n.T(
                    "只对启动器从 PCL 服务器拉取的文件做本地查毒（可执行/压缩类扩展名）；" +
                    "游戏文件、Mod、资源包、光影与元数据（JSON/图片等）不扫描。" +
                    "命中威胁时删除文件并中止下载。",
                    "Only files the launcher downloads from PCL servers are scanned locally " +
                    "(executable/archive extensions); game files, mods, resource packs, shaders " +
                    "and metadata are not scanned. A hit deletes the file and stops the download."))));

            page.Children.Add(SwapUi.Card(i18n.T("查毒引擎", "Scan engines"),
                SwapUi.InfoRow("Microsoft Defender",
                    i18n.T("内置，默认启用（本机扫描，不联网）", "Built in, enabled by default (local scan, offline)")),
                SwapUi.InfoRow("ClamAV", i18n.T("检测到本机安装即参与", "Runs when installed locally")),
                SwapUi.InfoRow("VirusTotal / MetaDefender Cloud",
                    i18n.T("需要 API Key，默认关闭", "Requires an API key; disabled by default"))));

            page.Children.Add(SwapUi.Card(i18n.T("目录", "Directories"),
                SwapUi.Note(i18n.T(
                    "日志与 Addon 都放在临时目录内，随 PCL 进程重建；Addon 包可随时放入。",
                    "Logs and addons live under the per-process data root; packages can be dropped in at any time.")),
                SwapUi.Buttons(
                    SwapUi.Button(i18n.T("打开日志目录", "Open log folder"),
                        () => SwapAddonView.OpenFolder(api?.Host.LogDirectory)),
                    SwapUi.Button(i18n.T("打开 Addon 目录", "Open addon folder"),
                        () => SwapAddonView.OpenFolder(api?.Host.AddonDirectory)))));

            return SwapAddonView.Wrap(page);
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
