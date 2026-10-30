using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap.Views
{
    /// <summary>
    /// Builds the swap pages out of PCL's own controls so they read as part of the launcher:
    /// MyCard sections with the native title row, MyButton actions, theme brushes and the same
    /// 25px page / 40px card padding the shipped settings pages use (measured from
    /// Pages/PageSetup/PageSetupLaunch.xaml). Every builder falls back to plain WPF so a stub
    /// host (the harness) or a future PCL build without these controls still renders.
    /// </summary>
    internal static class SwapUi
    {
        private static Assembly Host => SwapHost.HostAssembly;

        internal static Type HostType(string name) => Host?.GetType(name, throwOnError: false);

        /// <summary>Native settings page frame: the pages use StackPanel Margin="25,25,25,10".</summary>
        internal static StackPanel Page() => new StackPanel { Margin = new Thickness(25, 25, 25, 10) };

        /// <summary>A PCL settings card: rounded chrome, drop shadow, bold title, 25/40/25/25 body.</summary>
        internal static FrameworkElement Card(string title, params FrameworkElement[] content)
        {
            var body = new StackPanel();
            foreach (var child in content)
            {
                if (child != null) body.Children.Add(child);
            }

            var cardType = HostType("PCL.MyCard");
            if (cardType != null)
            {
                try
                {
                    if (Activator.CreateInstance(cardType) is Panel card)
                    {
                        PclUi.SetMember(card, "Title", title);
                        PclUi.SetMember(card, "Margin", new Thickness(0, 0, 0, 15));
                        // Shipped cards carry their content as a direct child and let this margin
                        // clear the title row; MyCard only looks at Children[3] when it is swappable.
                        // 20/40/18 + an 18px tail are the numbers PCL's own SetContentBuilder uses
                        // (PageOtherHelp.HelpListLoad), so the spacing matches the stock cards.
                        body.Margin = new Thickness(20, 40, 18, 0);
                        body.Children.Add(new Border { Height = 18 });
                        card.Children.Add(body);
                        // Keep SharedSizeGroup columns (if a future card uses them) per card.
                        Grid.SetIsSharedSizeScope(card, true);
                        return (FrameworkElement)card;
                    }
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：MyCard(\"" + title + "\") 构建失败，退回普通卡片：" + ex.Message);
                }
            }

            var fallback = new StackPanel();
            fallback.Children.Add(Text(title, 13, false, false, FontWeights.Bold));
            body.Margin = new Thickness(0, 10, 0, 2);
            fallback.Children.Add(body);
            return new Border
            {
                Margin = new Thickness(0, 0, 0, 15),
                Padding = new Thickness(15, 12, 15, 14),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
                Child = fallback
            };
        }

        internal static TextBlock Text(string text, double size = 12.5, bool secondary = false,
            bool wrap = true, FontWeight? weight = null)
        {
            var block = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = size,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Opacity = secondary ? 0.65 : 1.0
            };
            if (weight.HasValue) block.FontWeight = weight.Value;
            UseBrush(block, TextBlock.ForegroundProperty, "ColorBrush1");
            return block;
        }

        internal static void UseBrush(FrameworkElement element, DependencyProperty property, string brushKey)
        {
            try
            {
                element.SetResourceReference(property, brushKey);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("SwapUi：资源画笔 " + brushKey + " 绑定失败：" + ex.Message);
            }
        }

        /// <summary>Label + value row, matching the Auto/* grid the shipped cards use.</summary>
        internal static FrameworkElement Row(string label, FrameworkElement value, double bottom = 9)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, bottom), MinHeight = 26 };
            // Measured against PageSetupLaunch: the caption starts at the card's content edge and
            // every control starts 134px further right. A fixed column (110 + 24 gap) keeps our
            // rows on the same grid as the shipped pages; an Auto column silently grows to the
            // widest label of the whole page and leaves a hole in the middle of the card.
            // English captions ("Addon directory") are wider than the Chinese originals; widen the
            // caption column while English is active instead of clipping the row label.
            var captionWidth = SwapI18n.Current.IsEnglish ? 150.0 : 110.0;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(captionWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var caption = Text(label, 12.5, false, false);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Margin = new Thickness(0, 0, 24, 0);
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            if (value != null)
            {
                value.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
            }
            return grid;
        }

        internal static FrameworkElement InfoRow(string label, string value) =>
            Row(label, Text(string.IsNullOrEmpty(value) ? "(unknown)" : value, 12.5, false));

        /// <summary>Row height matches the 35px button strips the shipped pages use.</summary>
        internal static FrameworkElement Buttons(params FrameworkElement[] buttons)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            foreach (var button in buttons)
            {
                if (button != null) stack.Children.Add(button);
            }
            return stack;
        }

        internal static FrameworkElement Button(string text, Action onClick, string colorType = null)
        {
            var buttonType = HostType("PCL.MyButton");
            if (buttonType != null)
            {
                try
                {
                    if (Activator.CreateInstance(buttonType) is FrameworkElement button)
                    {
                        PclUi.SetMember(button, "Text", text);
                        PclUi.SetMember(button, "Height", 35.0);
                        PclUi.SetMember(button, "MinWidth", 140.0);
                        PclUi.SetMember(button, "Padding", new Thickness(13, 0, 13, 0));
                        // 12px is the gap the shipped settings strips use between two buttons.
                        PclUi.SetMember(button, "Margin", new Thickness(0, 0, 12, 0));
                        if (!string.IsNullOrEmpty(colorType))
                        {
                            var stateType = HostType("PCL.MyButton+ColorState");
                            if (stateType != null)
                                PclUi.SetMember(button, "ColorType", Enum.Parse(stateType, colorType));
                        }

                        var relay = new ClickRelay { Action = onClick };
                        var clickEvent = buttonType.GetEvent("Click", BindingFlags.Public | BindingFlags.Instance);
                        var handler = clickEvent == null
                            ? null
                            : Delegate.CreateDelegate(clickEvent.EventHandlerType, relay,
                                typeof(ClickRelay).GetMethod(nameof(ClickRelay.OnClick)));
                        if (clickEvent != null && handler != null) clickEvent.AddEventHandler(button, handler);
                        return button;
                    }
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：MyButton(\"" + text + "\") 构建失败，退回标准按钮：" + ex.Message);
                }
            }

            var stock = new Button
            {
                Content = text,
                Height = 35,
                MinWidth = 140,
                Padding = new Thickness(13, 0, 13, 0),
                Margin = new Thickness(0, 0, 20, 0)
            };
            stock.Click += (sender, args) => Relay(onClick);
            return stock;
        }

        /// <summary>Static-safe glue: MyButton.Click is its own delegate type, not RoutedEventHandler.</summary>
        private sealed class ClickRelay
        {
            public Action Action;

            // ReSharper disable once UnusedMember.Local - invoked through the host's delegate type
            public void OnClick(object sender, object args) => Relay(Action);
        }

        private static void Relay(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                SwapLog.Warn("SwapUi：按钮动作失败：" + ex.Message);
            }
        }

        /// <summary>Short explanation line under a card title.</summary>
        internal static FrameworkElement Note(string text, bool secondary = true)
        {
            var block = Text(text, 12, secondary);
            // Stock cards breathe at an 18px line pitch; the default tight leading makes multi-line
            // Chinese paragraphs look cramped next to PCL's own text blocks.
            block.LineHeight = 18;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            block.Margin = new Thickness(0, 0, 0, 6);
            return block;
        }

        /// <summary>
        /// Labelled checkbox. PCL's own MyCheckBox already draws its caption, so it is returned
        /// directly; the stock WPF fallback carries the caption as its content as well. Its Change
        /// event only says "a user touched it", so the current value is read back from the control.
        /// </summary>
        internal static FrameworkElement CheckBox(string label, bool initial, Action<bool> onChange)
        {
            var checkType = HostType("PCL.MyCheckBox");
            if (checkType != null)
            {
                try
                {
                    if (Activator.CreateInstance(checkType) is FrameworkElement box)
                    {
                        PclUi.SetMember(box, "Text", label);
                        PclUi.SetMember(box, "Checked", initial);

                        var relay = new CheckRelay { Action = onChange, Target = box };
                        var changeEvent = checkType.GetEvent("Change",
                            BindingFlags.Public | BindingFlags.Instance);
                        var handler = changeEvent == null
                            ? null
                            : Delegate.CreateDelegate(changeEvent.EventHandlerType, relay,
                                typeof(CheckRelay).GetMethod(nameof(CheckRelay.OnChange)));
                        if (changeEvent != null && handler != null) changeEvent.AddEventHandler(box, handler);
                        return box;
                    }
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：MyCheckBox(\"" + label + "\") 构建失败，退回标准复选框：" + ex.Message);
                }
            }

            var stock = new CheckBox { Content = label, IsChecked = initial, VerticalAlignment = VerticalAlignment.Center };
            stock.Checked += (sender, args) => RelayBool(onChange, stock.IsChecked == true);
            stock.Unchecked += (sender, args) => RelayBool(onChange, stock.IsChecked == true);
            return stock;
        }

        /// <summary>
        /// Labelled dropdown row (label + control, like the shipped settings rows). Uses the host's
        /// MyComboBox/MyComboBoxItem when available, a stock ComboBox otherwise; either way the
        /// callback receives the selected index.
        /// </summary>
        internal static FrameworkElement Combo(string label, IReadOnlyList<string> options,
            int selectedIndex, Action<int> onChange)
        {
            ComboBox combo = null;
            var comboType = HostType("PCL.MyComboBox");
            if (comboType != null)
            {
                try { combo = Activator.CreateInstance(comboType) as ComboBox; }
                catch (Exception ex) { SwapLog.Warn("SwapUi：MyComboBox 构建失败，退回标准下拉框：" + ex.Message); }
            }
            if (combo == null) combo = new ComboBox();

            var itemType = HostType("PCL.MyComboBoxItem");
            if (options != null)
            {
                foreach (var option in options)
                {
                    if (itemType != null)
                    {
                        try
                        {
                            var item = Activator.CreateInstance(itemType);
                            PclUi.SetMember(item, "Content", option);
                            combo.Items.Add(item);
                            continue;
                        }
                        catch
                        {
                            // fall through to a plain string item
                        }
                    }
                    combo.Items.Add(option);
                }
            }

            if (selectedIndex >= 0 && selectedIndex < combo.Items.Count) combo.SelectedIndex = selectedIndex;
            // Shipped settings combos are 35px high (measured on PageSetupLaunch); the bare default
            // template renders ~30px and makes the whole row look out of step with the rest.
            combo.Height = 35;
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            combo.VerticalAlignment = VerticalAlignment.Center;
            combo.SelectionChanged += (sender, args) => RelayInt(onChange, combo.SelectedIndex);
            return Row(label, combo);
        }

        /// <summary>Labelled slider row with a live numeric readout (API 1.1).</summary>
        internal static FrameworkElement Slider(string label, double min, double max, double step,
            double initial, Action<double> onChange)
        {
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step <= 0 ? 0.01 : step,
                LargeChange = (step <= 0 ? 0.01 : step) * 5,
                Value = initial,
                Width = 260,
                VerticalAlignment = VerticalAlignment.Center
            };
            var readout = Text(initial.ToString("0.##"), 12.5, true, false);
            readout.MinWidth = 48;
            readout.VerticalAlignment = VerticalAlignment.Center;

            slider.ValueChanged += (sender, args) =>
            {
                try
                {
                    readout.Text = args.NewValue.ToString("0.##");
                    onChange?.Invoke(args.NewValue);
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：滑块回调失败：" + ex.Message);
                }
            };

            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            strip.Children.Add(slider);
            strip.Children.Add(new Border { Width = 10 });
            strip.Children.Add(readout);
            return Row(label, strip);
        }

        /// <summary>Labelled text field row; uses the host MyTextBox when available (API 1.1).</summary>
        internal static FrameworkElement TextInput(string label, string initial, Action<string> onChange)
        {
            TextBox box = null;
            var boxType = HostType("PCL.MyTextBox");
            if (boxType != null)
            {
                try { box = Activator.CreateInstance(boxType) as TextBox; }
                catch (Exception ex) { SwapLog.Warn("SwapUi：MyTextBox 构建失败，退回标准输入框：" + ex.Message); }
            }
            if (box == null) box = new TextBox();

            box.Text = initial ?? string.Empty;
            box.Height = 35;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.TextChanged += (sender, args) =>
            {
                try { onChange?.Invoke(box.Text); }
                catch (Exception ex) { SwapLog.Warn("SwapUi：输入回调失败：" + ex.Message); }
            };
            return Row(label, box);
        }

        /// <summary>Underlined link that opens through PCL's own OpenWebsite helper (API 1.1).</summary>
        internal static FrameworkElement Link(string text, string url)
        {
            var block = Text(text, 12.5, false);
            block.TextDecorations = TextDecorations.Underline;
            block.Cursor = System.Windows.Input.Cursors.Hand;
            block.MouseLeftButtonUp += (sender, args) => PclUi.OpenWebsite(url);
            return block;
        }

        /// <summary>Themed hairline between groups, matching the shipped card rhythm (API 1.1).</summary>
        internal static FrameworkElement Separator()
        {
            var line = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 6, 0, 12)
            };
            try
            {
                line.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
            }
            catch
            {
                line.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            }
            return line;
        }

        private sealed class CheckRelay
        {
            public Action<bool> Action;
            public object Target;

            // ReSharper disable once UnusedMember.Local - invoked through the host's delegate type
            public void OnChange(object sender, bool user)
            {
                bool value;
                try { value = Convert.ToBoolean(PclUi.GetMember(Target, "Checked") ?? false); }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：读取复选框状态失败：" + ex.Message);
                    return;
                }
                RelayBool(Action, value);
            }
        }

        private static void RelayBool(Action<bool> action, bool value)
        {
            try { action?.Invoke(value); }
            catch (Exception ex) { SwapLog.Warn("SwapUi：复选框回调失败：" + ex.Message); }
        }

        private static void RelayInt(Action<int> action, int value)
        {
            try { action?.Invoke(value); }
            catch (Exception ex) { SwapLog.Warn("SwapUi：下拉框回调失败：" + ex.Message); }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
