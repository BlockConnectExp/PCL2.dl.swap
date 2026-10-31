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
            var i18n = SwapI18n.Current;
            var page = SwapUi.Page();
            var records = addons?.Records;
            if (addons?.SafeMode == true)
                page.Children.Add(SafeModeCard(api, addons));

            var list = new List<FrameworkElement>();
            if (records == null || records.Count == 0)
            {
                list.Add(SwapUi.Note(i18n.T(
                    "尚未发现任何 Addon。把 .swapaddon 包放进 Addon 目录后重启 PCL 即可加载。",
                    "No addons yet. Put a .swapaddon package in the addon directory and restart PCL to load it.")));
            }
            else
            {
                for (var index = 0; index < records.Count; index++)
                {
                    if (index > 0) list.Add(Separator());
                    list.Add(Row(records[index], api, addons));
                }
            }

            page.Children.Add(SwapUi.Card(i18n.T("已安装的 Addon", "Installed addons"), list.ToArray()));

            // Manager-style detail view: the row's 详情 button selects a module and the page is
            // rebuilt around it (metadata, audit trail, hook chains, log tail, actions).
            var selected = addons?.Selected;
            if (selected != null) page.Children.Add(Detail(api, addons, selected));

            page.Children.Add(SwapUi.Card(i18n.T("模块管理", "Module manager"),
                SwapUi.Note(i18n.T(
                    "安装会把包归档到 Addon 目录并立即启动，不需要重启 PCL；" +
                    "卸载会停掉模块、撤下它的设置页并删除模块目录（入口 DLL 被占用时留到下次启动清理）。",
                    "Installing archives the package into the addon directory and starts it without a PCL restart. " +
                    "Uninstalling stops the module, removes its pages and deletes its directory; a locked entry DLL is cleaned up on the next start.")),
                SwapUi.Buttons(
                    SwapUi.Button(i18n.T("安装模块…", "Install module..."),
                        () => InstallFromDialog(api, addons)),
                    SwapUi.Button(i18n.T("打开 Addon 目录", "Open addon directory"),
                        () => OpenFolder(api?.Host.AddonDirectory)),
                    SwapUi.Button(i18n.T("刷新", "Refresh"),
                        () => PageRegistry.InvalidatePage("swap-addons")))));

            page.Children.Add(RepositoryCard(api, addons));

            var chains = ScopedHook.Snapshot();
            if (chains.Count > 0)
            {
                var lines = chains.Select(ChainRow).ToList();
                lines.Add(SwapUi.Note(i18n.T(
                    "同一个宿主方法只有一条链：第一个钩子负责装 gate，后续模块加入这条链；" +
                    "链上 handler 全部失效时调用直通原方法。",
                    "Each host method has one chain: the first hook installs the gate, later modules join it, " +
                    "and once every handler is inactive the call goes straight to the original method.")));
                page.Children.Add(SwapUi.Card(i18n.T("钩子链（模块作用域）", "Hook chains (module scope)"),
                    lines.ToArray()));
            }

            page.Children.Add(SwapUi.Card(i18n.T("说明", "Notes"),
                SwapUi.Note(i18n.T(
                    "开关立即生效并写入 modules.json（内置功能的钩子按开关放行/拦截）；" +
                    "模块状态、配置与已安装的 Addon 都放在数据目录里，可在 Swap Settings 打开。",
                    "Toggles apply immediately and persist to modules.json. Module state, configuration and installed addons " +
                    "live under the data directory, reachable from Swap Settings.")),
                SwapUi.Note(i18n.T(
                    "注意：结构类修复（原版游戏页 / 下载侧栏）是启动时对宿主对象的一次性修复，" +
                    "关闭模块后要重启 PCL 才会完全回退；OTA、弹窗类拦截则立即停用。",
                    "Structural repairs such as the vanilla download page and sidebar run once at startup. Fully reverting " +
                    "them requires a PCL restart; OTA and dialog interception stop immediately.")),
                SwapUi.Buttons(
                    SwapUi.Button(i18n.T("打开 Addon 目录", "Open addon directory"),
                        () => OpenFolder(api?.Host.AddonDirectory)),
                    SwapUi.Button(i18n.T("刷新", "Refresh"),
                        () => PageRegistry.InvalidatePage("swap-addons")))));

            return Wrap(page);
        }

        // ------------------------------------------------------------------ rows

        private static FrameworkElement SafeModeCard(ISwapApi api, AddonManager addons)
        {
            var i18n = SwapI18n.Current;
            var recovery = addons?.Recovery;
            if (recovery == null) return SwapUi.Note(i18n.T("（恢复信息不可用）", "(recovery unavailable)"));

            var suspectId = recovery.SuspectModule;
            var suspect = addons?.Find(suspectId);
            var suspectDisplay = suspect == null
                ? suspectId
                : string.IsNullOrEmpty(suspect.Name) ? suspect.Id : suspect.Name;
            var suspectName = suspect == null
                ? (string.IsNullOrEmpty(suspectId) ? i18n.T("（无）", "(none)") : suspectId)
                : suspectDisplay + " (" + suspect.Id + ")";

            var rows = new List<FrameworkElement>
            {
                SwapUi.Banner(
                    i18n.T("安全模式正在保护当前启动", "Safe mode is protecting this launch"),
                    i18n.T(
                        "本次会话不会加载任何 Addon，但 Swap 页面和恢复入口仍然可用。",
                        "No addon is loaded in this session, but Swap pages and recovery controls remain available."),
                    SwapHintType.Warning),
                SwapUi.InfoRow(i18n.T("触发原因", "Reason"),
                    ReasonText(recovery, i18n)),
                SwapUi.InfoRow(i18n.T("未稳定启动", "Unclean starts"),
                    recovery.SafeModeUncleanStarts + " " + i18n.T("次", "times")),
                SwapUi.InfoRow(i18n.T("疑因模块", "Suspect module"), suspectName),
                SwapUi.InfoRow(i18n.T("疑因阶段", "Suspect phase"),
                    PhaseText(string.IsNullOrEmpty(recovery.SuspectPhase)
                        ? recovery.LifecyclePhase
                        : recovery.SuspectPhase)),
                SwapUi.InfoRow(i18n.T("最后成功启用", "Last enabled module"),
                    string.IsNullOrEmpty(recovery.LastCompletedModule)
                        ? i18n.T("（无）", "(none)")
                        : recovery.LastCompletedModule),
                SwapUi.InfoRow(i18n.T("当前状态", "Current state"),
                    i18n.T("安全模式运行中", "Safe mode active")),
                SwapUi.InfoRow(i18n.T("恢复目录", "Recovery directory"),
                    api?.Host?.RecoveryDirectory ?? string.Empty)
            };

            if (recovery.SafeModeExitPending)
            {
                rows.Add(SwapUi.Note(i18n.T(
                    "安全模式旗标已清除。重启 PCL 后 Addon 会按原配置重新加载。",
                    "The safe-mode flag is cleared. Restart PCL to load addons again with their saved enabled state.")));
            }

            var actions = new List<FrameworkElement>();
            if (suspect != null)
            {
                actions.Add(SwapUi.Button(
                    i18n.T("禁用疑因模块并退出安全模式", "Disable suspect and leave safe mode"),
                    () =>
                    {
                        var answer = api?.Ui.MsgBox(
                            i18n.T(
                                "将把“" + suspectDisplay + "”写入 modules.json 并禁用，然后清除安全模式旗标。" +
                                "当前会话仍保持隔离；重启 PCL 后加载其余模块。是否继续？",
                                "\"" + suspectDisplay + "\" will be disabled in modules.json and the safe-mode flag will be cleared. " +
                                "This session remains isolated; restart PCL to load the remaining addons. Continue?"),
                            i18n.T("PCL2 Swap · 安全模式", "PCL2 Swap - Safe mode"),
                            i18n.T("禁用并退出", "Disable and leave"), i18n.T("取消", "Cancel")) ?? 1;
                        if (answer != 1) return;
                        if (addons.DisableSuspectAndLeaveSafeMode())
                        {
                            api?.Ui.Hint(i18n.T(
                                "疑因模块已禁用，安全模式旗标已清除；重启 PCL 后加载其余模块。",
                                "The suspect module is disabled and safe mode is cleared. Restart PCL to load the remaining addons."),
                                SwapHintType.Success);
                            PageRegistry.InvalidatePage("swap-addons");
                        }
                    }, "Red"));
            }

            actions.Add(
                SwapUi.Button(i18n.T("退出安全模式（重启后生效）", "Leave safe mode (restart to apply)"),
                    () =>
                    {
                        var answer = api?.Ui.MsgBox(
                            i18n.T("这会清除安全模式旗标，但当前会话仍不会加载 Addon。是否继续？",
                                "This clears the safe-mode flag, but the current session still will not load addons. Continue?"),
                            i18n.T("PCL2 Swap · 安全模式", "PCL2 Swap - Safe mode"),
                            i18n.T("退出安全模式", "Leave safe mode"), i18n.T("取消", "Cancel")) ?? 1;
                        if (answer != 1) return;
                        if (recovery.ClearSafeMode())
                        {
                            api?.Ui.Hint(i18n.T("安全模式已清除；重启 PCL 后加载模块。",
                                "Safe mode cleared; restart PCL to load addons."));
                            PageRegistry.InvalidatePage("swap-addons");
                        }
                    }));
            actions.Add(
                SwapUi.Button(i18n.T("复制恢复诊断", "Copy recovery diagnostics"),
                    () =>
                    {
                        var copied = api?.Pcl?.Clipboard?.SetText(recovery.Diagnostics()) == true;
                        api?.Ui.Hint(copied
                            ? i18n.T("恢复诊断已复制。", "Recovery diagnostics copied.")
                            : i18n.T("复制恢复诊断失败。", "Failed to copy recovery diagnostics."),
                            copied ? SwapHintType.Success : SwapHintType.Error);
                    }));
            actions.Add(
                SwapUi.Button(i18n.T("打开恢复目录", "Open recovery directory"),
                    () => OpenFolder(api?.Host?.RecoveryDirectory)));
            var actionWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var action in actions) actionWrap.Children.Add(action);
            rows.Add(actionWrap);

            return SwapUi.Card(i18n.T("恢复模式", "Recovery mode"), rows.ToArray());
        }

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
            var i18n = SwapI18n.Current;
            var grid = new Grid { Margin = new Thickness(0, 9, 0, 9), MinHeight = 46 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var headline = new StackPanel { Orientation = Orientation.Horizontal };
            headline.Children.Add(SwapUi.Text(record.Name ?? record.Id, 13, false, false, FontWeights.SemiBold));
            var meta = (record.Version ?? string.Empty) +
                       (record.BuiltIn ? "  ·  " + i18n.T("内置", "Built-in") : string.Empty);
            var hookCount = ScopedHook.CountFor(record.Id);
            if (hookCount > 0)
                meta += "  ·  " + i18n.T(
                    $"钩子 {hookCount}（命中 {ScopedHook.HitsFor(record.Id)}）",
                    $"hooks {hookCount} (hits {ScopedHook.HitsFor(record.Id)})");
            if (meta.Trim().Length > 0)
            {
                var version = SwapUi.Text(meta, 11.5, true, false);
                version.Margin = new Thickness(8, 3, 0, 0);
                headline.Children.Add(version);
            }
            left.Children.Add(headline);

            if (!string.IsNullOrEmpty(record.Description))
            {
                var description = SwapUi.Text(i18n.T(record.Description, GetBuiltInDescription(record, i18n)),
                    11.5, true);
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
                var failure = SwapUi.Text(i18n.T("错误：", "Error: ") + record.Failure.Message, 11.5, false);
                failure.Foreground = Brushes.IndianRed;
                failure.Margin = new Thickness(0, 3, 12, 0);
                left.Children.Add(failure);
            }

            var hookErrors = ScopedHook.ErrorsFor(record.Id);
            if (hookErrors > 0)
            {
                // Isolation report: a failing handler is deactivated on its first throw, so this is a
                // record of "your module broke N times", not a still-broken state.
                var errors = SwapUi.Text(i18n.T(
                    $"钩子抛错 {hookErrors} 次（失败的钩子已自动停用，详见该模块日志）",
                    $"hooks threw {hookErrors} times (failed hooks were disabled; see the module log)"),
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

                var toggle = SwapUi.Button(record.Enabled
                    ? i18n.T("禁用", "Disable")
                    : i18n.T("启用", "Enable"), () =>
                {
                    if (!addons.SetEnabled(id, !record.Enabled)) return;
                    PageRegistry.InvalidatePage("swap-addons");
                });
                var detail = SwapUi.Button(i18n.T("详情", "Details"), () =>
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

                var logButton = SwapUi.Button(i18n.T("日志", "Log"),
                    () => OpenModuleLog(api, addons, record));
                PclUi.SetMember(logButton, "Height", 35.0);
                PclUi.SetMember(logButton, "MinWidth", 64.0);
                PclUi.SetMember(logButton, "Margin", new Thickness(0, 0, 8, 0));
                actions.Children.Add(logButton);

                if (!record.BuiltIn)
                {
                    var uninstall = SwapUi.Button(i18n.T("卸载", "Uninstall"),
                        () => UninstallModule(api, addons, record), "Red");
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

        private static string GetBuiltInDescription(AddonManager.AddonRecord record,
            SwapI18n i18n)
        {
            switch (record?.Id)
            {
                case "swap.download-mc":
                    return i18n.T("恢复原版游戏下载页（LoaderInit 重驱动 + 2.99.0.1 侧栏修复）。",
                        "Restores the vanilla download page (LoaderInit rerun plus the 2.99.0.1 sidebar repair).");
                case "swap.ota-protector":
                    return i18n.T("静默 OTA 显式确认；启动器文件下载完成后查毒（游戏内容不查）。",
                        "Makes silent OTA updates explicit and scans launcher files after download (not game content).");
                case "swap.cancel-button":
                    return i18n.T("给本来没有取消按钮的弹窗追加一个取消（MyMsgBox 家族）。",
                        "Adds a Cancel action to dialogs that otherwise have none (MyMsgBox family).");
                default:
                    return i18n.T(record?.Description ?? string.Empty);
            }
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
            var i18n = SwapI18n.Current;
            var manifest = record.Manifest;
            var content = new List<FrameworkElement>
            {
                SwapUi.InfoRow(i18n.T("状态", "Status"), StatusText(record)),
                SwapUi.InfoRow(i18n.T("id / 版本", "id / version"), record.Id + "  " + record.Version),
                SwapUi.InfoRow(i18n.T("安装时间", "Installed at"), record.InstalledAtUtc.HasValue
                    ? record.InstalledAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : i18n.T("（内置模块）", "(built-in)")),
                SwapUi.InfoRow(i18n.T("来源包", "Source package"),
                    string.IsNullOrEmpty(record.SourcePath) ? i18n.T("（内置模块）", "(built-in)") : record.SourcePath),
                SwapUi.InfoRow(i18n.T("包 SHA-256", "Package SHA-256"),
                    string.IsNullOrEmpty(record.Sha256) ? i18n.T("（内置模块）", "(built-in)") : record.Sha256),
                SwapUi.InfoRow(i18n.T("签名状态", "Signature"),
                    string.IsNullOrEmpty(record.Signature) ? i18n.T("（未记录）", "(not recorded)") : record.Signature),
                SwapUi.InfoRow(i18n.T("模块目录", "Module directory"),
                    string.IsNullOrEmpty(record.Directory) ? i18n.T("（内置模块）", "(built-in)") : record.Directory),
                SwapUi.InfoRow(i18n.T("配置文件", "Configuration"), addons.GetModuleConfigPath(record.Id))
            };

            if (manifest != null)
            {
                var separator = i18n.IsEnglish ? ", " : "、";
                content.Add(SwapUi.InfoRow(i18n.T("API 范围", "API range"), manifest.ApiVersion));
                content.Add(SwapUi.InfoRow(i18n.T("入口", "Entry point"), manifest.Entry));
                if (!string.IsNullOrEmpty(manifest.Author))
                    content.Add(SwapUi.InfoRow(i18n.T("作者", "Author"), manifest.Author));
                content.Add(SwapUi.InfoRow(i18n.T("形状作用域", "Shape scope"), manifest.Shapes.Count == 0
                    ? i18n.T("（未声明 —— 全部形状）", "(not declared - all shapes)")
                    : string.Join(separator, manifest.Shapes)));
                content.Add(SwapUi.InfoRow(i18n.T("宿主版本", "Host versions"), manifest.HostVersions.Count == 0
                    ? i18n.T("（未声明 —— 任意版本）", "(not declared - any version)")
                    : string.Join(separator, manifest.HostVersions)));
                content.Add(SwapUi.InfoRow(i18n.T("功能作用域", "Feature scopes"),
                    manifest.Scopes.Count == 0
                        ? i18n.T("（未声明）", "(not declared)")
                        : manifest.Scopes.Count + i18n.T(" 项，可在下方启停", " declared; edit below")));
                content.Add(SwapUi.InfoRow(i18n.T("配置 schema", "Config schema"),
                    record.ConfigStore == null
                        ? i18n.T("（模块尚未加载）", "(module not loaded)")
                        : "v" + record.ConfigStore.SchemaVersion + i18n.T(
                            "，" + record.ConfigStore.Keys.Count + " 项",
                            ", " + record.ConfigStore.Keys.Count + " keys")));
                content.Add(SwapUi.InfoRow(i18n.T("发布服务", "Published services"),
                    AddonServiceRegistry.Snapshot(record.Id).Count.ToString()));
                content.Add(SwapUi.InfoRow(i18n.T("权限声明", "Permissions"), manifest.Permissions.Count == 0
                    ? i18n.T("（未声明 —— 兼容模式，全部访问）",
                        "(not declared - compatibility mode, all access)")
                    : string.Join(separator, manifest.Permissions) + i18n.T("（严格门控）", " (strictly gated)")));
            }

            var scopeList = addons.ScopeSnapshot(record);
            if (scopeList.Count > 0)
            {
                var enabledScopeCount = scopeList.Count(scope => scope.Enabled);
                content.Add(SwapUi.Expander(
                    i18n.T("功能作用域", "Feature scopes"), true,
                    () =>
                    {
                        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
                        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                        var countBadge = SwapUi.Badge(
                            i18n.T($"{enabledScopeCount}/{scopeList.Count} 已启用",
                                $"{enabledScopeCount}/{scopeList.Count} enabled"),
                            enabledScopeCount == 0 ? SwapHintType.Warning : SwapHintType.Success);
                        badges.Children.Add(countBadge);
                        var countText = (countBadge as Border)?.Child as TextBlock;
                        panel.Children.Add(badges);
                        panel.Children.Add(SwapUi.Note(i18n.T(
                            "这些是模块声明的功能作用域。关闭某项会立即让绑定到它的 hook 直通原方法；模块仍保持启用。",
                            "These are feature scopes declared by the module. Disabling one immediately makes its bound hooks pass through while the module remains enabled.")));

                        foreach (var scope in scopeList)
                        {
                            var currentScope = scope;
                            var scopeName = i18n.IsEnglish
                                ? FirstNonEmpty(currentScope.NameEnglish, currentScope.Name, currentScope.Id)
                                : FirstNonEmpty(currentScope.Name, currentScope.Id);
                            var scopeDescription = i18n.IsEnglish
                                ? FirstNonEmpty(currentScope.DescriptionEnglish, currentScope.Description)
                                : currentScope.Description;
                            panel.Children.Add(SwapUi.CheckBox(
                                scopeName + "  [" + currentScope.Id + "]",
                                currentScope.Enabled,
                                value =>
                                {
                                    addons.SetScopeEnabled(record.Id, currentScope.Id, value);
                                    var currentCount = addons.ScopeSnapshot(record).Count(item => item.Enabled);
                                    if (countText != null)
                                        countText.Text = i18n.T(
                                            $"{currentCount}/{scopeList.Count} 已启用",
                                            $"{currentCount}/{scopeList.Count} enabled");
                                    SwapUi.SetBadgeState(countBadge,
                                        currentCount == 0 ? SwapHintType.Warning : SwapHintType.Success);
                                }));
                            if (!string.IsNullOrWhiteSpace(scopeDescription))
                                panel.Children.Add(SwapUi.Note(scopeDescription));
                        }
                        return panel;
                    }));
            }

            var serviceList = AddonServiceRegistry.Snapshot(record.Id);
            if (serviceList.Count > 0)
            {
                content.Add(SwapUi.Expander(
                    i18n.T("模块服务", "Module services"), true,
                    () =>
                    {
                        var rows = serviceList.Select(service => (IReadOnlyList<string>)new[]
                        {
                            service.ServiceId,
                            service.Version,
                            service.ContractType,
                            service.Description
                        }).ToList();
                        return SwapUi.Table(
                            new[]
                            {
                                i18n.T("服务", "Service"),
                                i18n.T("版本", "Version"),
                                i18n.T("契约类型", "Contract"),
                                i18n.T("说明", "Description")
                            },
                            rows, new[] { 1.2, 0.6, 1.4, 1.6 }, 220);
                    }));
            }

            var requestedPermissions = addons.RequestedPermissions(record);
            if (requestedPermissions.Count > 0)
            {
                var grantedCount = addons.EffectiveGrantedPermissions(record).Count;
                content.Add(SwapUi.Expander(
                    i18n.T("权限授予", "Permission grants"), true,
                    () =>
                    {
                        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
                        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                        var countBadge = SwapUi.Badge(
                            i18n.T($"已授予 {grantedCount}/{requestedPermissions.Count}",
                                $"{grantedCount}/{requestedPermissions.Count} granted"),
                            grantedCount >= requestedPermissions.Count
                                ? SwapHintType.Success
                                : grantedCount == 0 ? SwapHintType.Error : SwapHintType.Warning);
                        badges.Children.Add(countBadge);
                        var countText = (countBadge as Border)?.Child as TextBlock;
                        badges.Children.Add(SwapUi.Badge(i18n.T("严格门控", "Strictly gated"),
                            SwapHintType.Info));
                        panel.Children.Add(badges);
                        panel.Children.Add(SwapUi.Note(i18n.T(
                            "撤销会立即生效；模块已安装的钩子会按新授权开关放行或直通原方法。",
                            "Revocation applies immediately; installed hooks either run or pass through according to the new grant.")));
                        foreach (var permission in requestedPermissions)
                        {
                            var current = permission;
                            panel.Children.Add(SwapUi.CheckBox(
                                PermissionLabel(current, i18n),
                                addons.IsPermissionGranted(record, current),
                                value =>
                                {
                                    addons.SetPermission(record.Id, current, value);
                                    var currentCount = addons.EffectiveGrantedPermissions(record).Count;
                                    if (countText != null)
                                        countText.Text = i18n.T(
                                            $"已授予 {currentCount}/{requestedPermissions.Count}",
                                            $"{currentCount}/{requestedPermissions.Count} granted");
                                    SwapUi.SetBadgeState(countBadge,
                                        currentCount >= requestedPermissions.Count
                                            ? SwapHintType.Success
                                            : currentCount == 0 ? SwapHintType.Error : SwapHintType.Warning);
                                }));
                        }
                        return panel;
                    }));
            }

            var chains = ScopedHook.Snapshot()
                .Where(line => line.IndexOf(record.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            var hooks = ScopedHook.CountFor(record.Id);
            var hits = ScopedHook.HitsFor(record.Id);
            var errors = ScopedHook.ErrorsFor(record.Id);
            content.Add(SwapUi.InfoRow(i18n.T("钩子", "Hooks"), hooks == 0
                ? i18n.T("（无）", "(none)")
                : i18n.T($"{hooks} 个（命中 {hits}，抛错 {errors}）",
                    $"{hooks} (hits {hits}, errors {errors})")));
            if (chains.Count > 0) content.Add(SwapUi.Note(string.Join("；", chains)));

            var tail = addons.LogTail(record.Id, 12);
            content.Add(SwapUi.Note(i18n.T("模块日志尾部：", "Module log tail:")));
            content.Add(SwapUi.Text(string.IsNullOrEmpty(tail)
                ? i18n.T("（这个模块还没有日志）", "(this module has not logged yet)")
                : tail, 11, true));

            var actions = new List<FrameworkElement>
            {
                ActionButton(record.Enabled ? i18n.T("禁用", "Disable") : i18n.T("启用", "Enable"), () =>
                {
                    addons.SetEnabled(record.Id, !record.Enabled);
                    PageRegistry.InvalidatePage("swap-addons");
                }),
                ActionButton(i18n.T("重载", "Reload"), () =>
                {
                    addons.Reload(record.Id);
                    PageRegistry.InvalidatePage("swap-addons");
                }),
                ActionButton(i18n.T("打开日志", "Open log"), () => OpenModuleLog(api, addons, record)),
                ActionButton(i18n.T("打开配置目录", "Open config directory"), () =>
                    OpenFolder(System.IO.Path.GetDirectoryName(addons.GetModuleConfigPath(record.Id)))),
                ActionButton(i18n.T("收起", "Collapse"), () =>
                {
                    addons.SelectedAddonId = null;
                    PageRegistry.InvalidatePage("swap-addons");
                })
            };
            if (!record.BuiltIn)
                actions.Add(ActionButton(i18n.T("卸载", "Uninstall"),
                    () => UninstallModule(api, addons, record), "Red"));
            var actionWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var action in actions) actionWrap.Children.Add(action);
            content.Add(actionWrap);

            return SwapUi.Card(i18n.T("模块详情：", "Module details: ") + (record.Name ?? record.Id),
                content.ToArray());
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
            var i18n = SwapI18n.Current;
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
                        existing == null
                            ? i18n.T("PCL2 Swap · 安装模块", "PCL2 Swap - Install module")
                            : i18n.T("PCL2 Swap · 更新模块", "PCL2 Swap - Update module"),
                        existing == null ? i18n.T("安装", "Install") : i18n.T("更新", "Update"),
                        i18n.T("取消", "Cancel"));
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
                    ? i18n.T($"已安装并启用：{record.Name} {record.Version}",
                        $"Installed and enabled: {record.Name} {record.Version}")
                    : i18n.T($"已安装：{record.Name} {record.Version}（{StatusText(record)}）",
                        $"Installed: {record.Name} {record.Version} ({StatusText(record)})"),
                    SwapHintType.Success);
            }
            catch (Exception ex)
            {
                SwapLog.Error("安装模块失败：" + ex.Message);
                api?.Ui.MsgBox(i18n.T("安装失败：", "Install failed: ") + Environment.NewLine +
                               ex.Message + Environment.NewLine + Environment.NewLine +
                               i18n.T(
                                   "模块包需要包含 manifest.json（id / version / entry / apiVersion）。",
                                   "An addon package must contain manifest.json (id / version / entry / apiVersion)."),
                    i18n.T("PCL2 Swap · 安装模块", "PCL2 Swap - Install module"),
                    i18n.T("确定", "OK"));
            }
        }

        private static void UninstallModule(ISwapApi api, AddonManager addons, AddonManager.AddonRecord record)
        {
            if (addons == null || record == null) return;
            var i18n = SwapI18n.Current;
            try
            {
                var answer = 1;
                if (api != null)
                {
                    answer = api.Ui.MsgBox(
                        i18n.T(
                            $"确定要卸载“{record.Name} {record.Version}”吗？",
                            $"Uninstall \"{record.Name} {record.Version}\"?") +
                        Environment.NewLine + Environment.NewLine +
                        i18n.T(
                            "模块立即停止（钩子放行原方法）、设置页从侧栏移除，模块目录与包文件被删除。",
                            "The module stops immediately, its hook gates pass through to the original methods, " +
                            "its pages leave the sidebar, and its directory and package are deleted.") +
                        Environment.NewLine +
                        i18n.T("被 PCL 占用的 DLL 会留到下次启动时清理。",
                            "A DLL locked by PCL is removed on the next start."),
                        i18n.T("PCL2 Swap · 卸载模块", "PCL2 Swap - Uninstall module"),
                        i18n.T("卸载", "Uninstall"), i18n.T("取消", "Cancel"));
                }
                if (answer != 1)
                {
                    SwapLog.Info("卸载模块已取消：" + record.Id);
                    return;
                }

                if (!addons.Uninstall(record.Id))
                {
                    api?.Ui.MsgBox(
                        i18n.T("卸载失败：内置模块不能卸载，请在列表里禁用。",
                            "Uninstall failed: built-in modules cannot be removed. Disable it in the list instead."),
                        i18n.T("PCL2 Swap · 卸载模块", "PCL2 Swap - Uninstall module"),
                        i18n.T("确定", "OK"));
                    return;
                }

                PageRegistry.RefreshSidebar();
                PageRegistry.InvalidatePage("swap-addons");
                api?.Ui.Hint(i18n.T($"已卸载：{record.Name} {record.Version}",
                    $"Uninstalled: {record.Name} {record.Version}"), SwapHintType.Success);
            }
            catch (Exception ex)
            {
                SwapLog.Error("卸载模块失败：" + ex.Message);
                api?.Ui.MsgBox(i18n.T("卸载失败：", "Uninstall failed: ") + Environment.NewLine + ex.Message,
                    i18n.T("PCL2 Swap · 卸载模块", "PCL2 Swap - Uninstall module"),
                    i18n.T("确定", "OK"));
            }
        }

        private static string PickPackage()
        {
            var i18n = SwapI18n.Current;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = i18n.T("选择 Swap 模块包", "Select a Swap addon package"),
                Filter = i18n.T(
                    "Swap 模块包 (*.swapaddon)|*.swapaddon|ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    "Swap addon packages (*.swapaddon)|*.swapaddon|ZIP archives (*.zip)|*.zip|All files (*.*)|*.*"),
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>Opens a module's own log; shows the folder while the module has not logged yet.</summary>
        private static void OpenModuleLog(ISwapApi api, AddonManager addons, AddonManager.AddonRecord record)
        {
            var i18n = SwapI18n.Current;
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
                api?.Ui.Hint(i18n.T("这个模块还没有日志；已经打开日志目录。",
                    "This module has not logged yet; its log directory was opened."));
            }
            catch (Exception ex)
            {
                SwapLog.Warn("打开模块日志失败：" + ex.Message);
            }
        }

        /// <summary>Manifest scope line: author, API range, shapes and permission count.</summary>
        private static string FirstNonEmpty(params string[] values) =>
            values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        private static string ScopeText(AddonManager.AddonRecord record)
        {
            var manifest = record.Manifest;
            if (manifest == null) return string.Empty;
            var i18n = SwapI18n.Current;
            var separator = i18n.IsEnglish ? ", " : "、";

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(manifest.Author))
                parts.Add(i18n.T("作者 ", "by ") + manifest.Author);
            if (!string.IsNullOrEmpty(manifest.ApiVersion)) parts.Add("API " + manifest.ApiVersion);
            if (manifest.Shapes != null && manifest.Shapes.Count > 0)
                parts.Add(i18n.T("形状 ", "shapes ") + string.Join("/", manifest.Shapes));
            if (manifest.HostVersions != null && manifest.HostVersions.Count > 0)
                parts.Add(i18n.T("宿主 ", "host ") + string.Join(separator, manifest.HostVersions));
            if (manifest.Scopes != null && manifest.Scopes.Count > 0)
            {
                var scopes = SwapHost.Addons?.ScopeSnapshot(record) ?? manifest.Scopes
                    .Select(scope => new PCL2Swap.Api.SwapScopeInfo
                    {
                        Id = scope.Id,
                        Name = scope.Name,
                        NameEnglish = scope.NameEnglish,
                        Description = scope.Description,
                        DescriptionEnglish = scope.DescriptionEnglish,
                        DefaultEnabled = scope.DefaultEnabled,
                        Enabled = scope.DefaultEnabled
                    }).ToList();
                parts.Add(i18n.T("功能 ", "features ") +
                          scopes.Count(scope => scope.Enabled) + "/" + scopes.Count);
            }
            if (manifest.Permissions != null && manifest.Permissions.Count > 0)
            {
                var shown = manifest.Permissions.Take(3).ToList();
                var text = i18n.T("权限 ", "permissions ") + string.Join(separator, shown);
                if (manifest.Permissions.Count > shown.Count)
                    text += i18n.T(" 等 " + manifest.Permissions.Count + " 项",
                        " and " + manifest.Permissions.Count + " total");
                text += i18n.T("（严格）", " (strict)");
                parts.Add(text);
            }
            var serviceCount = AddonServiceRegistry.Snapshot(record.Id).Count;
            if (serviceCount > 0)
                parts.Add(i18n.T("服务 ", "services ") + serviceCount);
            if (record.ConfigStore != null)
                parts.Add(i18n.T("配置 v", "config v") + record.ConfigStore.SchemaVersion);
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }

        private static string StatusText(AddonManager.AddonRecord record)
        {
            var i18n = SwapI18n.Current;
            switch (record.Status)
            {
                case "enabled": return i18n.T("已启用", "Enabled");
                case "disabled": return i18n.T("已禁用", "Disabled");
                case "failed": return i18n.T("启动失败", "Startup failed");
                case "unpacked": return i18n.T("已解包（待启用）", "Unpacked (waiting to enable)");
                case "skipped-shape": return i18n.T("作用域不适用", "Shape not applicable");
                case "skipped-host": return i18n.T("宿主版本不适用", "Host version not applicable");
                case "skipped-safe-mode": return i18n.T("安全跳过", "Safe skip");
                case "removed": return i18n.T("已卸载", "Uninstalled");
                case "registered": return i18n.T("已登记", "Registered");
                default: return record.Status ?? string.Empty;
            }
        }

        private static string PhaseText(string phase)
        {
            var i18n = SwapI18n.Current;
            switch ((phase ?? string.Empty).Trim())
            {
                case "starting": return i18n.T("正在启动", "Starting");
                case "create": return i18n.T("创建模块实例", "Creating module instance");
                case "onLoad": return i18n.T("OnLoad", "OnLoad");
                case "onEnable": return i18n.T("OnEnable", "OnEnable");
                case "enabled": return i18n.T("生命周期已完成", "Lifecycle completed");
                case "stable": return i18n.T("启动已稳定", "Startup stable");
                case "safe-mode": return i18n.T("安全模式", "Safe mode");
                case "safe-mode-cleared": return i18n.T("安全模式已清除", "Safe mode cleared");
                case "": return i18n.T("（无）", "(none)");
                default: return phase;
            }
        }

        private static string PermissionLabel(string permission, SwapI18n i18n)
        {
            switch ((permission ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "hooks": return i18n.T("挂钩宿主方法", "Hook host methods");
                case "pages": return i18n.T("注册界面页面", "Register UI pages");
                case "files": return i18n.T("打开文件和文件夹", "Open files and folders");
                case "clipboard": return i18n.T("读写剪贴板", "Read/write clipboard");
                case "network": return i18n.T("使用 PCL 网络栈", "Use the PCL network stack");
                case "logs": return i18n.T("读取宿主日志", "Read host logs");
                case "secret": return i18n.T("访问更新与密钥能力", "Access update and secret capabilities");
                case "launch": return i18n.T("启动游戏和账户资料", "Launch games and account profiles");
                case "settings": return i18n.T("读写 PCL 设置", "Read/write PCL settings");
                case "instance": return i18n.T("访问游戏实例", "Access game instances");
                case "messaging": return i18n.T("与模块交换消息", "Exchange module messages");
                case "*": return i18n.T("全部权限", "All permissions");
                default: return permission ?? "(unknown)";
            }
        }

        private static string ReasonText(StartupRecovery recovery, SwapI18n i18n)
        {
            var code = recovery?.SafeModeReasonCode ?? string.Empty;
            if (code == "forced-env" ||
                (recovery?.SafeModeReason ?? string.Empty)
                .StartsWith("PCL2SWAP_SAFE_MODE=1", StringComparison.OrdinalIgnoreCase))
                return i18n.T("由 PCL2SWAP_SAFE_MODE=1 强制启用",
                    "Forced by PCL2SWAP_SAFE_MODE=1");
            if (code == "previous-safe-mode" ||
                (recovery?.SafeModeReason ?? string.Empty).Contains("安全模式已在之前会话启用"))
                return i18n.T("安全模式已在之前会话启用",
                    "Safe mode was already enabled by a previous session");
            if (code == "unstable-start" ||
                (recovery?.SafeModeReason ?? string.Empty).Contains("未进入稳定窗口"))
            {
                var count = Math.Max(2, recovery?.SafeModeUncleanStarts ?? 0);
                return i18n.T($"连续 {count} 次启动未进入稳定窗口",
                    $"{count} consecutive launches did not reach the stable window");
            }

            var original = recovery?.SafeModeReason ?? string.Empty;
            return string.IsNullOrEmpty(original) ? "(unknown)" : original;
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
