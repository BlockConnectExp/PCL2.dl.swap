using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap.Views
{
    /// <summary>
    /// "Swap Addon" settings page: the installed addons with their live status, laid out with
    /// PCL's own MyCard / MyButton so it reads like a shipped settings page. Editing (enable /
    /// disable, import) arrives with the P1 addon manager work.
    /// </summary>
    internal static class SwapAddonView
    {
        public static FrameworkElement Build(ISwapApi api, AddonManager addons)
        {
            var page = SwapUi.Page();
            var records = addons?.Records;

            var list = new List<FrameworkElement>();
            if (records == null || records.Count == 0)
            {
                list.Add(SwapUi.Note("尚未发现任何 Addon。把 .swapaddon 包放进 Addon 目录后重启 PCL 即可加载。"));
            }
            else
            {
                for (var index = 0; index < records.Count; index++)
                {
                    if (index > 0) list.Add(Separator());
                    list.Add(Row(records[index]));
                }
            }

            page.Children.Add(SwapUi.Card("已安装的 Addon", list.ToArray()));
            page.Children.Add(SwapUi.Card("说明",
                SwapUi.Note("内置 Addon 由宿主装配阶段启用，开关编辑将在后续版本接入。"),
                SwapUi.Buttons(SwapUi.Button("打开 Addon 目录",
                    () => OpenFolder(api?.Host.AddonDirectory)))));

            return Wrap(page);
        }

        // ------------------------------------------------------------------ rows

        private static FrameworkElement Row(AddonManager.AddonRecord record)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();

            var headline = new StackPanel { Orientation = Orientation.Horizontal };
            headline.Children.Add(SwapUi.Text(record.Name ?? record.Id, 13, false, false, FontWeights.SemiBold));
            var meta = (record.Version ?? string.Empty) + (record.BuiltIn ? "  ·  内置" : string.Empty);
            if (meta.Trim().Length > 0)
            {
                var version = SwapUi.Text(meta, 11.5, true, false);
                version.Margin = new Thickness(8, 3, 0, 0);
                headline.Children.Add(version);
            }
            left.Children.Add(headline);

            if (!string.IsNullOrEmpty(record.Description))
            {
                var description = SwapUi.Text(record.Description, 11.5, true);
                description.Margin = new Thickness(0, 3, 12, 0);
                left.Children.Add(description);
            }

            if (record.Failure != null)
            {
                var failure = SwapUi.Text("错误：" + record.Failure.Message, 11.5, false);
                failure.Foreground = Brushes.IndianRed;
                failure.Margin = new Thickness(0, 3, 12, 0);
                left.Children.Add(failure);
            }

            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var status = SwapUi.Text(StatusText(record), 11.5, record.Status != "enabled", false);
            status.VerticalAlignment = VerticalAlignment.Top;
            status.Margin = new Thickness(16, 2, 0, 0);
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);
            return grid;
        }

        private static FrameworkElement Separator()
        {
            var line = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            SwapUi.UseBrush(line, Border.BorderBrushProperty, "ColorBrush6");
            line.Opacity = 0.6;
            return line;
        }

        private static string StatusText(AddonManager.AddonRecord record)
        {
            switch (record.Status)
            {
                case "enabled": return "已启用";
                case "failed": return "启动失败";
                case "unpacked": return "已解包（待启用）";
                default: return record.Status ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------ chrome

        /// <summary>
        /// PCL's animation helpers only recognise its own MyScrollViewer for the scroll-bar
        /// flourish. Build one through the host type when possible; a stock ScrollViewer remains
        /// the fallback so the page can never fail to render.
        /// </summary>
        internal static FrameworkElement Wrap(FrameworkElement content)
        {
            try
            {
                var scrollType = SwapUi.HostType("PCL.MyScrollViewer");
                if (scrollType != null)
                {
                    if (Activator.CreateInstance(scrollType) is ScrollViewer viewer)
                    {
                        viewer.Content = content;
                        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        SwapLog.Info("[page] 使用宿主 MyScrollViewer 承载视图。");
                        return viewer;
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[page] MyScrollViewer 构建失败，退回标准 ScrollViewer：" + ex.Message);
            }

            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        internal static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
