using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// The pass is idempotent and coalesced: LayoutUpdated only schedules one background pass at
    /// a time, while a slow fallback timer catches top-level dialogs that stop repainting.
    /// </summary>
    internal static class HostTranslator
    {
        private const int FallbackIntervalSeconds = 4;
        private const int SettleQuietMilliseconds = 500;
        private const int MaxSettleLatencyMilliseconds = 1500;

        private static readonly object Gate = new object();
        private static readonly List<Window> Windows = new List<Window>();
        private static readonly HashSet<Window> Hooked = new HashSet<Window>();
        private static readonly HashSet<Window> Pending = new HashSet<Window>();
        private static readonly ConditionalWeakTable<DependencyObject, TranslationState> Ledger =
            new ConditionalWeakTable<DependencyObject, TranslationState>();
        private static volatile bool _started;
        private static System.Windows.Threading.DispatcherTimer _settleTimer;
        private static DateTime _lastLayoutUtc = DateTime.MinValue;
        private static DateTime _dirtySinceUtc = DateTime.MinValue;
        private static int _applying;
        private static long _passes;
        private static long _translatedTotal;
        private static long _lastDurationTicks;
        private static int _lastHits;

        /// <summary>Strings translated by the most recent pass (diagnostics / self-test).</summary>
        public static int LastHits => _lastHits;
        public static long Passes => Interlocked.Read(ref _passes);
        public static long TranslatedTotal => Interlocked.Read(ref _translatedTotal);
        public static double LastDurationMilliseconds =>
            Interlocked.Read(ref _lastDurationTicks) * 1000.0 / Stopwatch.Frequency;
        public static int TrackedWindows
        {
            get { lock (Gate) return Windows.Count; }
        }

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

                window.LayoutUpdated += (sender, args) => RequestApply(window);

                // LayoutUpdated only fires while the launcher repaints; a slow timer catches
                // pages that appear and then stay still (a freshly built Swap page, for example).
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(FallbackIntervalSeconds)
                };
                timer.Tick += (sender, args) =>
                {
                    if (!SwapI18n.Current.IsEnglish) return;
                    try
                    {
                        lock (Gate)
                        {
                            Pending.Clear();
                            _dirtySinceUtc = DateTime.MinValue;
                        }
                        ApplyAllWindows();
                    }
                    catch { }
                };
                timer.Start();

                if (_settleTimer == null)
                {
                    _settleTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    _settleTimer.Tick += (sender, args) => ApplySettledWindows();
                }

                if (SwapI18n.Current.IsEnglish) Apply(window);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 主窗口接管失败：" + ex.Message);
            }
        }

        private static void RequestApply(Window window)
        {
            if (!SwapI18n.Current.IsEnglish) return;
            lock (Gate)
            {
                Pending.Add(window);
                var now = DateTime.UtcNow;
                _lastLayoutUtc = now;
                if (_dirtySinceUtc == DateTime.MinValue) _dirtySinceUtc = now;
                if (_settleTimer != null && !_settleTimer.IsEnabled) _settleTimer.Start();
            }
        }

        private static void ApplySettledWindows()
        {
            Window[] windows;
            lock (Gate)
            {
                if (!SwapI18n.Current.IsEnglish || Pending.Count == 0)
                {
                    Pending.Clear();
                    _dirtySinceUtc = DateTime.MinValue;
                    if (_settleTimer != null) _settleTimer.Stop();
                    return;
                }

                var now = DateTime.UtcNow;
                var quiet = (now - _lastLayoutUtc).TotalMilliseconds >= SettleQuietMilliseconds;
                var maxWait = (now - _dirtySinceUtc).TotalMilliseconds >=
                              MaxSettleLatencyMilliseconds;
                if (!quiet && !maxWait) return;

                windows = Pending.ToArray();
                Pending.Clear();
                _dirtySinceUtc = DateTime.MinValue;
            }

            foreach (var window in windows)
            {
                try { Apply(window); }
                catch (Exception ex)
                {
                    SwapLog.Warn("[i18n] deferred window pass failed: " + ex.Message);
                }
            }
        }

        /// <summary>Re-applies the current language to every hooked window.</summary>
        public static void ReapplyAll()
        {
            lock (Gate)
            {
                Pending.Clear();
                _dirtySinceUtc = DateTime.MinValue;
                if (_settleTimer != null) _settleTimer.Stop();
            }
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

            // Dialogs are top-level windows, not descendants of the main form. They appear after
            // the main form and must be translated by the same language switch.
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    PclUi.OnUiThread(() =>
                    {
                        foreach (Window window in app.Windows)
                        {
                            if (SwapI18n.Current.IsEnglish) Apply(window);
                            else Restore(window);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 对话框重新应用失败：" + ex.Message);
            }
        }

        /// <summary>Applies the current dictionary to every top-level WPF window.</summary>
        public static int ApplyAllWindows()
        {
            var hits = 0;
            try
            {
                var app = Application.Current;
                if (app == null) return hits;
                foreach (Window window in app.Windows) hits += Apply(window);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 全窗口翻译失败：" + ex.Message);
            }
            return hits;
        }

        public static int Apply(object visualRoot)
        {
            if (Interlocked.Exchange(ref _applying, 1) != 0) return 0;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var i18n = SwapI18n.Current;
                var hits = Walk(visualRoot, node => TranslateNode(node, i18n));
                _lastHits = hits;
                Interlocked.Increment(ref _passes);
                Interlocked.Add(ref _translatedTotal, hits);
                stopwatch.Stop();
                Interlocked.Exchange(ref _lastDurationTicks, stopwatch.ElapsedTicks);
                if (hits > 0)
                    SwapLog.Info("[i18n] 本轮翻译 " + hits + " 处文本（" +
                                 stopwatch.Elapsed.TotalMilliseconds.ToString("0.0") + " ms）。");
                return hits;
            }
            finally
            {
                Interlocked.Exchange(ref _applying, 0);
            }
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
                    hits += TranslateValue(block, "text", block.Text,
                        value => block.Text = value, i18n);
            }

            if (node is AccessText access)
                hits += TranslateValue(access, "text", access.Text,
                    value => access.Text = value, i18n);

            // The launch log renders into a read-only TextBox on some builds; editable inputs are
            // deliberately left alone so user text is never rewritten.
            if (node is TextBox readOnly && readOnly.IsReadOnly)
                hits += TranslateValue(readOnly, "text", readOnly.Text,
                    value => readOnly.Text = value, i18n);

            if (node is RichTextBox richTextBox && richTextBox.Document != null)
            {
                foreach (var documentBlock in richTextBox.Document.Blocks)
                {
                    if (documentBlock is Paragraph paragraph)
                        hits += TranslateInlines(paragraph.Inlines, i18n);
                }
            }

            if (node is Window window)
                hits += TranslateValue(window, "title", window.Title,
                    value => window.Title = value, i18n);

            if (node is ContentControl content && content.Content is string contentText)
            {
                hits += TranslateValue(content, "content", contentText,
                    value => content.Content = value, i18n);
            }

            if (node is HeaderedContentControl headered && headered.Header is string headerText)
            {
                hits += TranslateValue(headered, "header", headerText,
                    value => headered.Header = value, i18n);
            }

            if (node is HeaderedItemsControl headeredItems &&
                headeredItems.Header is string itemsHeader)
            {
                hits += TranslateValue(headeredItems, "header", itemsHeader,
                    value => headeredItems.Header = value, i18n);
            }

            if (node is DataGrid dataGrid)
            {
                foreach (var column in dataGrid.Columns)
                {
                    if (!(column.Header is string header)) continue;
                    hits += TranslateValue(column, "header", header,
                        value => column.Header = value, i18n);
                }
            }

            if (node is FrameworkElement element)
            {
                if (element.ToolTip is string tooltip)
                {
                    hits += TranslateValue(element, "tooltip", tooltip,
                        value => element.ToolTip = value, i18n);
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
                    hits += RestoreValue(block, "text", block.Text,
                        value => block.Text = value, i18n);
            }

            if (node is AccessText access)
                hits += RestoreValue(access, "text", access.Text,
                    value => access.Text = value, i18n);

            if (node is TextBox readOnly && readOnly.IsReadOnly)
                hits += RestoreValue(readOnly, "text", readOnly.Text,
                    value => readOnly.Text = value, i18n);

            if (node is RichTextBox richTextBox && richTextBox.Document != null)
            {
                foreach (var documentBlock in richTextBox.Document.Blocks)
                {
                    if (documentBlock is Paragraph paragraph)
                        hits += RestoreInlines(paragraph.Inlines, i18n);
                }
            }

            if (node is Window window)
                hits += RestoreValue(window, "title", window.Title,
                    value => window.Title = value, i18n);

            if (node is ContentControl content && content.Content is string contentText)
            {
                hits += RestoreValue(content, "content", contentText,
                    value => content.Content = value, i18n);
            }

            if (node is HeaderedContentControl headered && headered.Header is string headerText)
            {
                hits += RestoreValue(headered, "header", headerText,
                    value => headered.Header = value, i18n);
            }

            if (node is HeaderedItemsControl headeredItems &&
                headeredItems.Header is string itemsHeader)
            {
                hits += RestoreValue(headeredItems, "header", itemsHeader,
                    value => headeredItems.Header = value, i18n);
            }

            if (node is DataGrid dataGrid)
            {
                foreach (var column in dataGrid.Columns)
                {
                    if (!(column.Header is string header)) continue;
                    hits += RestoreValue(column, "header", header,
                        value => column.Header = value, i18n);
                }
            }

            if (node is FrameworkElement element)
            {
                if (element.ToolTip is string tooltip)
                {
                    hits += RestoreValue(element, "tooltip", tooltip,
                        value => element.ToolTip = value, i18n);
                }

                if (!(element is TextBox) && !(element is TextBlock))
                    hits += RestoreStringProperty(element, i18n);
            }

            return hits;
        }

        private static string Original(string english, SwapI18n i18n) =>
            i18n.TryGetOriginal(english, out var chinese) ? chinese : null;

        private static int TranslateValue(DependencyObject owner, string slot, string current,
            Action<string> assign, SwapI18n i18n)
        {
            if (owner == null || string.IsNullOrEmpty(current)) return 0;
            var updated = i18n.Translate(current);
            if (string.IsNullOrEmpty(updated) || updated == current) return 0;

            var state = Ledger.GetOrCreateValue(owner);
            lock (state)
            {
                state.Slots[slot] = new TranslationSlot
                {
                    Original = current,
                    Translated = updated
                };
            }
            assign(updated);
            return 1;
        }

        private static int RestoreValue(DependencyObject owner, string slot, string current,
            Action<string> assign, SwapI18n i18n)
        {
            if (owner == null || string.IsNullOrEmpty(current)) return 0;

            TranslationSlot translated = null;
            if (Ledger.TryGetValue(owner, out var state))
            {
                lock (state)
                {
                    if (state.Slots.TryGetValue(slot, out translated))
                        state.Slots.Remove(slot);
                }
            }

            if (translated != null)
            {
                // The host may have regenerated the text since our pass. In that case its new value
                // wins; never restore a stale value over fresh UI state.
                if (!string.Equals(current, translated.Translated, StringComparison.Ordinal) ||
                    string.Equals(current, translated.Original, StringComparison.Ordinal))
                    return 0;
                assign(translated.Original);
                return 1;
            }

            // Compatibility fallback for a tree translated before this ledger existed.
            var original = Original(current, i18n);
            if (original == null) return 0;
            assign(original);
            return 1;
        }

        private sealed class TranslationState
        {
            public readonly Dictionary<string, TranslationSlot> Slots =
                new Dictionary<string, TranslationSlot>(StringComparer.Ordinal);
        }

        private sealed class TranslationSlot
        {
            public string Original;
            public string Translated;
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
                    hits += TranslateValue(element, "property:" + memberName, current,
                        value => property.SetValue(element, value), i18n);
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string) && !field.IsInitOnly)
                {
                    var current = field.GetValue(element) as string;
                    hits += TranslateValue(element, "field:" + memberName, current,
                        value => field.SetValue(element, value), i18n);
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
                    hits += RestoreValue(element, "property:" + memberName, current,
                        value => property.SetValue(element, value), i18n);
                }

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string) && !field.IsInitOnly)
                {
                    var current = field.GetValue(element) as string;
                    hits += RestoreValue(element, "field:" + memberName, current,
                        value => field.SetValue(element, value), i18n);
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
                    hits += TranslateValue(run, "text", run.Text,
                        value => run.Text = value, i18n);
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
                    hits += RestoreValue(run, "text", run.Text,
                        value => run.Text = value, i18n);
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
