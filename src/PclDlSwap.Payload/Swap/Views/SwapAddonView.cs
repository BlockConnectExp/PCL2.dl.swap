using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                    list.Add(Row(records[index], addons));
                }
            }

            page.Children.Add(SwapUi.Card("已安装的 Addon", list.ToArray()));

            var chains = ScopedHook.Snapshot();
            if (chains.Count > 0)
            {
                var lines = chains.Select(chain => (FrameworkElement)SwapUi.Note("· " + chain)).ToList();
                lines.Add(SwapUi.Note("同一个宿主方法只有一条链：第一个钩子负责装 gate，后续模块加入这条链；" +
                                      "链上 handler 全部失效时调用直通原方法。"));
                page.Children.Add(SwapUi.Card("钩子链（模块作用域）", lines.ToArray()));
            }

            page.Children.Add(SwapUi.Card("说明",
                SwapUi.Note("开关立即生效并写入 modules.json（内置功能的钩子按开关放行/拦截）；" +
                            "模块状态、配置与已安装的 Addon 都放在数据目录里，可在 Swap Settings 打开。"),
                SwapUi.Note("注意：结构类修复（原版游戏页 / 下载侧栏）是启动时对宿主对象的一次性修复，" +
                            "关闭模块后要重启 PCL 才会完全回退；OTA、弹窗类拦截则立即停用。"),
                SwapUi.Buttons(
                    SwapUi.Button("打开 Addon 目录", () => OpenFolder(api?.Host.AddonDirectory)),
                    SwapUi.Button("刷新", () => PageRegistry.InvalidatePage("swap-addons")))));

            return Wrap(page);
        }

        // ------------------------------------------------------------------ rows

        private static FrameworkElement Row(AddonManager.AddonRecord record, AddonManager addons)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();

            var headline = new StackPanel { Orientation = Orientation.Horizontal };
            headline.Children.Add(SwapUi.Text(record.Name ?? record.Id, 13, false, false, FontWeights.SemiBold));
            var meta = (record.Version ?? string.Empty) + (record.BuiltIn ? "  ·  内置" : string.Empty);
            var hookCount = ScopedHook.CountFor(record.Id);
            if (hookCount > 0)
                meta += $"  ·  钩子 {hookCount}（命中 {ScopedHook.HitsFor(record.Id)}）";
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

            if (addons != null)
            {
                var id = record.Id;
                var toggle = SwapUi.Button(record.Enabled ? "禁用" : "启用", () =>
                {
                    if (!addons.SetEnabled(id, !record.Enabled)) return;
                    PageRegistry.InvalidatePage("swap-addons");
                });
                PclUi.SetMember(toggle, "Height", 28.0);
                PclUi.SetMember(toggle, "MinWidth", 76.0);
                PclUi.SetMember(toggle, "Margin", new Thickness(16, 0, 0, 0));
                toggle.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(toggle, 2);
                grid.Children.Add(toggle);
            }
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
                case "disabled": return "已禁用";
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
