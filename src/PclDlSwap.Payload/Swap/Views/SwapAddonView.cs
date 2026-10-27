using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap.Views
{
    /// <summary>
    /// "Swap Addon" settings page: lists built-in and installed addons with their live status.
    /// Editing (enable/disable, import) arrives with the P1 addon manager work.
    /// </summary>
    internal static class SwapAddonView
    {
        public static FrameworkElement Build(ISwapApi api, AddonManager addons)
        {
            var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 24) };
            panel.Children.Add(Header("Swap Addon"));
            panel.Children.Add(Sub(
                $"宿主 {api?.Host.SwapVersion ?? "?"} · 形状 {api?.Host.Shape.ToString() ?? "?"} · " +
                $"API {api?.Host.ApiVersion ?? "?"} · 共 {addons?.Records.Count ?? 0} 个 Addon"));

            if (addons != null)
            {
                foreach (var record in addons.Records)
                    panel.Children.Add(Row(record));
            }

            panel.Children.Add(NewButton("打开 Addon 目录", () => OpenFolder(api?.Host.AddonDirectory)));
            return Wrap(panel);
        }

        // ------------------------------------------------------------------ rows

        private static FrameworkElement Row(AddonManager.AddonRecord record)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };

            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock
            {
                Text = record.Name ?? record.Id,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });
            line.Children.Add(new TextBlock
            {
                Text = " " + (record.Version ?? string.Empty) + (record.BuiltIn ? "  ·  内置" : string.Empty),
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 0)
            });
            line.Children.Add(new TextBlock
            {
                Text = StatusText(record),
                Foreground = StatusBrush(record),
                FontSize = 12,
                Margin = new Thickness(12, 2, 0, 0)
            });
            panel.Children.Add(line);

            if (!string.IsNullOrEmpty(record.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = record.Description,
                    Opacity = 0.65,
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (record.Failure != null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "错误：" + record.Failure.Message,
                    Foreground = Brushes.IndianRed,
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = panel
            };
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

        private static Brush StatusBrush(AddonManager.AddonRecord record)
        {
            switch (record.Status)
            {
                case "enabled": return Brushes.SeaGreen;
                case "failed": return Brushes.IndianRed;
                default: return Brushes.Gray;
            }
        }

        // ------------------------------------------------------------------ chrome

        internal static TextBlock Header(string text) => new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        };

        internal static TextBlock Sub(string text) => new TextBlock
        {
            Text = text,
            Opacity = 0.65,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };

        internal static Button NewButton(string text, Action onClick)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += (sender, args) =>
            {
                try { onClick(); }
                catch (Exception ex) { Trace.WriteLine("[PCL2Swap] button failed: " + ex.Message); }
            };
            return button;
        }

        internal static ScrollViewer Wrap(FrameworkElement content) => new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        internal static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
