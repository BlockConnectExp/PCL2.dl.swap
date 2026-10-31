using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCL2Swap.Api;
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

        /// <summary>
        /// A native-looking mutually exclusive choice row. PCL's MyRadioButton is used when the
        /// running build exposes it; the fallback uses one WPF RadioButton group per row.
        /// </summary>
        internal static FrameworkElement RadioGroup(string label, IReadOnlyList<string> options,
            int selectedIndex, Action<int> onChange)
        {
            options = options ?? new string[0];
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var controls = new List<FrameworkElement>();
            var radioType = HostType("PCL.MyRadioButton");
            var groupName = "swap-" + Guid.NewGuid().ToString("N");

            for (var index = 0; index < options.Count; index++)
            {
                var optionIndex = index;
                FrameworkElement control = null;
                if (radioType != null)
                {
                    try
                    {
                        control = Activator.CreateInstance(radioType) as FrameworkElement;
                        if (control != null)
                        {
                            PclUi.SetMember(control, "Text", options[index]);
                            PclUi.SetMember(control, "Checked", index == selectedIndex);
                            PclUi.SetMember(control, "Margin", new Thickness(0, 0, 16, 6));
                            control.PreviewMouseLeftButtonUp += (sender, args) =>
                            {
                                SelectRadio(controls, optionIndex, onChange);
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("SwapUi：MyRadioButton 构建失败，退回标准单选框：" + ex.Message);
                    }
                }

                if (control == null)
                {
                    var radio = new RadioButton
                    {
                        Content = options[index],
                        GroupName = groupName,
                        IsChecked = index == selectedIndex,
                        Margin = new Thickness(0, 0, 16, 6),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    radio.Checked += (sender, args) => RelayInt(onChange, optionIndex);
                    control = radio;
                }

                controls.Add(control);
                panel.Children.Add(control);
            }

            return string.IsNullOrEmpty(label) ? panel : Row(label, panel);
        }

        /// <summary>A compact numeric editor with bounded -/+ controls.</summary>
        internal static FrameworkElement NumberInput(string label, double initial, double min,
            double max, double step, Action<double> onChange)
        {
            if (max < min)
            {
                var swap = min;
                min = max;
                max = swap;
            }
            if (step <= 0) step = 1;

            TextBox box = null;
            var boxType = HostType("PCL.MyTextBox");
            if (boxType != null)
            {
                try { box = Activator.CreateInstance(boxType) as TextBox; }
                catch { /* fall through */ }
            }
            if (box == null) box = new TextBox();
            box.Width = 120;
            box.Height = 31;
            box.VerticalContentAlignment = VerticalAlignment.Center;

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var minButton = new Button
            {
                Content = "-",
                Width = 34,
                Height = 31,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var maxButton = new Button
            {
                Content = "+",
                Width = 34,
                Height = 31,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0)
            };

            var current = Math.Min(max, Math.Max(min, initial));
            var updating = false;
            Action<double> apply = value =>
            {
                current = Math.Min(max, Math.Max(min, value));
                updating = true;
                box.Text = current.ToString("0.###", CultureInfo.InvariantCulture);
                updating = false;
                try { onChange?.Invoke(current); }
                catch (Exception ex) { SwapLog.Warn("SwapUi：数字输入回调失败：" + ex.Message); }
            };

            box.Text = current.ToString("0.###", CultureInfo.InvariantCulture);
            box.TextChanged += (sender, args) =>
            {
                if (updating) return;
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
                    !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return;
                current = Math.Min(max, Math.Max(min, parsed));
                try { onChange?.Invoke(current); }
                catch (Exception ex) { SwapLog.Warn("SwapUi：数字输入回调失败：" + ex.Message); }
            };
            box.LostFocus += (sender, args) => apply(current);
            minButton.Click += (sender, args) => apply(current - step);
            maxButton.Click += (sender, args) => apply(current + step);

            stack.Children.Add(minButton);
            stack.Children.Add(box);
            stack.Children.Add(maxButton);
            return Row(label, stack);
        }

        /// <summary>Masked input; the value is read only from PasswordBox events.</summary>
        internal static FrameworkElement PasswordInput(string label, string initial,
            Action<string> onChange)
        {
            var box = new PasswordBox
            {
                Height = 35,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Password = initial ?? string.Empty
            };
            box.PasswordChanged += (sender, args) =>
            {
                try { onChange?.Invoke(box.Password); }
                catch (Exception ex) { SwapLog.Warn("SwapUi：密码输入回调失败：" + ex.Message); }
            };
            return Row(label, box);
        }

        /// <summary>Multi-line editor with an explicit minimum height and no width clipping.</summary>
        internal static FrameworkElement MultilineTextInput(string label, string initial,
            int minHeight, Action<string> onChange)
        {
            var root = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(Text(label, 12.5, false, false));
            var box = new TextBox
            {
                Text = initial ?? string.Empty,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = Math.Max(48, minHeight),
                Margin = new Thickness(0, 5, 0, 0)
            };
            box.TextChanged += (sender, args) =>
            {
                try { onChange?.Invoke(box.Text); }
                catch (Exception ex) { SwapLog.Warn("SwapUi：多行输入回调失败：" + ex.Message); }
            };
            root.Children.Add(box);
            return root;
        }

        /// <summary>Local image preview through PCL.MyImage with a plain WPF fallback.</summary>
        internal static FrameworkElement Image(string path, double width = 0, double height = 0)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Note("(no image)", true);

            var imageType = HostType("PCL.MyImage");
            if (imageType != null)
            {
                try
                {
                    if (Activator.CreateInstance(imageType) is FrameworkElement image)
                    {
                        PclUi.SetMember(image, "Source", path);
                        if (width > 0) image.Width = width;
                        if (height > 0) image.Height = height;
                        return image;
                    }
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("SwapUi：MyImage 构建失败，退回标准图片：" + ex.Message);
                }
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) return Note("(image not found)", true);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform
                };
                if (width > 0) image.Width = width;
                if (height > 0) image.Height = height;
                return image;
            }
            catch (Exception ex)
            {
                SwapLog.Warn("SwapUi：图片加载失败：" + ex.Message);
                return Note("(image could not be loaded)", true);
            }
        }

        /// <summary>Progress bar plus a live status label; setters are safe from worker threads.</summary>
        internal static IProgressView Progress(string label, double initial = 0,
            bool indeterminate = false)
        {
            var root = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caption = Text(label, 12.5, false, false);
            header.Children.Add(caption);
            var status = Text(string.Empty, 11.5, true, false);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(status, 1);
            header.Children.Add(status);
            root.Children.Add(header);

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = Math.Min(1, Math.Max(0, initial)),
                IsIndeterminate = indeterminate,
                Height = 6,
                Margin = new Thickness(0, 5, 0, 0)
            };
            UseBrush(bar, Control.ForegroundProperty, "ColorBrush3");
            root.Children.Add(bar);
            return new ProgressViewImpl(root, bar, status);
        }

        private sealed class ProgressViewImpl : IProgressView
        {
            private readonly ProgressBar _bar;
            private readonly TextBlock _status;
            private readonly object _gate = new object();
            private double _value;
            private bool _indeterminate;
            private string _statusText = string.Empty;

            public ProgressViewImpl(FrameworkElement element, ProgressBar bar, TextBlock status)
            {
                Element = element;
                _bar = bar;
                _status = status;
                _value = bar.Value;
                _indeterminate = bar.IsIndeterminate;
            }

            public FrameworkElement Element { get; }

            public double Value
            {
                get { lock (_gate) return _value; }
                set
                {
                    var clamped = Math.Min(1, Math.Max(0, value));
                    lock (_gate) _value = clamped;
                    OnUiThread(() => _bar.Value = clamped);
                }
            }

            public bool IsIndeterminate
            {
                get { lock (_gate) return _indeterminate; }
                set
                {
                    lock (_gate) _indeterminate = value;
                    OnUiThread(() => _bar.IsIndeterminate = value);
                }
            }

            public string Status
            {
                get { lock (_gate) return _statusText; }
                set
                {
                    var text = value ?? string.Empty;
                    lock (_gate) _statusText = text;
                    OnUiThread(() => _status.Text = text);
                }
            }

            private void OnUiThread(Action action)
            {
                var dispatcher = _bar.Dispatcher;
                if (dispatcher.CheckAccess()) action();
                else dispatcher.BeginInvoke(DispatcherPriority.Background, action);
            }
        }

        private static void SelectRadio(IReadOnlyList<FrameworkElement> controls, int selected,
            Action<int> onChange)
        {
            for (var index = 0; index < controls.Count; index++)
            {
                try { PclUi.SetMember(controls[index], "Checked", index == selected); }
                catch { }
            }
            RelayInt(onChange, selected);
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
