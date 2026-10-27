using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// IPclAccess implementation: typed wrappers over PCL's Secret / Net / Animation subsystems.
    /// Calls go through the host's real methods (so the swap's own hooks still run); members that
    /// are missing on a build throw MissingMethodException, except the animation gate which
    /// degrades silently as documented.
    /// </summary>
    internal sealed class PclAccess : IPclAccess
    {
        public PclAccess(Assembly host)
        {
            host = host ?? Assembly.GetEntryAssembly();
            Secret = new SecretAccess(host);
            Net = new NetAccess(host);
            Animation = new AnimationAccess(host);
        }

        public IPclSecretAccess Secret { get; }
        public IPclNetAccess Net { get; }
        public IPclAnimationAccess Animation { get; }

        // ------------------------------------------------------------------ helpers

        internal static MethodInfo FindMethod(Assembly host, string typeName, string methodName,
            Func<MethodInfo, bool> filter = null)
        {
            var type = host?.GetType(typeName, throwOnError: false);
            return type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == methodName)
                .FirstOrDefault(method => filter == null || filter(method));
        }

        internal static void InvokeWithDefaults(MethodInfo method, object[] provided)
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = i < provided.Length ? provided[i] : DefaultFor(parameters[i]);
            }
            method.Invoke(null, args);
        }

        internal static object DefaultFor(ParameterInfo parameter)
        {
            try
            {
                if (parameter.HasDefaultValue && parameter.DefaultValue != null &&
                    parameter.DefaultValue != DBNull.Value && parameter.DefaultValue != Type.Missing)
                    return parameter.DefaultValue;
            }
            catch
            {
                // some signatures expose unusable default metadata; fall through to the type default
            }
            return parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
        }

        private sealed class SecretAccess : IPclSecretAccess
        {
            private readonly Assembly _host;

            public SecretAccess(Assembly host) => _host = host;

            public void UpdateStart(bool silent, IReadOnlyList<string> urlsOverride = null)
            {
                var method = FindMethod(_host, "PCL.ModSecret", "UpdateStart",
                    m => m.GetParameters().Length == 2);
                if (method == null) throw new MissingMethodException("PCL.ModSecret.UpdateStart");
                method.Invoke(null, new object[] { silent, urlsOverride == null ? null : urlsOverride.ToArray() });
            }

            public bool HasUpdatePermission
            {
                get
                {
                    var method = FindMethod(_host, "PCL.ModSecret", "get_HasUpdatePermission");
                    if (method == null) throw new MissingMethodException("PCL.ModSecret.get_HasUpdatePermission");
                    return Convert.ToBoolean(method.Invoke(null, null));
                }
            }

            public void UpdateCheckByButton()
            {
                var method = FindMethod(_host, "PCL.ModSecret", "UpdateCheckByButton");
                if (method == null) throw new MissingMethodException("PCL.ModSecret.UpdateCheckByButton");
                InvokeWithDefaults(method, new object[0]);
            }
        }

        private sealed class NetAccess : IPclNetAccess
        {
            private readonly Assembly _host;

            public NetAccess(Assembly host) => _host = host;

            public void DownloadByLoader(IReadOnlyList<string> urls, string localPath,
                object loader = null, object checker = null, bool simulateBrowserHeaders = false)
            {
                var method = FindMethod(_host, "PCL.ModNet", "NetDownloadByLoader",
                    m => m.GetParameters().Length == 5 &&
                         m.GetParameters()[0].ParameterType != typeof(string));
                if (method == null) throw new MissingMethodException("PCL.ModNet.NetDownloadByLoader(IEnumerable)");
                method.Invoke(null, new object[]
                {
                    urls == null ? null : urls.ToArray(),
                    localPath,
                    loader,
                    checker,
                    simulateBrowserHeaders
                });
            }

            public void DownloadByClient(string url, string localPath, bool simulateBrowserHeaders = false)
            {
                var method = FindMethod(_host, "PCL.ModNet", "NetDownloadByClient",
                    m => m.GetParameters().Length == 3);
                if (method == null) throw new MissingMethodException("PCL.ModNet.NetDownloadByClient");
                method.Invoke(null, new object[] { url, localPath, simulateBrowserHeaders });
            }
        }

        private sealed class AnimationAccess : IPclAnimationAccess
        {
            private readonly Assembly _host;

            public AnimationAccess(Assembly host) => _host = host;

            public int ControlEnabled
            {
                get
                {
                    var type = _host?.GetType("PCL.ModAnimation", throwOnError: false);
                    if (type == null) return 0;
                    try { return Convert.ToInt32(PclUi.GetMember(type, "AniControlEnabled") ?? 0); }
                    catch { return 0; }
                }
                set
                {
                    var type = _host?.GetType("PCL.ModAnimation", throwOnError: false);
                    if (type == null)
                    {
                        SwapLog.Warn("PclAccess.Animation：宿主缺少 ModAnimation，写入被忽略。");
                        return;
                    }
                    try { PclUi.SetMember(type, "AniControlEnabled", value); }
                    catch (Exception ex) { SwapLog.Warn("PclAccess.Animation：写入门控失败：" + ex.Message); }
                }
            }

            public void Stop(string id)
            {
                var method = FindMethod(_host, "PCL.ModAnimation", "AniStop",
                    m => m.GetParameters().Length >= 1 &&
                         m.GetParameters()[0].ParameterType == typeof(string));
                if (method == null) throw new MissingMethodException("PCL.ModAnimation.AniStop");
                InvokeWithDefaults(method, new object[] { id });
            }

            public void Start(object animationData, string id)
            {
                var method = FindMethod(_host, "PCL.ModAnimation", "AniStart",
                    m => m.GetParameters().Length >= 2 &&
                         m.GetParameters()[1].ParameterType == typeof(string));
                if (method == null) throw new MissingMethodException("PCL.ModAnimation.AniStart");
                InvokeWithDefaults(method, new object[] { animationData, id });
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
