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
                    list.Add(Row(records[index], api, addons));
                }
            }

            page.Children.Add(SwapUi.Card("已安装的 Addon", list.ToArray()));

            page.Children.Add(SwapUi.Card("模块管理",
                SwapUi.Note("安装会把包归档到 Addon 目录并立即启动，不需要重启 PCL；" +
                            "卸载会停掉模块、撤下它的设置页并删除模块目录（入口 DLL 被占用时留到下次启动清理）。"),
                SwapUi.Buttons(
                    SwapUi.Button("安装模块…", () => InstallFromDialog(api, addons)),
                    SwapUi.Button("打开 Addon 目录", () => OpenFolder(api?.Host.AddonDirectory)),
                    SwapUi.Button("刷新", () => PageRegistry.InvalidatePage("swap-addons")))));

            var chains = ScopedHook.Snapshot();
            if (chains.Count > 0)
            {
                var lines = chains.Select(ChainRow).ToList();
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

        /// <summary>
        /// One chain as a two-line block (target on the first line, module/mode/hits on the second)
        /// instead of a cramped single line - the text carries the load of the whole card.
        /// </summary>
        private static FrameworkElement ChainRow(string chain)
        {
            var parts = (chain ?? string.Empty).Split(new[] { " · " }, StringSplitOptions.None);
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(SwapUi.Text(parts.Length > 0 ? parts[0] : chain, 12.5, false, false));
            if (parts.Length > 1)
                stack.Children.Add(SwapUi.Text(string.Join(" · ", parts.Skip(1)), 11.5, true));
            return stack;
        }

        private static FrameworkElement Row(AddonManager.AddonRecord record, ISwapApi api, AddonManager addons)
        {
            var grid = new Grid { Margin = new Thickness(0, 9, 0, 9), MinHeight = 46 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

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

            var scope = ScopeText(record);
            if (scope.Length > 0)
            {
                var scopeText = SwapUi.Text(scope, 11, true, false);
                scopeText.Margin = new Thickness(0, 3, 12, 0);
                left.Children.Add(scopeText);
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
            status.TextAlignment = TextAlignment.Right;
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Margin = new Thickness(0, 0, 14, 0);
            if (record.Status == "enabled")
                SwapUi.UseBrush(status, TextBlock.ForegroundProperty, "ColorBrush2");
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            if (addons != null)
            {
                var id = record.Id;
                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var toggle = SwapUi.Button(record.Enabled ? "禁用" : "启用", () =>
                {
                    if (!addons.SetEnabled(id, !record.Enabled)) return;
                    PageRegistry.InvalidatePage("swap-addons");
                });
                PclUi.SetMember(toggle, "Height", 35.0);
                PclUi.SetMember(toggle, "MinWidth", 76.0);
                PclUi.SetMember(toggle, "Margin", new Thickness(0, 0, 8, 0));
                actions.Children.Add(toggle);

                if (!record.BuiltIn)
                {
                    var uninstall = SwapUi.Button("卸载", () => UninstallModule(api, addons, record), "Red");
                    PclUi.SetMember(uninstall, "Height", 35.0);
                    PclUi.SetMember(uninstall, "MinWidth", 76.0);
                    PclUi.SetMember(uninstall, "Margin", new Thickness(0));
                    actions.Children.Add(uninstall);
                }

                Grid.SetColumn(actions, 2);
                grid.Children.Add(actions);
            }
            return grid;
        }

        private static FrameworkElement Separator()
        {
            var line = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            SwapUi.UseBrush(line, Border.BorderBrushProperty, "ColorBrush6");
            line.Opacity = 0.7;
            return line;
        }

        // ------------------------------------------------------------------ module management

        /// <summary>Picks a .swapaddon with the Windows file dialog and installs it hot.</summary>
        private static void InstallFromDialog(ISwapApi api, AddonManager addons)
        {
            if (addons == null) return;
            try
            {
                var path = PickPackage();
                if (string.IsNullOrEmpty(path)) return;

                var record = addons.Install(path);
                PageRegistry.RefreshSidebar();
                PageRegistry.InvalidatePage("swap-addons");
                SwapLog.Info($"模块已安装：{record.Id} {record.Version}（状态 {record.Status}）");
                api?.Ui.Hint(record.Status == "enabled"
                    ? $"已安装并启用：{record.Name} {record.Version}"
                    : $"已安装：{record.Name} {record.Version}（{StatusText(record)}）");
            }
            catch (Exception ex)
            {
                SwapLog.Error("安装模块失败：" + ex.Message);
                api?.Ui.MsgBox("安装失败：" + Environment.NewLine + ex.Message + Environment.NewLine +
                               Environment.NewLine +
                               "模块包需要包含 manifest.json（id / version / entry / apiVersion）。",
                    "PCL2 Swap · 安装模块", "确定");
            }
        }

        private static void UninstallModule(ISwapApi api, AddonManager addons, AddonManager.AddonRecord record)
        {
            if (addons == null || record == null) return;
            try
            {
                var answer = 1;
                if (api != null)
                {
                    answer = api.Ui.MsgBox(
                        $"确定要卸载“{record.Name} {record.Version}”吗？" + Environment.NewLine + Environment.NewLine +
                        "模块立即停止（钩子放行原方法）、设置页从侧栏移除，模块目录与包文件被删除。" + Environment.NewLine +
                        "被 PCL 占用的 DLL 会留到下次启动时清理。",
                        "PCL2 Swap · 卸载模块", "卸载", "取消");
                }
                if (answer != 1)
                {
                    SwapLog.Info("卸载模块已取消：" + record.Id);
                    return;
                }

                if (!addons.Uninstall(record.Id))
                {
                    api?.Ui.MsgBox("卸载失败：内置模块不能卸载，请在列表里禁用。", "PCL2 Swap · 卸载模块", "确定");
                    return;
                }

                PageRegistry.RefreshSidebar();
                PageRegistry.InvalidatePage("swap-addons");
                api?.Ui.Hint($"已卸载：{record.Name} {record.Version}");
            }
            catch (Exception ex)
            {
                SwapLog.Error("卸载模块失败：" + ex.Message);
                api?.Ui.MsgBox("卸载失败：" + Environment.NewLine + ex.Message, "PCL2 Swap · 卸载模块", "确定");
            }
        }

        private static string PickPackage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 Swap 模块包",
                Filter = "Swap 模块包 (*.swapaddon)|*.swapaddon|ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>Manifest scope line: author, API range, shapes and permission count.</summary>
        private static string ScopeText(AddonManager.AddonRecord record)
        {
            var manifest = record.Manifest;
            if (manifest == null) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(manifest.Author)) parts.Add("作者 " + manifest.Author);
            if (!string.IsNullOrEmpty(manifest.ApiVersion)) parts.Add("API " + manifest.ApiVersion);
            if (manifest.Shapes != null && manifest.Shapes.Count > 0)
                parts.Add("形状 " + string.Join("/", manifest.Shapes));
            if (manifest.Permissions != null && manifest.Permissions.Count > 0)
                parts.Add("权限 " + manifest.Permissions.Count);
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }

        private static string StatusText(AddonManager.AddonRecord record)
        {
            switch (record.Status)
            {
                case "enabled": return "已启用";
                case "disabled": return "已禁用";
                case "failed": return "启动失败";
                case "unpacked": return "已解包（待启用）";
                case "skipped-shape": return "作用域不适用";
                case "removed": return "已卸载";
                case "registered": return "已登记";
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
