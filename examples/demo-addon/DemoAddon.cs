using System;
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
        private int _launches;
        private double _volume = 50;

        public void OnLoad(SwapContext context)
        {
            _context = context;
            var api = context.Api;
            _launches = int.Parse(api.Data.Get("launches", "0")) + 1;
            api.Data.Set("launches", _launches.ToString());
            api.Data.Save();

            api.Ui.RegisterSettingsPage("settings",
                api.I18n.T("示例设置", "Demo Settings"), () => BuildSettings(api));
            api.Ui.RegisterMorePage("more",
                api.I18n.T("示例页面", "Demo Page"), () => BuildMore(api));

            api.Log($"Demo addon loaded (launch #{_launches}).");
        }

        public void OnEnable() => _context?.Api.Log("Demo addon enabled.");
        public void OnDisable() => _context?.Api.Log("Demo addon disabled.");
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

            return page;
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
            return page;
        }

        /// <summary>Matches the 25px page margin PCL uses for settings pages.</summary>
        private static StackPanel Page() =>
            new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
    }
}
