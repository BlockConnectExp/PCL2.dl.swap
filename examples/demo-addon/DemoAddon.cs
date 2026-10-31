using System;
using System.Collections.Generic;
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
            api?.Log("Demo addon enabled.");
            try
            {
                _messageSubscription = api?.Messaging.Subscribe("demo.ready", message =>
                {
                    _messageHits++;
                    api.Log($"Demo message from {message.Source}: {message.Payload}");
                });
                api?.Messaging.Publish("demo.ready", "hello from Demo Addon");
            }
            catch (Exception ex)
            {
                api?.LogWarning("Demo messaging failed: " + ex.Message);
            }
        }

        public void OnDisable()
        {
            _messageSubscription?.Dispose();
            _messageSubscription = null;
            _context?.Api.Log("Demo addon disabled.");
        }

        public void OnUnload() => _context?.Api.Log("Demo addon unloaded.");

        private FrameworkElement BuildSettings(ISwapApi api)
        {
            var page = Page();

            page.Children.Add(api.Ui.Card(
                api.I18n.T("宿主信息", "Host information"),
                api.Ui.InfoRow(api.I18n.T("宿主版本", "Host version"), api.Host.SwapVersion),
                api.Ui.InfoRow(api.I18n.T("API 版本", "API version"), api.Host.ApiVersion),
                api.Ui.InfoRow(api.I18n.T("交付形状", "Delivery shape"), api.Host.Shape.ToString()),
                api.Ui.InfoRow(api.I18n.T("加载次数", "Launch count"), _launches.ToString())));

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

            page.Children.Add(api.Ui.Card(
                api.I18n.T("API 1.8 前后端", "API 1.8 back end and front end"),
                api.Ui.Table(
                    new[]
                    {
                        api.I18n.T("能力", "Surface"),
                        api.I18n.T("状态", "Status")
                    },
                    new IReadOnlyList<string>[]
                    {
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
                    "api.Messaging.Publish(\"demo.ready\", payload);\n" +
                    "api.Files.WriteText(\"demo/launch.txt\", text);\n" +
                    "api.I18n.Format(\"已处理 {0} 项\", count);", 150)));

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

        /// <summary>Matches the 25px page margin PCL uses for settings pages.</summary>
        private static StackPanel Page() =>
            new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
    }
}
