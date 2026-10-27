using System;
using PCL2Swap.Api;

namespace PCL2Swap.SampleAddon
{
    /// <summary>
    /// Reference addon: records its lifecycle and subscribes to the host event bus, so both the
    /// package loader and the semantic events can be asserted end to end (unpack → manifest check
    /// → ISwapAddon discovery → OnLoad/OnEnable → events). Doubles as the packaging example the
    /// addon-format documentation points at.
    /// </summary>
    public sealed class SampleAddon : ISwapAddon
    {
        public static int LoadCalls;
        public static int EnableCalls;
        public static int DisableCalls;
        public static int UnloadCalls;
        public static int DialogEvents;
        public static int UpdateEvents;
        public static int DownloadEvents;
        public static string LastApiVersion;
        public static string LastDialogTitle;

        private ISwapApi _api;

        public void OnLoad(SwapContext context)
        {
            LoadCalls++;
            _api = context?.Api;
            LastApiVersion = _api?.Host?.ApiVersion;
            _api?.Data?.Set("loaded", "1");
            _api?.Data?.Save();
        }

        public void OnEnable()
        {
            EnableCalls++;
            var hooks = _api?.Hooks;
            if (hooks == null) return;

            hooks.DialogOpening += OnDialogOpening;
            hooks.UpdateRequested += OnUpdateRequested;
            hooks.DownloadCompleted += OnDownloadCompleted;
            _api.Log("示例 Addon 已订阅事件总线（DialogOpening / UpdateRequested / DownloadCompleted）。");
        }

        public void OnDisable()
        {
            DisableCalls++;
            var hooks = _api?.Hooks;
            if (hooks == null) return;

            hooks.DialogOpening -= OnDialogOpening;
            hooks.UpdateRequested -= OnUpdateRequested;
            hooks.DownloadCompleted -= OnDownloadCompleted;
        }

        public void OnUnload() => UnloadCalls++;

        private void OnDialogOpening(object sender, DialogEventArgs args)
        {
            DialogEvents++;
            LastDialogTitle = args?.Title;
            _api?.Log($"示例 Addon 收到弹窗：{args?.Title}（warn={args?.IsWarn}，forceWait={args?.ForceWait}，" +
                      $"已追加取消={args?.CancelAdded}）");
        }

        private void OnUpdateRequested(object sender, UpdateEventArgs args)
        {
            UpdateEvents++;
            _api?.Log($"示例 Addon 收到更新请求：silent={args?.Silent}，地址数={args?.Urls?.Length ?? 0}");
        }

        private void OnDownloadCompleted(object sender, DownloadEventArgs args)
        {
            DownloadEvents++;
            _api?.Log($"示例 Addon 收到下载完成：地址数={args?.Urls?.Length ?? 0}，本地={args?.LocalPath}");
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
