using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap.Views
{
    /// <summary>
    /// "Swap Settings" page: host facts and the current scan policy. Editable switches arrive in
    /// the P1 pass; today the page documents exactly what the runtime does.
    /// </summary>
    internal static class SwapSettingsView
    {
        public static FrameworkElement Build(ISwapApi api)
        {
            var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 24) };
            panel.Children.Add(SwapAddonView.Header("Swap Settings"));
            panel.Children.Add(SwapAddonView.Sub("宿主信息与全局策略；可编辑项将在后续版本接入。"));

            panel.Children.Add(InfoRow("宿主版本", api?.Host.SwapVersion));
            panel.Children.Add(InfoRow("交付形状", api?.Host.Shape.ToString()));
            panel.Children.Add(InfoRow("API 版本", api?.Host.ApiVersion));
            panel.Children.Add(InfoRow("进程 PID", api?.Host.ProcessId.ToString()));
            panel.Children.Add(InfoRow("日志目录", api?.Host.LogDirectory));
            panel.Children.Add(InfoRow("Addon 目录", api?.Host.AddonDirectory));

            panel.Children.Add(SectionHeader("查毒策略"));
            panel.Children.Add(Note(
                "只对启动器从 PCL 服务器拉取的文件做本地查毒（可执行/压缩类扩展名）；游戏文件、Mod、" +
                "资源包、光影与元数据（JSON/图片等）不扫描。命中威胁时删除文件并中止下载。"));

            panel.Children.Add(SectionHeader("查毒引擎"));
            panel.Children.Add(Note(
                "Microsoft Defender：内置，默认启用（本机扫描，不联网）。" +
                "ClamAV：检测到本机安装即参与。" +
                "VirusTotal / MetaDefender Cloud：需要 API Key，默认关闭。"));

            panel.Children.Add(SwapAddonView.NewButton("打开日志目录",
                () => SwapAddonView.OpenFolder(api?.Host.LogDirectory)));
            panel.Children.Add(SwapAddonView.NewButton("打开 Addon 目录",
                () => SwapAddonView.OpenFolder(api?.Host.AddonDirectory)));

            return SwapAddonView.Wrap(panel);
        }

        private static FrameworkElement InfoRow(string label, string value)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 96,
                Opacity = 0.6,
                FontSize = 12
            });
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "(unknown)" : value,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620
            });
            return row;
        }

        private static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 0)
        };

        private static TextBlock Note(string text) => new TextBlock
        {
            Text = text,
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        };
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
