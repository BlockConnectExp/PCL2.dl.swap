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
        private readonly PclAccess _pcl;

        public HostApi(SwapShape shape, Assembly host)
        {
            _host = host ?? Assembly.GetEntryAssembly();
            Host = new HostInfoImpl(shape, _host);
            _hooks = new HookBusImpl(_host);
            _ui = new UiImpl();
            _reflect = new ReflectionImpl(_host);
            _pcl = new PclAccess(_host);
        }

        public IHostInfo Host { get; }
        public IHooks Hooks => _hooks;
        public IUi Ui => _ui;
        public IReflection Reflect => _reflect;
        public ISwapSurface Surface => _surface;
        public IPclAccess Pcl => _pcl;

        public IData Data { get; private set; }

        internal void AttachData(AddonDataStore store) => Data = store;

        // Host-wide event surface for the payload's own features; addons subscribe through IHooks.
        internal void RaiseDialogOpening(DialogEventArgs args) => _hooks.RaiseDialogOpening(args);
        internal void RaiseUpdateRequested(UpdateEventArgs args) => _hooks.RaiseUpdateRequested(args);
        internal void RaiseDownloadCompleted(DownloadEventArgs args) => _hooks.RaiseDownloadCompleted(args);

        /// <summary>Scoped attach used by <c>ScopedApi</c>: the hook belongs to one addon.</summary>
        internal IDisposable AttachAddonHook(string addonId, string declaringType, string methodName,
            string hookId, HookMode mode, Delegate handler) =>
            _hooks.AttachScoped(addonId, declaringType, methodName, hookId, mode, handler);

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
                var target = ResolveTarget(declaringType, methodName);
                if (handler.Target != null)
                    throw new NotSupportedException("当前版本只支持静态 handler（实例委托包装在路线图中）。");

                var detour = DetourInstaller.Install(target, handler.Method);
                SwapLog.Info($"[hookbus] {(string.IsNullOrEmpty(hookId) ? methodName : hookId)} -> " +
                             $"{declaringType}.{methodName}（{mode}）已安装");
                return new DetourToken(detour);
            }

            /// <summary>
            /// Module-scoped attach: the addon's own delegate runs behind a generated gate that
            /// follows the module toggle, so a disabled module stops hooking immediately and a
            /// disposed token forwards to the original from then on.
            /// </summary>
            internal IDisposable AttachScoped(string addonId, string declaringType, string methodName,
                string hookId, HookMode mode, Delegate handler)
            {
                if (handler == null) throw new ArgumentNullException(nameof(handler));
                var target = ResolveTarget(declaringType, methodName);
                ValidateHandler(target, handler);
                return ScopedHook.Attach(addonId, hookId, mode, handler, target);
            }

            /// <summary>
            /// Fail fast on a handler whose shape cannot be called with the target's ABI. Without
            /// this the mismatch only surfaces inside the gate, as a bare
            /// "TargetParameterCountException" on whichever launcher code path happens to hit it.
            /// Rules: an instance target passes the receiver first (object or the declaring type);
            /// value-typed parameters must match exactly; reference parameters may be widened to
            /// any base type or interface.
            /// </summary>
            private static void ValidateHandler(MethodInfo target, Delegate handler)
            {
                var declared = target.GetParameters();
                var offset = target.IsStatic ? 0 : 1;
                var actual = handler.Method.GetParameters();

                if (actual.Length != declared.Length + offset)
                {
                    throw new ArgumentException(
                        $"钩子 handler 参数个数不匹配：{handler.Method.Name} 需要 " +
                        $"{actual.Length} 个参数，但 {target.DeclaringType?.Name}.{target.Name} " +
                        $"需要 {(target.IsStatic ? string.Empty : "接收者 + ")}{declared.Length} 个。");
                }

                for (var i = 0; i < actual.Length; i++)
                {
                    var want = i < offset
                        ? target.DeclaringType ?? typeof(object)
                        : declared[i - offset].ParameterType;
                    var have = actual[i].ParameterType;
                    var compatible = want.IsValueType
                        ? have == want
                        : have.IsAssignableFrom(want);
                    if (compatible) continue;

                    throw new ArgumentException(
                        $"钩子 handler 第 {i + 1} 个参数类型不匹配：期望 {want.Name}" +
                        (want.IsValueType ? "（值类型必须一致）" : "（或其基类/接口）") +
                        $"，实际是 {have.Name}。");
                }
            }

            private MethodInfo ResolveTarget(string declaringType, string methodName)
            {
                var type = _host?.GetType(declaringType, throwOnError: false);
                var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Static | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName);
                if (target == null)
                    throw new InvalidOperationException($"宿主方法不存在：{declaringType}.{methodName}");
                return target;
            }

            public bool IsHooked(string declaringType, string methodName)
            {
                var type = _host?.GetType(declaringType, throwOnError: false);
                var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Static | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName);
                return target != null && DetourInstaller.IsHooked(target);
            }

            // Raised by the host pipelines that already see every dialog / update / download.
            // Each subscriber runs on its own so a broken addon cannot take the bus (or the
            // launcher) down with it.
            internal void RaiseDownloadCompleted(DownloadEventArgs args) =>
                RaiseSafely(DownloadCompleted, args, nameof(DownloadCompleted));

            internal void RaiseDialogOpening(DialogEventArgs args) =>
                RaiseSafely(DialogOpening, args, nameof(DialogOpening));

            internal void RaiseUpdateRequested(UpdateEventArgs args) =>
                RaiseSafely(UpdateRequested, args, nameof(UpdateRequested));

            private void RaiseSafely<T>(EventHandler<T> handlers, T args, string name) where T : EventArgs
            {
                if (handlers == null) return;
                foreach (var subscriber in handlers.GetInvocationList())
                {
                    try
                    {
                        ((EventHandler<T>)subscriber)(this, args);
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Error($"[hookbus] {name} 订阅者异常（已隔离，其余订阅者继续）：" + ex.Message, ex);
                    }
                }
            }
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

            // Native-look builders shared with the built-in pages (Views.SwapUi owns the details).
            public System.Windows.FrameworkElement Card(string title,
                params System.Windows.FrameworkElement[] content) => Views.SwapUi.Card(title, content);

            public System.Windows.FrameworkElement Text(string text, double size = 12.5, bool secondary = false) =>
                Views.SwapUi.Text(text, size, secondary);

            public System.Windows.FrameworkElement Button(string text, Action onClick, string colorType = null) =>
                Views.SwapUi.Button(text, onClick, colorType);

            public System.Windows.FrameworkElement InfoRow(string label, string value) =>
                Views.SwapUi.InfoRow(label, value);

            public System.Windows.FrameworkElement Buttons(
                params System.Windows.FrameworkElement[] buttons) => Views.SwapUi.Buttons(buttons);

            public System.Windows.FrameworkElement Note(string text) => Views.SwapUi.Note(text);

            public System.Windows.FrameworkElement CheckBox(string label, bool initial,
                Action<bool> onChange) => Views.SwapUi.CheckBox(label, initial, onChange);

            public System.Windows.FrameworkElement Combo(string label,
                System.Collections.Generic.IReadOnlyList<string> options, int selectedIndex,
                Action<int> onChange) => Views.SwapUi.Combo(label, options, selectedIndex, onChange);
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
