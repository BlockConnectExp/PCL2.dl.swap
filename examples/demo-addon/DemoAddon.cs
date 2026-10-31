using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCL2Swap.Api;

namespace DemoAddon
{
    /// <summary>
    /// Minimal but complete Swap addon: one settings page, one More page, persisted data and a
    /// few API 1.2 widgets. Copy this folder, rename the id in build.ps1, and start building.
    /// </summary>
    public sealed class DemoAddon : ISwapAddon
    {
        private SwapContext _context;
        private IDisposable _messageSubscription;
        private int _launches;
        private int _messageHits;
        private double _volume = 50;

        public void OnLoad(SwapContext context)
        {
            _context = context;
            var api = context.Api;
            _launches = int.Parse(api.Data.Get("launches", "0")) + 1;
            api.Data.Set("launches", _launches.ToString());
            api.Data.Save();
            try
            {
                api.Files.WriteText("demo/launch.txt",
                    $"launch={_launches}; api={api.Host.ApiVersion}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                api.LogWarning("Demo file backend failed: " + ex.Message);
            }

            api.Ui.RegisterSettingsPage("settings",
                api.I18n.T("示例设置", "Demo Settings"), () => BuildSettings(api));
            api.Ui.RegisterMorePage("more",
                api.I18n.T("示例页面", "Demo Page"), () => BuildMore(api));

            api.Log($"Demo addon loaded (launch #{_launches}).");
        }

        public void OnEnable()
        {
            var api = _context?.Api;
            if (api == null) return;
            api.Scopes.Changed += OnScopesChanged;
            ApplyScopedFeatures();
            api.Log("Demo addon enabled.");
        }

        public void OnDisable()
        {
            var api = _context?.Api;
            if (api != null) api.Scopes.Changed -= OnScopesChanged;
            _messageSubscription?.Dispose();
            _messageSubscription = null;
            api?.Log("Demo addon disabled.");
        }

        public void OnUnload() => _context?.Api.Log("Demo addon unloaded.");

        private void OnScopesChanged(object sender, EventArgs args)
        {
            var api = _context?.Api;
            if (api == null) return;
            ApplyScopedFeatures();
            api.Log("Demo feature scopes changed: " + string.Join(",", api.Scopes.Enabled));
            api.Ui.Hint(api.I18n.T("示例模块的功能作用域已更新。",
                "Demo feature scopes updated."));
        }

        private void ApplyScopedFeatures()
        {
            var api = _context?.Api;
            if (api == null) return;
            if (!api.Scopes.IsEnabled("demo.core"))
            {
                _messageSubscription?.Dispose();
                _messageSubscription = null;
                return;
            }
            if (_messageSubscription != null) return;
            try
            {
                _messageSubscription = api.Messaging.Subscribe("demo.ready", message =>
                {
                    _messageHits++;
                    api.Log($"Demo message from {message.Source}: {message.Payload}");
                });
                api.Messaging.Publish("demo.ready", "hello from Demo Addon");
            }
            catch (Exception ex)
            {
                api.LogWarning("Demo messaging failed: " + ex.Message);
            }
        }

        private FrameworkElement BuildSettings(ISwapApi api)
        {
            var page = Page();

            page.Children.Add(api.Ui.Card(
                api.I18n.T("宿主信息", "Host information"),
                api.Ui.InfoRow(api.I18n.T("宿主版本", "Host version"), api.Host.SwapVersion),
                api.Ui.InfoRow(api.I18n.T("API 版本", "API version"), api.Host.ApiVersion),
                api.Ui.InfoRow(api.I18n.T("交付形状", "Delivery shape"), api.Host.Shape.ToString()),
                api.Ui.InfoRow(api.I18n.T("加载次数", "Launch count"), _launches.ToString())));

            var scopeRows = new List<IReadOnlyList<string>>();
            foreach (var scope in api.Scopes.Scopes)
            {
                scopeRows.Add(new[]
                {
                    ScopeName(api, scope),
                    scope.Id,
                    scope.Enabled ? api.I18n.T("已启用", "enabled") : api.I18n.T("已停用", "disabled")
                });
            }
            page.Children.Add(api.Ui.Card(
                api.I18n.T("功能作用域", "Feature scopes"),
                api.Ui.Text(api.I18n.T(
                    "作用域由 manifest 声明；用户可在 Swap Addon 的模块详情中即时启停。",
                    "Scopes come from the manifest and can be switched immediately in Swap Addon module details."),
                    12.5, true),
                scopeRows.Count == 0
                    ? api.Ui.Note(api.I18n.T("此模块没有声明功能作用域。", "This module declares no feature scopes."))
                    : api.Ui.Table(
                        new[] { api.I18n.T("名称", "Name"), api.I18n.T("标识", "ID"), api.I18n.T("状态", "Status") },
                        scopeRows, new[] { 1.4, 1.0, 0.8 }, 180)));

            if (!api.Scopes.IsEnabled("demo.core"))
            {
                page.Children.Add(api.Ui.Card(
                    api.I18n.T("核心作用域已停用", "Core scope disabled"),
                    api.Ui.EmptyState(
                        api.I18n.T("基础功能当前停用", "Core features are off"),
                        api.I18n.T("重新启用 demo.core 后，消息订阅和页面主体会立即恢复。",
                            "Enable demo.core again to restore messaging and the page body."))));
                return api.Ui.Scroll(page);
            }

            page.Children.Add(api.Ui.Card(
                api.I18n.T("控件示例", "Widgets"),
                api.Ui.Slider(api.I18n.T("音量", "Volume"), 0, 100, 1, _volume,
                    value => _volume = value),
                api.Ui.TextInput(api.I18n.T("自定义文本", "Custom text"),
                    api.Data.Get("text", string.Empty),
                    text =>
                    {
                        api.Data.Set("text", text);
                        api.Data.Save();
                    }),
                api.Ui.Separator(),
                api.Ui.Link(api.I18n.T("打开 Swap 仓库", "Open the Swap repository"),
                    "https://github.com/BlockConnectExp/PCL2.dlswap"),
                api.Ui.Buttons(
                    api.Ui.Button(api.I18n.T("复制宿主版本", "Copy host version"), () =>
                    {
                        var ok = api.Pcl.Clipboard.SetText(api.Host.SwapVersion);
                        api.Ui.Hint(ok
                            ? api.I18n.T("已复制宿主版本。", "Host version copied.")
                            : api.I18n.T("复制失败。", "Copy failed."));
                    }),
                    api.Ui.Button(api.I18n.T("打开日志目录", "Open log folder"),
                        () => api.Pcl.Shell.OpenFolder(api.Host.LogDirectory)))));

            page.Children.Add(api.Ui.Card(
                api.I18n.T("说明", "Notes"),
                api.Ui.Note(api.I18n.T(
                    "这个页面由示例 Addon 注册；删除 examples/demo-addon 不影响 PCL2 Swap 本身。",
                    "This page is registered by the demo addon; removing examples/demo-addon does not affect PCL2 Swap."))));

            if (api.Scopes.IsEnabled("demo.advanced"))
            {
                page.Children.Add(api.Ui.Card(
                    api.I18n.T("API 1.9 前后端", "API 1.9 back end and front end"),
                    api.Ui.Table(
                        new[]
                        {
                            api.I18n.T("能力", "Surface"),
                            api.I18n.T("状态", "Status")
                        },
                        new IReadOnlyList<string>[]
                        {
                            new[] { "IModuleScopes", api.I18n.T("可用", "available") },
                            new[] { "IAddonFiles", api.I18n.T("可用", "available") },
                            new[] { "IAddonMessaging", _messageHits + api.I18n.T(" 条消息", " message(s)") },
                            new[] { "IUi.Table", api.Host.ApiVersion },
                            new[] { "IUi.EmptyState", api.I18n.T("可用", "available") },
                            new[] { "IUi.CodeBlock", api.I18n.T("可用", "available") }
                        },
                        new[] { 1.4, 1.0 }),
                    api.Ui.EmptyState(
                        api.I18n.T("这里没有待处理项目", "Nothing is waiting"),
                        api.I18n.T("空状态用于解释为什么为空，并给出一个明确动作。",
                            "An empty state explains why the collection is empty and offers one clear action."),
                        api.Ui.Button(api.I18n.T("刷新页面", "Refresh"),
                            () => api.Ui.Hint(api.I18n.T("已刷新。", "Refreshed.")))),
                    api.Ui.CodeBlock(
                        "api.Hooks.Attach(target, \"demo.hook\", \"demo.core\", handler, HookMode.Prefix);\n" +
                        "api.Messaging.Publish(\"demo.ready\", payload);\n" +
                        "api.Scopes.Changed += OnScopesChanged;", 170)));
            }
            else
            {
                page.Children.Add(api.Ui.Card(
                    api.I18n.T("高级作用域已停用", "Advanced scope disabled"),
                    api.Ui.EmptyState(
                        api.I18n.T("高级示例未加载", "Advanced examples are not loaded"),
                        api.I18n.T("启用 demo.advanced 后重新打开此页面即可看到扩展前端。",
                            "Enable demo.advanced and reopen this page to see the extended front end."))));
            }

            return api.Ui.Scroll(page);
        }

        private FrameworkElement BuildMore(ISwapApi api)
        {
            var page = Page();
            page.Children.Add(api.Ui.Card(
                api.I18n.T("示例页面", "Demo page"),
                api.Ui.Text(api.I18n.T(
                    "这是在「更多」侧栏注册的第二个页面槽位。",
                    "This page uses the second page slot, registered into the More sidebar.")),
                api.Ui.InfoRow(api.I18n.T("保存的文本", "Saved text"),
                    api.Data.Get("text", api.I18n.T("（空）", "(empty)")))));
            return api.Ui.Scroll(page);
        }

        private static string ScopeName(ISwapApi api, SwapScopeInfo scope) =>
            api.I18n.IsEnglish
                ? FirstNonEmpty(scope.NameEnglish, scope.Name, scope.Id)
                : FirstNonEmpty(scope.Name, scope.Id);

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        /// <summary>Matches the 25px page margin PCL uses for settings pages.</summary>
        private static StackPanel Page() =>
            new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
    }
}
