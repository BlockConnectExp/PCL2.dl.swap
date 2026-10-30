using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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

            // Manager-style detail view: the row's 详情 button selects a module and the page is
            // rebuilt around it (metadata, audit trail, hook chains, log tail, actions).
            var selected = addons?.Selected;
            if (selected != null) page.Children.Add(Detail(api, addons, selected));

            page.Children.Add(SwapUi.Card("模块管理",
                SwapUi.Note("安装会把包归档到 Addon 目录并立即启动，不需要重启 PCL；" +
                            "卸载会停掉模块、撤下它的设置页并删除模块目录（入口 DLL 被占用时留到下次启动清理）。"),
                SwapUi.Buttons(
                    SwapUi.Button("安装模块…", () => InstallFromDialog(api, addons)),
                    SwapUi.Button("打开 Addon 目录", () => OpenFolder(api?.Host.AddonDirectory)),
                    SwapUi.Button("刷新", () => PageRegistry.InvalidatePage("swap-addons")))));

            page.Children.Add(RepositoryCard(api, addons));

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
                // Wraps instead of clipping: a declared scope (shapes + host versions + permissions)
                // is longer than one line once the action buttons take their share of the row.
                var scopeText = SwapUi.Text(scope, 11, true);
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

            var hookErrors = ScopedHook.ErrorsFor(record.Id);
            if (hookErrors > 0)
            {
                // Isolation report: a failing handler is deactivated on its first throw, so this is a
                // record of "your module broke N times", not a still-broken state.
                var errors = SwapUi.Text($"钩子抛错 {hookErrors} 次（失败的钩子已自动停用，详见该模块日志）",
                    11.5, false);
                errors.Foreground = Brushes.IndianRed;
                errors.Margin = new Thickness(0, 3, 12, 0);
                left.Children.Add(errors);
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
                var detail = SwapUi.Button("详情", () =>
                {
                    addons.SelectedAddonId = id;
                    PageRegistry.InvalidatePage("swap-addons");
                });
                PclUi.SetMember(detail, "Height", 35.0);
                PclUi.SetMember(detail, "MinWidth", 56.0);
                PclUi.SetMember(detail, "Margin", new Thickness(0, 0, 8, 0));
                actions.Children.Add(detail);

                PclUi.SetMember(toggle, "Height", 35.0);
                PclUi.SetMember(toggle, "MinWidth", 76.0);
                PclUi.SetMember(toggle, "Margin", new Thickness(0, 0, 8, 0));
                actions.Children.Add(toggle);

                var logButton = SwapUi.Button("日志", () => OpenModuleLog(api, addons, record));
                PclUi.SetMember(logButton, "Height", 35.0);
                PclUi.SetMember(logButton, "MinWidth", 64.0);
                PclUi.SetMember(logButton, "Margin", new Thickness(0, 0, 8, 0));
                actions.Children.Add(logButton);

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

        /// <summary>
        /// Per-module detail view: everything the manager knows about one module - metadata, the audit
        /// trail (where the package came from, its hash, when it landed), that module's own hook
        /// chains, the tail of its log, and the lifecycle actions (toggle, reload, log, config,
        /// uninstall).
        /// </summary>
        private static FrameworkElement Detail(ISwapApi api, AddonManager addons,
            AddonManager.AddonRecord record)
        {
            var manifest = record.Manifest;
            var content = new List<FrameworkElement>
            {
                SwapUi.InfoRow("状态", StatusText(record)),
                SwapUi.InfoRow("id / 版本", record.Id + "  " + record.Version),
                SwapUi.InfoRow("安装时间", record.InstalledAtUtc.HasValue
                    ? record.InstalledAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : "（内置模块）"),
                SwapUi.InfoRow("来源包", string.IsNullOrEmpty(record.SourcePath) ? "（内置模块）" : record.SourcePath),
                SwapUi.InfoRow("包 SHA-256", string.IsNullOrEmpty(record.Sha256) ? "（内置模块）" : record.Sha256),
                SwapUi.InfoRow("签名状态", string.IsNullOrEmpty(record.Signature) ? "（未记录）" : record.Signature),
                SwapUi.InfoRow("模块目录", string.IsNullOrEmpty(record.Directory) ? "（内置模块）" : record.Directory),
                SwapUi.InfoRow("配置文件", addons.GetModuleConfigPath(record.Id))
            };

            if (manifest != null)
            {
                content.Add(SwapUi.InfoRow("API 范围", manifest.ApiVersion));
                content.Add(SwapUi.InfoRow("入口", manifest.Entry));
                if (!string.IsNullOrEmpty(manifest.Author)) content.Add(SwapUi.InfoRow("作者", manifest.Author));
                content.Add(SwapUi.InfoRow("形状作用域", manifest.Shapes.Count == 0
                    ? "（未声明 —— 全部形状）"
                    : string.Join("、", manifest.Shapes)));
                content.Add(SwapUi.InfoRow("宿主版本", manifest.HostVersions.Count == 0
                    ? "（未声明 —— 任意版本）"
                    : string.Join("、", manifest.HostVersions)));
                content.Add(SwapUi.InfoRow("权限声明", manifest.Permissions.Count == 0
                    ? "（未声明 —— 兼容模式，全部访问）"
                    : string.Join("、", manifest.Permissions) + "（严格门控）"));
            }

            var chains = ScopedHook.Snapshot()
                .Where(line => line.IndexOf(record.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            var hooks = ScopedHook.CountFor(record.Id);
            var hits = ScopedHook.HitsFor(record.Id);
            var errors = ScopedHook.ErrorsFor(record.Id);
            content.Add(SwapUi.InfoRow("钩子", hooks == 0
                ? "（无）"
                : $"{hooks} 个（命中 {hits}，抛错 {errors}）"));
            if (chains.Count > 0) content.Add(SwapUi.Note(string.Join("；", chains)));

            var tail = addons.LogTail(record.Id, 12);
            content.Add(SwapUi.Note("模块日志尾部："));
            content.Add(SwapUi.Text(string.IsNullOrEmpty(tail) ? "（这个模块还没有日志）" : tail, 11, true));

            var actions = new List<FrameworkElement>
            {
                ActionButton(record.Enabled ? "禁用" : "启用", () =>
                {
                    addons.SetEnabled(record.Id, !record.Enabled);
                    PageRegistry.InvalidatePage("swap-addons");
                }),
                ActionButton("重载", () =>
                {
                    addons.Reload(record.Id);
                    PageRegistry.InvalidatePage("swap-addons");
                }),
                ActionButton("打开日志", () => OpenModuleLog(api, addons, record)),
                ActionButton("打开配置目录", () =>
                    OpenFolder(System.IO.Path.GetDirectoryName(addons.GetModuleConfigPath(record.Id)))),
                ActionButton("收起", () =>
                {
                    addons.SelectedAddonId = null;
                    PageRegistry.InvalidatePage("swap-addons");
                })
            };
            if (!record.BuiltIn)
                actions.Add(ActionButton("卸载", () => UninstallModule(api, addons, record), "Red"));
            content.Add(SwapUi.Buttons(actions.ToArray()));

            return SwapUi.Card("模块详情：" + (record.Name ?? record.Id), content.ToArray());
        }

        /// <summary>
        /// Compact native button for the detail action strip: the shipped default is 140px wide,
        /// which overflows a card once five or six actions sit on one line.
        /// </summary>
        private static FrameworkElement ActionButton(string text, Action onClick,
            string colorType = null, double minWidth = 100)
        {
            var button = SwapUi.Button(text, onClick, colorType);
            PclUi.SetMember(button, "Height", 35.0);
            PclUi.SetMember(button, "MinWidth", minWidth);
            PclUi.SetMember(button, "Margin", new Thickness(0, 0, 8, 0));
            return button;
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

                // Review-before-install: the manifest, the declared scope and the requested
                // permissions are shown first, so a package never reaches the disk unannounced.
                var review = addons.Review(path);
                var manifest = review.Manifest;
                var preview = addons.DescribeForReview(manifest, path, review.Trust);
                var existing = addons.Find(manifest.Id);
                var answer = 1;
                if (api != null)
                {
                    answer = api.Ui.MsgBox(preview,
                        existing == null ? "PCL2 Swap · 安装模块" : "PCL2 Swap · 更新模块",
                        existing == null ? "安装" : "更新", "取消");
                }
                if (answer != 1)
                {
                    SwapLog.Info("安装模块已取消（预览阶段）：" + manifest.Id);
                    return;
                }

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

        /// <summary>Opens a module's own log; shows the folder while the module has not logged yet.</summary>
        private static void OpenModuleLog(ISwapApi api, AddonManager addons, AddonManager.AddonRecord record)
        {
            try
            {
                var path = addons?.GetModuleLogPath(record.Id);
                if (string.IsNullOrEmpty(path)) return;

                if (System.IO.File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return;
                }

                var folder = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder)) return;
                System.IO.Directory.CreateDirectory(folder);
                OpenFolder(folder);
                api?.Ui.Hint("这个模块还没有日志；已经打开日志目录。");
            }
            catch (Exception ex)
            {
                SwapLog.Warn("打开模块日志失败：" + ex.Message);
            }
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
            if (manifest.HostVersions != null && manifest.HostVersions.Count > 0)
                parts.Add("宿主 " + string.Join("、", manifest.HostVersions));
            if (manifest.Permissions != null && manifest.Permissions.Count > 0)
            {
                var shown = manifest.Permissions.Take(3).ToList();
                var text = "权限 " + string.Join("、", shown);
                if (manifest.Permissions.Count > shown.Count) text += " 等 " + manifest.Permissions.Count + " 项";
                text += "（严格）";
                parts.Add(text);
            }
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
                case "skipped-host": return "宿主版本不适用";
                case "removed": return "已卸载";
                case "registered": return "已登记";
                default: return record.Status ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------ chrome

        /// <summary>
        /// Module repository card: lists the JSON index, installs one entry through the normal
        /// installer (review/trust/permissions still apply on the manager side).
        /// </summary>
        private static FrameworkElement RepositoryCard(ISwapApi api, AddonManager addons)
        {
            var i18n = SwapI18n.Current;
            var snapshot = AddonRepository.Current;
            if (snapshot.LoadedAt == null && !snapshot.Loading)
            {
                AddonRepository.RefreshAsync(_ =>
                    PclUi.OnUiThread(() => PageRegistry.InvalidatePage("swap-addons")));
            }

            var rows = new List<FrameworkElement>();
            if (snapshot.Loading)
                rows.Add(SwapUi.Note(i18n.T("正在读取模块仓库…", "Loading module repository...")));
            else if (!string.IsNullOrEmpty(snapshot.Error))
                rows.Add(SwapUi.Note(snapshot.Error));

            if (snapshot.Modules.Count == 0 && !snapshot.Loading &&
                string.IsNullOrEmpty(snapshot.Error))
                rows.Add(SwapUi.Note(i18n.T("仓库目前没有模块。", "The repository has no modules yet.")));

            foreach (var module in snapshot.Modules)
            {
                var installed = addons?.Find(module.Id);
                var updateAvailable = installed != null &&
                                      AddonRepository.CompareVersion(module.Version, installed.Version) > 0;
                var title = module.Name + "  " + module.Version +
                            (installed != null
                                ? updateAvailable
                                    ? "  ·  " + i18n.T("可更新（已装 " + installed.Version + "）",
                                        "update available (installed " + installed.Version + ")")
                                    : "  ·  " + i18n.T("已安装", "installed")
                                : string.Empty);
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
                row.Children.Add(SwapUi.Text(title, 13, false, false));
                var meta = module.Author ?? string.Empty;
                if (module.Size > 0) meta += (meta.Length > 0 ? " · " : string.Empty) + (module.Size / 1024) + " KB";
                if (meta.Length > 0) row.Children.Add(SwapUi.Note(meta));
                if (!string.IsNullOrEmpty(module.Description))
                    row.Children.Add(SwapUi.Note(module.Description));

                var buttons = new List<FrameworkElement>
                {
                    SwapUi.Button(installed == null
                            ? i18n.T("一键安装", "Install")
                            : updateAvailable
                                ? i18n.T("更新", "Update")
                                : i18n.T("重新安装", "Reinstall"),
                        () => InstallFromRepository(api, addons, module))
                };
                if (!string.IsNullOrEmpty(module.Project))
                    buttons.Add(SwapUi.Button(i18n.T("项目主页", "Project page"),
                        () => PclUi.OpenWebsite(module.Project)));
                row.Children.Add(SwapUi.Buttons(buttons.ToArray()));
                rows.Add(row);
            }

            rows.Add(SwapUi.Buttons(
                SwapUi.Button(i18n.T("刷新仓库", "Refresh repository"), () =>
                {
                    AddonRepository.RefreshAsync(true, _ =>
                        PclUi.OnUiThread(() => PageRegistry.InvalidatePage("swap-addons")));
                    PageRegistry.InvalidatePage("swap-addons");
                }),
                SwapUi.Button(i18n.T("打开索引", "Open index"),
                    () => PclUi.OpenWebsite(AddonRepository.DefaultIndexUrl))));

            rows.Add(SwapUi.Separator());
            rows.Add(SwapUi.Note((i18n.IsEnglish ? "Source: " : "仓库源：") +
                                 AddonRepository.CurrentIndexUrl));
            var pendingSource = AddonRepository.CurrentIndexUrl;
            rows.Add(SwapUi.TextInput(i18n.T("自定义源", "Custom source"),
                AddonRepository.CurrentIndexUrl, value => pendingSource = value));
            rows.Add(SwapUi.Buttons(
                SwapUi.Button(i18n.T("保存源并刷新", "Save source & refresh"), () =>
                {
                    AddonRepository.SetSource(pendingSource);
                    AddonRepository.RefreshAsync(true, _ =>
                        PclUi.OnUiThread(() => PageRegistry.InvalidatePage("swap-addons")));
                    PageRegistry.InvalidatePage("swap-addons");
                })));

            return SwapUi.Card(i18n.T("模块仓库", "Module repository"), rows.ToArray());
        }

        private static void InstallFromRepository(ISwapApi api, AddonManager addons,
            AddonRepository.RepoModule module)
        {
            var i18n = SwapI18n.Current;
            api?.Ui?.Hint(i18n.T("正在下载模块…", "Downloading module..."));
            var worker = new Thread(() =>
            {
                var path = AddonRepository.Download(module, out var error);
                if (path == null)
                {
                    PclUi.OnUiThread(() =>
                        api?.Ui?.Hint((i18n.IsEnglish ? "Install failed: " : "安装失败：") + error));
                    return;
                }

                AddonManager.PackageReview review;
                try
                {
                    review = SwapHost.Addons.Review(path);
                }
                catch (Exception ex)
                {
                    PclUi.OnUiThread(() =>
                        api?.Ui?.Hint((i18n.IsEnglish ? "Package rejected: " : "安装包被拒绝：") + ex.Message));
                    return;
                }

                var manifest = review.Manifest;
                PclUi.OnUiThread(() =>
                {
                    var text = (i18n.IsEnglish ? "Install " : "安装 ") +
                               manifest.Name + " " + manifest.Version + "\n" +
                               (i18n.IsEnglish ? "Permissions: " : "权限：") +
                               (manifest.Permissions == null || manifest.Permissions.Count == 0
                                   ? i18n.T("（未声明 —— 兼容模式）", "(none - compatibility mode)")
                                   : string.Join(", ", manifest.Permissions)) + "\n" +
                               (i18n.IsEnglish ? "Trust: " : "信任：") + review.Trust.Summary;
                    var answer = api.Ui.MsgBox(text,
                        i18n.T("安装模块", "Install module"),
                        i18n.T("安装", "Install"), i18n.T("取消", "Cancel"));
                    if (answer != 1)
                    {
                        try { System.IO.File.Delete(path); } catch { }
                        api.Ui.Hint(i18n.T("已取消安装。", "Install cancelled."));
                        return;
                    }

                    try
                    {
                        var record = SwapHost.Addons.Install(path);
                        api.Ui.Hint((i18n.IsEnglish ? "Installed: " : "已安装：") +
                                    record.Name + " " + record.Version);
                        // The sidebar Info carries the module count; rebuild it so the label
                        // matches the new list without waiting for a restart.
                        PageRegistry.RefreshSidebar();
                        MorePageRegistry.RefreshSidebar();
                        PageRegistry.InvalidatePage("swap-addons");
                    }
                    catch (Exception ex)
                    {
                        api.Ui.Hint((i18n.IsEnglish ? "Install failed: " : "安装失败：") + ex.Message);
                    }
                });
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.RepoInstall"
            };
            worker.Start();
        }

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
