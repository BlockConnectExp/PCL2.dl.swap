using System;
using System.Linq;
using System.Reflection;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Thin bridge that raises prompts through PCL's own dialog / hint system instead of the Win32
    /// message box, so every message the swap produces looks and behaves like a native PCL prompt.
    /// Falls back to <see cref="Dialogs"/> only when the host UI is not reachable yet.
    /// </summary>
    internal static class PclUi
    {
        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static readonly object CacheLock = new object();
        private static Type _applicationType;
        private static Type _dispatcherType;
        private static MethodInfo _checkAccessMethod;
        private static MethodInfo _invokeWithArgs;
        private static MethodInfo _invokeAction;

        private static Assembly Host => Assembly.GetEntryAssembly();

        private static Type HostType(string name) => Host?.GetType(name, throwOnError: false);

        /// <summary>Runs the body on PCL's UI thread (no-op when already there).</summary>
        public static T OnUiThread<T>(Func<T> body, T fallback)
        {
            try
            {
                var dispatcher = TryGetDispatcher();
                if (dispatcher == null)
                    return body();

                EnsureDispatcherMethods(dispatcher.GetType());
                var checkAccess = GetCheckAccessMethod();
                if (checkAccess != null && (bool)checkAccess.Invoke(dispatcher, null))
                    return body();

                return InvokeOnDispatcher(dispatcher, body, fallback);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("PclUi.OnUiThread 失败：" + ex.Message);
                return fallback;
            }
        }

        /// <summary>
        /// PCL is a WPF application, so its own Dispatcher is the marshalling point. The first
        /// version looked for an "Invoke(Delegate)" single-parameter overload - WPF has no such
        /// overload (the real shape is "Invoke(Delegate, params object[])"), the lookup returned
        /// null and the body silently ran on the polling thread. The runtime evidence is in
        /// %TEMP%\PCL2Swap\v26.0-Alpha4\pcl2swap.log: the About-page insert and the 2.99.0.1
        /// sidebar repair both died with "调用线程无法访问此对象".
        /// </summary>
        private static object TryGetDispatcher()
        {
            var application = ResolveApplicationType();
            var current = application?
                .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null, null);
            return current?
                .GetType().GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(current, null);
        }

        /// <summary>
        /// "System.Windows.Application, PresentationFramework" does not bind from inside the injected
        /// payload: a named Type.GetType runs against the payload's LoadFrom context and returns null
        /// even while the assembly sits loaded in the process (the uitest hit exactly this). Walk the
        /// loaded assemblies first, and keep Assembly.Load as a last resort.
        /// </summary>
        private static Type ResolveApplicationType()
        {
            lock (CacheLock)
            {
                if (_applicationType != null) return _applicationType;
            }

            var resolved = FindApplicationType();
            if (resolved != null)
            {
                lock (CacheLock) { _applicationType = resolved; }
            }
            return resolved;
        }

        private static Type FindApplicationType()
        {
            var type = Type.GetType("System.Windows.Application, PresentationFramework", throwOnError: false);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name != "PresentationFramework") continue;
                    type = assembly.GetType("System.Windows.Application", throwOnError: false);
                    if (type != null) return type;
                }
                catch
                {
                    // dynamic or reflection-only assemblies can throw from GetName/GetType
                }
            }

            try
            {
                return Assembly.Load("PresentationFramework")
                    ?.GetType("System.Windows.Application", throwOnError: false);
            }
            catch
            {
                return null;
            }
        }

        private static T InvokeOnDispatcher<T>(object dispatcher, Func<T> body, T fallback)
        {
            EnsureDispatcherMethods(dispatcher.GetType());
            var withArgs = GetInvokeWithArgs();
            var actionInvoke = GetInvokeAction();

            // WPF: public object Invoke(Delegate method, params object[] args)
            if (withArgs != null)
            {
                var result = withArgs.Invoke(dispatcher, new object[] { body, new object[0] });
                if (result is T) return (T)result;
                return result == null ? default(T) : fallback;
            }

            // WPF: public void Invoke(Action callback)
            if (actionInvoke != null)
            {
                var result = fallback;
                Exception failure = null;
                Action action = () =>
                {
                    try { result = body(); }
                    catch (Exception ex) { failure = ex; }
                };
                actionInvoke.Invoke(dispatcher, new object[] { action });
                if (failure != null) throw failure;
                return result;
            }

            SwapLog.Warn("PclUi.OnUiThread：Dispatcher 上没有可用的 Invoke 重载，跳过该次 UI 更新以避免跨线程访问。");
            return fallback;
        }

        // Reflection lookups on the dispatcher are cached per type; OnUiThread sits on the pollers'
        // hot path (every 300 ms until a page repair lands).
        private static void EnsureDispatcherMethods(Type dispatcherType)
        {
            lock (CacheLock)
            {
                if (_dispatcherType == dispatcherType) return;
                var methods = dispatcherType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                _checkAccessMethod = dispatcherType.GetMethod("CheckAccess", Type.EmptyTypes);
                _invokeWithArgs = methods.FirstOrDefault(m =>
                    m.Name == "Invoke" &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(Delegate) &&
                    m.GetParameters()[1].ParameterType == typeof(object[]));
                _invokeAction = methods.FirstOrDefault(m =>
                    m.Name == "Invoke" &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Action) &&
                    m.ReturnType == typeof(void));
                _dispatcherType = dispatcherType;
            }
        }

        private static MethodInfo GetCheckAccessMethod()
        {
            lock (CacheLock) { return _checkAccessMethod; }
        }

        private static MethodInfo GetInvokeWithArgs()
        {
            lock (CacheLock) { return _invokeWithArgs; }
        }

        private static MethodInfo GetInvokeAction()
        {
            lock (CacheLock) { return _invokeAction; }
        }

        public static void OnUiThread(Action body)
        {
            if (!OnUiThread(() => { body(); return true; }, true))
                SwapLog.Warn("PclUi.OnUiThread：这次 UI 更新未能 marshal 到 Dispatcher，已跳过。");
        }

        /// <summary>
        /// PCL's own message box. Returns 1/2/3 for the pressed button (0 when unavailable).
        /// </summary>
        public static int MsgBox(string caption, string title, string button1,
            string button2 = "", string button3 = "", bool isWarn = false, bool highLight = true,
            bool forceWait = false)
        {
            return OnUiThread(() =>
            {
                try
                {
                    var type = HostType("PCL.ModMain");
                    var method = type?.GetMethods(AnyMember)
                        .FirstOrDefault(m => m.Name == "MyMsgBox" && m.GetParameters().Length >= 8 &&
                                             m.GetParameters()[0].ParameterType == typeof(string));
                    if (method == null)
                    {
                        SwapLog.Warn("PclUi：未找到 ModMain.MyMsgBox，退回 Win32 弹窗。");
                        return Fallback(caption, title, button1, button2, button3, isWarn);
                    }

                    var parameters = method.GetParameters();
                    var args = new object[parameters.Length];
                    args[0] = caption;
                    args[1] = title;
                    args[2] = button1;
                    args[3] = button2;
                    args[4] = button3;
                    args[5] = isWarn;
                    args[6] = highLight;
                    args[7] = forceWait;
                    for (int i = 8; i < args.Length; i++) args[i] = null;
                    return Convert.ToInt32(method.Invoke(null, args) ?? 0);
                }
                catch (Exception ex)
                {
                    SwapLog.Error("PclUi.MsgBox 失败：" + ex.Message);
                    return Fallback(caption, title, button1, button2, button3, isWarn);
                }
            }, 0);
        }

        private static int Fallback(string caption, string title, string button1, string button2,
            string button3, bool isWarn)
        {
            if (string.IsNullOrEmpty(button2) && string.IsNullOrEmpty(button3))
            {
                Dialogs.Info(title, caption);
                return 1;
            }
            return Dialogs.Confirm(title, caption, button1, button2, isWarn);
        }

        /// <summary>PCL's non-blocking hint banner.</summary>
        public static void Hint(string text, string hintTypeName = "Blue")
        {
            OnUiThread(() =>
            {
                try
                {
                    var type = HostType("PCL.ModMain");
                    var method = type?.GetMethods(AnyMember)
                        .FirstOrDefault(m => m.Name == "Hint" && m.GetParameters().Length >= 1 &&
                                             m.GetParameters()[0].ParameterType == typeof(string));
                    if (method == null) return;
                    var parameters = method.GetParameters();
                    var args = new object[parameters.Length];
                    args[0] = text;
                    if (parameters.Length > 1 && parameters[1].ParameterType.IsEnum)
                        args[1] = Enum.Parse(parameters[1].ParameterType, hintTypeName, ignoreCase: true);
                    for (int i = 2; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                    method.Invoke(null, args);
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("PclUi.Hint 失败：" + ex.Message);
                }
            });
        }

        public static void OpenWebsite(string url)
        {
            try
            {
                var type = HostType("PCL.ModBase");
                var method = type?.GetMethods(AnyMember)
                    .FirstOrDefault(m => m.Name == "OpenWebsite" && m.GetParameters().Length == 1);
                if (method != null)
                {
                    method.Invoke(null, new object[] { url });
                    return;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("PclUi.OpenWebsite 失败：" + ex.Message);
            }

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { SwapLog.Warn("OpenWebsite 回退失败：" + ex.Message); }
        }

        // ---- small reflection helpers shared by the features ----

        /// <summary>
        /// Reads a member by name: plain fields first, then properties. PCL mixes both shapes
        /// (VB.NET exposes many members as "virtual properties" whose backing field is the only
        /// thing a field-only lookup would find), and a field-only lookup silently returns null
        /// for them - which is how nulls used to reach PageLoaderInit and the About page.
        /// </summary>
        public static object GetMember(object instance, string name)
        {
            if (instance == null) return null;
            var type = instance as Type ?? instance.GetType();
            var target = instance as Type == null ? instance : null;

            var field = type.GetField(name, AnyMember);
            if (field != null) return field.GetValue(target);

            var property = type.GetProperty(name, AnyMember);
            if (property != null && property.CanRead) return property.GetValue(target, null);

            return null;
        }

        /// <summary>Writes a member by name: plain fields first, then writable properties.</summary>
        public static void SetMember(object instance, string name, object value)
        {
            var type = instance.GetType();
            var field = type.GetField(name, AnyMember);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            var property = type.GetProperty(name, AnyMember);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return;
            }

            throw new MissingMemberException(type.FullName, name);
        }

        public static void SetProperty(object instance, string name, object value)
        {
            var type = instance.GetType();
            var property = type.GetProperty(name, AnyMember);
            if (property == null || !property.CanWrite) throw new MissingMemberException(type.FullName, name);
            property.SetValue(instance, value, null);
        }

        public static object Invoke(object instance, string name, params object[] args)
        {
            var type = instance as Type ?? instance.GetType();
            var target = instance as Type == null ? instance : null;
            var method = type.GetMethods(AnyMember)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method.Invoke(target, args);
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
