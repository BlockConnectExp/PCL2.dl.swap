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
        public static string LastHostPath;

        private ISwapApi _api;
        private IDisposable _hook;
        private DispatcherTimer _timer;
        private TextBlock _liveLabel;
        private IProgressView _progress;
        private bool _countHook = true;

        public void OnLoad(SwapContext context)
        {
            LoadCalls++;
            _api = context?.Api;
            LastApiVersion = _api?.Host?.ApiVersion;
            _countHook = AddonSettings.GetBool(_api?.Data, "demo.countHook", true);
            _api?.Data?.Set("loaded", "1");
            _api?.Data?.Save();
            LastHostPath = _api?.Pcl?.Environment?.HostPath;
            _api?.Log($"示例模块已加载（API {LastApiVersion}，宿主 {_api?.Host?.SwapVersion}，" +
                      $"数据目录 {_api?.Host?.AddonDirectory}，记录钩子命中={_countHook}）。");
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

        private void Ping()
        {
            if (_countHook) HookHits++;
        }

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
            var page = new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
            _liveLabel = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            RefreshLabel();

            page.Children.Add(_api.Ui.Card("示例模块（Sample Addon）",
                _api.Ui.Text($"API {LastApiVersion} · 钩子目标 {LastHookTarget ?? "(未挂载)"}", 12, true),
                _liveLabel));

            page.Children.Add(_api.Ui.Card("这个模块用到了什么",
                _api.Ui.Note("事件总线（弹窗 / 更新请求 / 下载完成）、模块作用域 Prefix 钩子、"),
                _api.Ui.Note("持久化数据、Reflect 直达宿主方法，以及下面这个原生外观按钮。"),
                _api.Ui.Buttons(_api.Ui.Button("调用宿主方法并刷新", CallHostAndRefresh))));

            page.Children.Add(_api.Ui.Card("模块设置（自动持久化）",
                _api.Ui.CheckBox("记录 Prefix 钩子命中", _countHook, value =>
                {
                    _countHook = value;
                    AddonSettings.SetBool(_api.Data, "demo.countHook", value);
                    _api.Log($"示例模块设置：记录钩子命中 = {value}");
                    RefreshLabel();
                }),
                _api.Ui.Combo("页面刷新频率", new[] { "1 秒", "2 秒", "5 秒" },
                    AddonSettings.GetInt(_api.Data, "demo.refreshIndex", 0), ApplyRefreshInterval)));

            page.Children.Add(_api.Ui.Card("API 1.3 控件与后端",
                _api.Ui.RadioGroup("演示选项", new[] { "简洁", "详细" },
                    AddonSettings.GetInt(_api.Data, "demo.radio", 0), value =>
                    {
                        AddonSettings.SetInt(_api.Data, "demo.radio", value);
                        _api.Log("示例模块单选：" + value);
                    }),
                _api.Ui.NumberInput("演示数值", AddonSettings.GetDouble(_api.Data, "demo.number", 3),
                    0, 100, 1, value =>
                    {
                        AddonSettings.SetDouble(_api.Data, "demo.number", value);
                        _api.Log("示例模块数值：" + value);
                    }),
                _api.Ui.PasswordInput("演示密码", string.Empty,
                    value => _api.Log("示例模块密码长度：" + (value ?? string.Empty).Length)),
                _api.Ui.MultilineTextInput("演示备注", AddonSettings.GetString(_api.Data, "demo.notes"),
                    70, value => AddonSettings.SetString(_api.Data, "demo.notes", value))));

            _progress = null;
            try
            {
                _progress = _api.Ui.Progress("示例实时进度", 0.25, false);
                page.Children.Add(_api.Ui.Card("后端与实时进度",
                    _api.Ui.InfoRow("宿主目录", _api.Pcl.Environment.HostDirectory),
                    _api.Ui.InfoRow("数据目录", _api.Pcl.Environment.DataDirectory),
                    _progress.Element));
            }
            catch (Exception ex)
            {
                _api.LogWarning("创建 API 1.3 后端演示失败：" + ex.Message);
            }

            page.Children.Add(_api.Ui.Card("API 1.6 模块治理",
                _api.Ui.Buttons(
                    _api.Ui.Badge("模块目录 " + (_api.Modules?.Modules.Count ?? 0), SwapHintType.Info),
                    _api.Ui.Badge("签名钩子", SwapHintType.Success)),
                _api.Ui.Tabs(new[] { "概览", "说明" }, 0, index =>
                    _api.Ui.Text(index == 0
                        ? "模块目录、权限授予和签名目标选择均由宿主统一提供。"
                        : "旧 API 保持兼容；新模块可精确选择重载，并按用户授权运行。", 12.5, true)),
                _api.Ui.Expander("高级信息", false, () =>
                    _api.Ui.Note("这个区域由 IUi.Expander 构建，内容在展开前不会干扰主页面。"))));

            // Live view: the page is built once and cached, so a timer keeps the counters honest
            // while the user is looking at it.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (sender, args) => RefreshLabel();
            _timer.Start();

            return page;
        }

        /// <summary>Applies a persisted dropdown choice to the live timer.</summary>
        private void ApplyRefreshInterval(int index)
        {
            AddonSettings.SetInt(_api.Data, "demo.refreshIndex", index);
            var seconds = index <= 0 ? 1 : index == 1 ? 2 : 5;
            if (_timer != null) _timer.Interval = TimeSpan.FromSeconds(seconds);
            _api.Log($"示例模块设置：刷新频率 = {seconds} 秒");
            RefreshLabel();
        }

        /// <summary>
        /// Two SDK features in one click: the reflection escape hatch reaches any host member (here
        /// the method our Prefix hook observes) and the hook counter on the page goes up.
        /// </summary>
        private void CallHostAndRefresh()
        {
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
        }

        private void RefreshLabel()
        {
            if (_liveLabel == null) return;
            _liveLabel.Text =
                $"生命周期：加载 {LoadCalls} 次 · 启用 {EnableCalls} 次 · 禁用 {DisableCalls} 次\n" +
                $"事件：弹窗 {DialogEvents} · 更新请求 {UpdateEvents} · 下载完成 {DownloadEvents}\n" +
                $"Prefix 钩子命中：{HookHits} 次（目标 {LastHookTarget ?? "(未挂载)"}）\n" +
                $"当前时间：{DateTime.Now:HH:mm:ss}";
            if (_progress != null)
            {
                _progress.Value = (DateTime.Now.Second % 60) / 60.0;
                _progress.Status = DateTime.Now.ToString("HH:mm:ss");
            }
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
