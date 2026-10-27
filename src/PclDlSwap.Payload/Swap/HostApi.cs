using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// The ISwapApi implementation handed to every addon. Thin wrappers over the payload's
    /// existing primitives (PclUi reflection bridge, DetourInstaller, SwapLog) so addon code sees
    /// one readable surface instead of the raw internals.
    /// </summary>
    internal sealed class HostApi : ISwapApi
    {
        public const string ApiVersion = "1.0.0";

        private readonly Assembly _host;
        private readonly HookBusImpl _hooks;
        private readonly UiImpl _ui;
        private readonly ReflectionImpl _reflect;
        private readonly SurfaceIndex _surface = new SurfaceIndex();

        public HostApi(SwapShape shape, Assembly host)
        {
            _host = host ?? Assembly.GetEntryAssembly();
            Host = new HostInfoImpl(shape, _host);
            _hooks = new HookBusImpl(_host);
            _ui = new UiImpl();
            _reflect = new ReflectionImpl(_host);
        }

        public IHostInfo Host { get; }
        public IHooks Hooks => _hooks;
        public IUi Ui => _ui;
        public IReflection Reflect => _reflect;
        public ISwapSurface Surface => _surface;

        public IData Data { get; private set; }

        internal void AttachData(AddonDataStore store) => Data = store;

        public void Log(string message) => SwapLog.Info("[addon] " + message);
        public void LogWarning(string message) => SwapLog.Warn("[addon] " + message);
        public void LogError(string message, Exception exception = null) =>
            SwapLog.Error("[addon] " + message + (exception == null ? string.Empty : " | " + exception));

        private sealed class HostInfoImpl : IHostInfo
        {
            public HostInfoImpl(SwapShape shape, Assembly host)
            {
                Shape = shape;
                PclAssembly = host;
            }

            public string SwapVersion => SwapLog.ToolVersion;
            public string ApiVersion => HostApi.ApiVersion;
            public SwapShape Shape { get; }
            public int ProcessId => System.Diagnostics.Process.GetCurrentProcess().Id;
            public string LogDirectory => Path.GetDirectoryName(SwapLog.LogPath) ?? string.Empty;
            public string AddonDirectory => SwapPaths.AddonsDirectory;
            public object PclAssembly { get; }
        }

        private sealed class HookBusImpl : IHooks
        {
            private readonly Assembly _host;

            public HookBusImpl(Assembly host) => _host = host;

            public event EventHandler<DownloadEventArgs> DownloadCompleted;
            public event EventHandler<DialogEventArgs> DialogOpening;
            public event EventHandler<UpdateEventArgs> UpdateRequested;

            public IDisposable Attach(string declaringType, string methodName, string hookId,
                Delegate handler, HookMode mode)
            {
                if (handler == null) throw new ArgumentNullException(nameof(handler));
                var type = _host?.GetType(declaringType, throwOnError: false);
                var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Static | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName);
                if (target == null)
                    throw new InvalidOperationException($"宿主方法不存在：{declaringType}.{methodName}");
                if (handler.Target != null)
                    throw new NotSupportedException("当前版本只支持静态 handler（实例委托包装在路线图中）。");

                var detour = DetourInstaller.Install(target, handler.Method);
                SwapLog.Info($"[hookbus] {(string.IsNullOrEmpty(hookId) ? methodName : hookId)} -> " +
                             $"{declaringType}.{methodName}（{mode}）已安装");
                return new DetourToken(detour);
            }

            public bool IsHooked(string declaringType, string methodName)
            {
                var type = _host?.GetType(declaringType, throwOnError: false);
                var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Static | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName);
                return target != null && DetourInstaller.IsHooked(target);
            }

            // Raised by the built-in addons once they own the corresponding pipeline.
            internal void RaiseDownloadCompleted(DownloadEventArgs args) => DownloadCompleted?.Invoke(this, args);
            internal void RaiseDialogOpening(DialogEventArgs args) => DialogOpening?.Invoke(this, args);
            internal void RaiseUpdateRequested(UpdateEventArgs args) => UpdateRequested?.Invoke(this, args);
        }

        private sealed class DetourToken : IDisposable
        {
            private readonly DetourInstaller _detour;
            private bool _disposed;

            public DetourToken(DetourInstaller detour) => _detour = detour;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                // Hot-unloading a detour is not supported yet (the trampoline would have to be
                // restored); the token exists so addons can express lifecycle intent today.
                SwapLog.Warn("[hookbus] detour 卸载尚未支持，句柄保持安装状态。");
            }
        }

        private sealed class UiImpl : IUi
        {
            public void RegisterSettingsPage(string id, string title, Func<System.Windows.FrameworkElement> viewFactory) =>
                PageRegistry.RegisterPage(id, title, viewFactory);

            public void Hint(string text) => PclUi.Hint(text);

            public int MsgBox(string caption, string title, string button1 = "确定", string button2 = "") =>
                PclUi.MsgBox(caption, title, button1, button2);
        }

        private sealed class ReflectionImpl : IReflection
        {
            private readonly Assembly _host;

            public ReflectionImpl(Assembly host) => _host = host;

            public Type HostType(string fullName) => _host?.GetType(fullName, throwOnError: false);
            public object GetMember(object instanceOrType, string name) => PclUi.GetMember(instanceOrType, name);
            public void SetMember(object instanceOrType, string name, object value) =>
                PclUi.SetMember(instanceOrType, name, value);
            public object Call(object instanceOrType, string name, params object[] args) =>
                PclUi.Invoke(instanceOrType, name, args);
        }
    }

    /// <summary>Simple per-addon key/value store persisted next to the addon.</summary>
    internal sealed class AddonDataStore : IData
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public AddonDataStore(string addonId)
        {
            var directory = SwapPaths.AddonDirectory(addonId);
            try { Directory.CreateDirectory(directory); } catch { }
            _path = Path.Combine(directory, "config.json");
            TryLoad();
        }

        public string Get(string key, string fallback = null) =>
            key != null && _values.TryGetValue(key, out var value) ? value : fallback;

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _values[key] = value ?? string.Empty;
        }

        public void Save()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(_path, serializer.Serialize(_values), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                SwapLog.Warn($"Addon 配置写入失败（{_path}）：" + ex.Message);
            }
        }

        private void TryLoad()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var serializer = new JavaScriptSerializer();
                var map = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_path));
                if (map == null) return;
                foreach (var pair in map) _values[pair.Key] = Convert.ToString(pair.Value);
            }
            catch (Exception ex)
            {
                SwapLog.Warn($"Addon 配置读取失败（{_path}）：" + ex.Message);
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
