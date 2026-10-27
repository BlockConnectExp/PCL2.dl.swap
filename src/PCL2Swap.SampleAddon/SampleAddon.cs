using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL2Swap.Api;

namespace PCL2Swap.SampleAddon
{
    /// <summary>
    /// Reference module: subscribes to the host event bus, attaches a Prefix hook, keeps its own
    /// persistent data and registers a live settings page - i.e. it touches every part of the
    /// public SDK. Doubles as the packaging example the author guide points at
    /// (<c>tools/PclAddonPack</c>).
    /// </summary>
    public sealed class SampleAddon : ISwapAddon
    {
        // Lifecycle + event counters. Static so the harness can assert them without owning the
        // instance the package loader created.
        public static int LoadCalls;
        public static int EnableCalls;
        public static int DisableCalls;
        public static int UnloadCalls;
        public static int DialogEvents;
        public static int UpdateEvents;
        public static int DownloadEvents;
        public static int HookHits;
        public static string LastApiVersion;
        public static string LastDialogTitle;
        public static string LastHookTarget;

        private ISwapApi _api;
        private IDisposable _hook;
        private DispatcherTimer _timer;
        private TextBlock _liveLabel;

        public void OnLoad(SwapContext context)
        {
            LoadCalls++;
            _api = context?.Api;
            LastApiVersion = _api?.Host?.ApiVersion;
            _api?.Data?.Set("loaded", "1");
            _api?.Data?.Save();
            _api?.Log($"示例模块已加载（API {LastApiVersion}，宿主 {_api?.Host?.SwapVersion}，" +
                      $"数据目录 {_api?.Host?.AddonDirectory}）。");
        }

        public void OnEnable()
        {
            EnableCalls++;
            var hooks = _api?.Hooks;
            if (hooks == null) return;

            hooks.DialogOpening += OnDialogOpening;
            hooks.UpdateRequested += OnUpdateRequested;
            hooks.DownloadCompleted += OnDownloadCompleted;

            AttachDemoHook(hooks);
            RegisterPage();
            _api.Log("示例模块已订阅事件总线、注册演示页面并尝试挂载 Prefix 钩子。");
        }

        public void OnDisable()
        {
            DisableCalls++;
            var hooks = _api?.Hooks;
            if (hooks != null)
            {
                hooks.DialogOpening -= OnDialogOpening;
                hooks.UpdateRequested -= OnUpdateRequested;
                hooks.DownloadCompleted -= OnDownloadCompleted;
            }

            _hook?.Dispose();
            _hook = null;
            StopTimer();
        }

        public void OnUnload() => UnloadCalls++;

        // ------------------------------------------------------------------ hook + events

        /// <summary>
        /// A Prefix hook only observes: the platform still runs the host method and returns its
        /// value, so a module can count/inspect calls without changing launcher behaviour.
        /// The target list is defensive - a host build may not carry every helper.
        /// </summary>
        private void AttachDemoHook(IHooks hooks)
        {
            // The handler shape must match the target's ABI (the platform rejects it otherwise),
            // so each candidate carries its own delegate.
            var candidates = new[]
            {
                Tuple.Create("PCL.ModBase", "ToUnixTimestampUtc", (Delegate)(Action<DateTime>)(_ => Ping())),
                Tuple.Create("PCL.ModBase", "GetUuid", (Delegate)(Action)Ping),
                Tuple.Create("PCL.ModBase", "GetTimeMs", (Delegate)(Action)Ping)
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    _hook = hooks.Attach(candidate.Item1, candidate.Item2, "sample.demo.prefix",
                        candidate.Item3, HookMode.Prefix);
                    LastHookTarget = candidate.Item1 + "." + candidate.Item2;
                    _api.Log($"示例模块已在 {LastHookTarget} 上挂 Prefix 钩子（只观察，不改变宿主行为）。");
                    return;
                }
                catch (Exception ex)
                {
                    _api.LogWarning($"挂钩子 {candidate.Item1}.{candidate.Item2} 失败：" + ex.Message);
                }
            }
            LastHookTarget = "(无可用目标)";
        }

        private void Ping() => HookHits++;

        private void OnDialogOpening(object sender, DialogEventArgs args)
        {
            DialogEvents++;
            LastDialogTitle = args?.Title;
            _api?.Log($"示例模块收到弹窗：{args?.Title}（warn={args?.IsWarn}，forceWait={args?.ForceWait}，" +
                      $"已追加取消={args?.CancelAdded}）");
        }

        private void OnUpdateRequested(object sender, UpdateEventArgs args)
        {
            UpdateEvents++;
            _api?.Log($"示例模块收到更新请求：silent={args?.Silent}，地址数={args?.Urls?.Length ?? 0}");
        }

        private void OnDownloadCompleted(object sender, DownloadEventArgs args)
        {
            DownloadEvents++;
            _api?.Log($"示例模块收到下载完成：地址数={args?.Urls?.Length ?? 0}，本地={args?.LocalPath}");
        }

        // ------------------------------------------------------------------ page

        private void RegisterPage()
        {
            try
            {
                _api.Ui.RegisterSettingsPage("sample.demo", "示例模块", BuildView);
            }
            catch (Exception ex)
            {
                _api.LogWarning("注册演示页面失败：" + ex.Message);
            }
        }

        private FrameworkElement BuildView()
        {
            var panel = new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = "示例模块（Sample Addon）",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"API {LastApiVersion} · 钩子目标 {LastHookTarget ?? "(未挂载)"}",
                Opacity = 0.7,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _liveLabel = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_liveLabel);
            RefreshLabel();

            // Live view: the page is built once and cached, so a timer keeps the counters honest
            // while the user is looking at it.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (sender, args) => RefreshLabel();
            _timer.Start();

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            var button = new Button
            {
                Content = "调用宿主方法并刷新",
                Padding = new Thickness(12, 4, 12, 4),
                MinWidth = 150
            };
            button.Click += (sender, args) =>
            {
                // Two SDK features in one click: the reflection escape hatch reaches any host member
                // (here the method our Prefix hook observes), and the hook counter below goes up.
                try
                {
                    _api.Reflect.Call(_api.Reflect.HostType("PCL.ModBase"), "ToUnixTimestampUtc",
                        DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _api.LogWarning("调用宿主方法失败：" + ex.Message);
                }
                RefreshLabel();
            };
            actions.Children.Add(button);
            panel.Children.Add(actions);

            return panel;
        }

        private void RefreshLabel()
        {
            if (_liveLabel == null) return;
            _liveLabel.Text =
                $"生命周期：加载 {LoadCalls} 次 · 启用 {EnableCalls} 次 · 禁用 {DisableCalls} 次\n" +
                $"事件：弹窗 {DialogEvents} · 更新请求 {UpdateEvents} · 下载完成 {DownloadEvents}\n" +
                $"Prefix 钩子命中：{HookHits} 次（目标 {LastHookTarget ?? "(未挂载)"}）\n" +
                $"当前时间：{DateTime.Now:HH:mm:ss}";
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer = null;
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
