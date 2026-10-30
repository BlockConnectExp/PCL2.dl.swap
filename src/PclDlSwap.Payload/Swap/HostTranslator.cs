using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Runtime English layer for the Chinese-only PCL UI. It walks the WPF visual tree and
    /// swaps strings that exist in the dictionary - TextBlock text, control Text properties
    /// (MyButton and friends), string content/header, window titles and tooltips. Original text
    /// is recoverable because the dictionary maps English values back to their Chinese key.
    ///
    /// The pass is idempotent and throttled: translated strings no longer match a Chinese key,
    /// and LayoutUpdated ticks are coalesced to at most one pass per half second.
    /// </summary>
    internal static class HostTranslator
    {
        private const int PassIntervalMs = 500;

        private static readonly object Gate = new object();
        private static readonly List<Window> Windows = new List<Window>();
        private static readonly HashSet<Window> Hooked = new HashSet<Window>();
        private static DateTime _lastPass = DateTime.MinValue;
        private static volatile bool _started;
        private static int _lastHits;

        /// <summary>Strings translated by the most recent pass (diagnostics / self-test).</summary>
        public static int LastHits => _lastHits;

        public static void StartWatching(Assembly host)
        {
            if (_started) return;
            _started = true;

            var thread = new Thread(() =>
            {
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        if (SwapI18n.Current.IsEnglish)
                        {
                            var modMain = host?.GetType("PCL.ModMain", throwOnError: false);
                            var window = PclUi.GetMember(modMain, "FrmMain") as Window;
                            if (window != null)
                            {
                                PclUi.OnUiThread(() => HookWindow(window));
                                SwapLog.Info("[i18n] 主窗口已接管，按需翻译 Launcher 界面。");
                                break;
                            }
                        }
                        else
                        {
                            // Still hook the window so a later language switch can translate it.
                            var modMain = host?.GetType("PCL.ModMain", throwOnError: false);
                            var window = PclUi.GetMember(modMain, "FrmMain") as Window;
                            if (window != null)
                            {
                                PclUi.OnUiThread(() => HookWindow(window));
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("[i18n] 主窗口探测异常：" + ex.Message);
                    }

                    try { Thread.Sleep(1000); } catch { }
                }
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.I18n"
            };
            thread.Start();
        }

        private static void HookWindow(Window window)
        {
            try
            {
                lock (Gate)
                {
                    if (!Windows.Contains(window)) Windows.Add(window);
                    if (!Hooked.Add(window)) return;
                }

                window.LayoutUpdated += (sender, args) =>
                {
                    if (!SwapI18n.Current.IsEnglish) return;
                    var now = DateTime.UtcNow;
                    if ((now - _lastPass).TotalMilliseconds < PassIntervalMs) return;
                    _lastPass = now;
                    Apply(window);
                };

                // LayoutUpdated only fires while the launcher repaints; a slow timer catches
                // pages that appear and then stay still (a freshly built Swap page, for example).
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (sender, args) =>
                {
                    if (!SwapI18n.Current.IsEnglish) return;
                    try { Apply(window); } catch { }
                };
                timer.Start();

                if (SwapI18n.Current.IsEnglish) Apply(window);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 主窗口接管失败：" + ex.Message);
            }
        }

        /// <summary>Re-applies the current language to every hooked window.</summary>
        public static void ReapplyAll()
        {
            Window[] snapshot;
            lock (Gate) snapshot = Windows.ToArray();
            foreach (var window in snapshot)
            {
                try
                {
                    var target = window;
                    PclUi.OnUiThread(() =>
                    {
                        if (SwapI18n.Current.IsEnglish) Apply(target);
                        else Restore(target);
                    });
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("[i18n] 重新应用失败：" + ex.Message);
                }
            }
        }

        public static int Apply(object visualRoot)
        {
            var i18n = SwapI18n.Current;
            var hits = Walk(visualRoot, node => TranslateNode(node, i18n));
            _lastHits = hits;
            if (hits > 0) SwapLog.Info("[i18n] 本轮翻译 " + hits + " 处文本。");
            return hits;
        }

        public static int Restore(object visualRoot)
        {
            var i18n = SwapI18n.Current;
            var hits = Walk(visualRoot, node => RestoreNode(node, i18n));
            if (hits > 0) SwapLog.Info("[i18n] 本轮回退 " + hits + " 处文本。");
            return hits;
        }

        private static int Walk(object visualRoot, Func<DependencyObject, int> visit)
        {
            var root = visualRoot as DependencyObject;
            if (root == null) return 0;

            var hits = 0;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            var guard = 0;
            while (queue.Count > 0 && guard++ < 20000)
            {
                var node = queue.Dequeue();
                try { hits += visit(node); } catch { }

                try
                {
                    if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                    {
                        var count = VisualTreeHelper.GetChildrenCount(node);
                        for (var i = 0; i < count; i++)
                            queue.Enqueue(VisualTreeHelper.GetChild(node, i));
                    }
                }
                catch
                {
                    // some virtualised containers refuse tree access mid-layout
                }
            }

            return hits;
        }

        private static int TranslateNode(DependencyObject node, SwapI18n i18n)
        {
            var hits = 0;
            if (node is TextBlock block)
            {
                // A TextBlock that carries Runs renders the Runs, not .Text; assigning Text would
                // wipe the formatted log lines, so translate the inlines instead.
                if (block.Inlines.Count > 0)
                    hits += TranslateInlines(block.Inlines, i18n);
                else
                    hits += Replace(block.Text, value => block.Text = value, i18n.Translate);
            }

            if (node is AccessText access)
                hits += Replace(access.Text, value => access.Text = value, i18n.Translate);

            // The launch log renders into a read-only TextBox on some builds; editable inputs are
            // deliberately left alone so user text is never rewritten.
            if (node is TextBox readOnly && readOnly.IsReadOnly)
                hits += Replace(readOnly.Text, value => readOnly.Text = value, i18n.Translate);

            if (node is RichTextBox richTextBox && richTextBox.Document != null)
            {
                foreach (var documentBlock in richTextBox.Document.Blocks)
                {
                    if (documentBlock is Paragraph paragraph)
                        hits += TranslateInlines(paragraph.Inlines, i18n);
                }
            }

            if (node is Window window)
                hits += Replace(window.Title, value => window.Title = value, i18n.Translate);

            if (node is ContentControl content && content.Content is string contentText)
            {
                var updated = i18n.Translate(contentText);
                if (!ReferenceEquals(updated, contentText) && updated != contentText)
                {
                    content.Content = updated;
                    hits++;
                }
            }

            if (node is HeaderedContentControl headered && headered.Header is string headerText)
            {
                var updated = i18n.Translate(headerText);
                if (!ReferenceEquals(updated, headerText) && updated != headerText)
                {
                    headered.Header = updated;
                    hits++;
                }
            }

            if (node is FrameworkElement element)
            {
                if (element.ToolTip is string tooltip)
                {
                    var updated = i18n.Translate(tooltip);
                    if (!ReferenceEquals(updated, tooltip) && updated != tooltip)
                    {
                        element.ToolTip = updated;
                        hits++;
                    }
                }

                // PCL's MyButton keeps its caption in a writable string property rather than
                // Content; the same shape covers MyIconTextButton and MyExtraTextButton.
                if (!(element is TextBox) && !(element is TextBlock))
                    hits += TranslateStringProperty(element, i18n);
            }

            return hits;
        }

        private static int RestoreNode(DependencyObject node, SwapI18n i18n)
        {
            var hits = 0;
            if (node is TextBlock block)
            {
                if (block.Inlines.Count > 0)
                    hits += RestoreInlines(block.Inlines, i18n);
                else
                    hits += Replace(block.Text, value => block.Text = value, text => Original(text, i18n));
            }

            if (node is AccessText access)
                hits += Replace(access.Text, value => access.Text = value, text => Original(text, i18n));

            if (node is TextBox readOnly && readOnly.IsReadOnly)
                hits += Replace(readOnly.Text, value => readOnly.Text = value, text => Original(text, i18n));

            if (node is RichTextBox richTextBox && richTextBox.Document != null)
            {
                foreach (var documentBlock in richTextBox.Document.Blocks)
                {
                    if (documentBlock is Paragraph paragraph)
                        hits += RestoreInlines(paragraph.Inlines, i18n);
                }
            }

            if (node is Window window)
                hits += Replace(window.Title, value => window.Title = value, text => Original(text, i18n));

            if (node is ContentControl content && content.Content is string contentText)
            {
                var original = Original(contentText, i18n);
                if (original != null)
                {
                    content.Content = original;
                    hits++;
                }
            }

            if (node is HeaderedContentControl headered && headered.Header is string headerText)
            {
                var original = Original(headerText, i18n);
                if (original != null)
                {
                    headered.Header = original;
                    hits++;
                }
            }

            if (node is FrameworkElement element)
            {
                if (element.ToolTip is string tooltip)
                {
                    var original = Original(tooltip, i18n);
                    if (original != null)
                    {
                        element.ToolTip = original;
                        hits++;
                    }
                }

                if (!(element is TextBox) && !(element is TextBlock))
                    hits += RestoreStringProperty(element, i18n);
            }

            return hits;
        }

        private static string Original(string english, SwapI18n i18n) =>
            i18n.TryGetOriginal(english, out var chinese) ? chinese : null;

        private static int Replace(string current, Action<string> assign, Func<string, string> map)
        {
            if (string.IsNullOrEmpty(current)) return 0;
            var updated = map(current);
            if (string.IsNullOrEmpty(updated) || updated == current) return 0;
            assign(updated);
            return 1;
        }

        private static int TranslateStringProperty(FrameworkElement element, SwapI18n i18n)
        {
            var hits = 0;
            var type = element.GetType();
            // "Text" covers MyButton-style captions; "Info" is MyListItem's secondary line.
            foreach (var memberName in new[] { "Text", "Info" })
            {
                var property = type.GetProperty(memberName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(string) && property.CanWrite &&
                    property.CanRead)
                {
                    var current = property.GetValue(element) as string;
                    var updated = i18n.Translate(current);
                    if (!string.IsNullOrEmpty(updated) && updated != current)
                    {
                        property.SetValue(element, updated);
                        hits++;
                    }
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string) && !field.IsInitOnly)
                {
                    var current = field.GetValue(element) as string;
                    var updated = i18n.Translate(current);
                    if (!string.IsNullOrEmpty(updated) && updated != current)
                    {
                        field.SetValue(element, updated);
                        hits++;
                    }
                }
            }

            return hits;
        }

        private static int RestoreStringProperty(FrameworkElement element, SwapI18n i18n)
        {
            var hits = 0;
            var type = element.GetType();
            foreach (var memberName in new[] { "Text", "Info" })
            {
                var property = type.GetProperty(memberName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(string) && property.CanWrite &&
                    property.CanRead)
                {
                    var current = property.GetValue(element) as string;
                    var original = Original(current, i18n);
                    if (original != null)
                    {
                        property.SetValue(element, original);
                        hits++;
                    }
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string) && !field.IsInitOnly)
                {
                    var current = field.GetValue(element) as string;
                    var original = Original(current, i18n);
                    if (original != null)
                    {
                        field.SetValue(element, original);
                        hits++;
                    }
                }
            }

            return hits;
        }

        private static int TranslateInlines(InlineCollection inlines, SwapI18n i18n)
        {
            var hits = 0;
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    var updated = i18n.Translate(run.Text);
                    if (!string.IsNullOrEmpty(updated) && updated != run.Text)
                    {
                        run.Text = updated;
                        hits++;
                    }
                }
                else if (inline is Span span)
                {
                    hits += TranslateInlines(span.Inlines, i18n);
                }
            }

            return hits;
        }

        private static int RestoreInlines(InlineCollection inlines, SwapI18n i18n)
        {
            var hits = 0;
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    var original = Original(run.Text, i18n);
                    if (original != null)
                    {
                        run.Text = original;
                        hits++;
                    }
                }
                else if (inline is Span span)
                {
                    hits += RestoreInlines(span.Inlines, i18n);
                }
            }

            return hits;
        }
    }
}
