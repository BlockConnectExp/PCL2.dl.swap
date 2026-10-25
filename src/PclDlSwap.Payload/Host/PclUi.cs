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

        private static Assembly Host => Assembly.GetEntryAssembly();

        private static Type HostType(string name) => Host?.GetType(name, throwOnError: false);

        /// <summary>Runs the body on PCL's UI thread (no-op when already there).</summary>
        public static T OnUiThread<T>(Func<T> body, T fallback)
        {
            try
            {
                var application = Type.GetType("System.Windows.Application, PresentationFramework");
                var current = application?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                var dispatcher = current?.GetType().GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current, null);
                if (dispatcher == null)
                    return body();

                var checkAccess = dispatcher.GetType().GetMethod("CheckAccess", Type.EmptyTypes);
                if (checkAccess != null && (bool)checkAccess.Invoke(dispatcher, null))
                    return body();

                var invoke = dispatcher.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Invoke" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Delegate));
                if (invoke == null)
                    return body();

                Func<T> wrapped = body;
                return (T)invoke.Invoke(dispatcher, new object[] { wrapped });
            }
            catch (Exception ex)
            {
                SwapLog.Warn("PclUi.OnUiThread 失败：" + ex.Message);
                try { return body(); } catch { return fallback; }
            }
        }

        public static void OnUiThread(Action body)
        {
            OnUiThread(() => { body(); return true; }, true);
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
