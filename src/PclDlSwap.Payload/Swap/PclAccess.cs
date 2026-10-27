using System;
using System.Collections;
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
            Settings = new SettingsAccess(host);
            Base = new BaseAccess(host);
            Instance = new InstanceAccess(host);
            Download = new DownloadAccess(host);
            Launch = new LaunchAccess(host);
            Loader = new LoaderAccess(host);
        }

        public IPclSecretAccess Secret { get; }
        public IPclNetAccess Net { get; }
        public IPclAnimationAccess Animation { get; }
        public IPclSettingsAccess Settings { get; }
        public IPclBaseAccess Base { get; }
        public IPclInstanceAccess Instance { get; }
        public IPclDownloadAccess Download { get; }
        public IPclLaunchAccess Launch { get; }
        public IPclLoaderAccess Loader { get; }

        // ------------------------------------------------------------------ helpers

        internal static MethodInfo FindMethod(Assembly host, string typeName, string methodName,
            Func<MethodInfo, bool> filter = null)
        {
            var type = host?.GetType(typeName, throwOnError: false);
            return type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == methodName)
                .FirstOrDefault(method => filter == null || filter(method));
        }

        /// <summary>Reads a static field or property (PCL mixes both shapes).</summary>
        internal static object GetStatic(Assembly host, string typeName, string memberName)
        {
            var type = host?.GetType(typeName, throwOnError: false);
            if (type == null) throw new MissingMemberException(typeName);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var field = type.GetField(memberName, flags);
            if (field != null) return field.GetValue(null);
            var property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead) return property.GetValue(null, null);
            throw new MissingMemberException(typeName, memberName);
        }

        internal static void SetStatic(Assembly host, string typeName, string memberName, object value)
        {
            var type = host?.GetType(typeName, throwOnError: false);
            if (type == null) throw new MissingMemberException(typeName);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var field = type.GetField(memberName, flags);
            if (field != null) { field.SetValue(null, value); return; }
            var property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite) { property.SetValue(null, value, null); return; }
            throw new MissingMemberException(typeName, memberName);
        }

        /// <summary>
        /// Reads an instance member by name across the shapes PCL uses: plain fields, VB.NET
        /// properties and parameterless methods (McInstance exposes both GetJsonPath() and
        /// VersionDisplayName()).
        /// </summary>
        internal static object ReadMember(object instance, string name)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = instance.GetType();

            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(instance);
            var property = type.GetProperty(name, flags);
            if (property != null && property.CanRead) return property.GetValue(instance, null);
            var method = type.GetMethods(flags)
                .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == 0);
            if (method != null) return method.Invoke(instance, null);
            throw new MissingMemberException(type.FullName, name);
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

        private sealed class SettingsAccess : IPclSettingsAccess
        {
            private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            private readonly Assembly _host;

            public SettingsAccess(Assembly host) => _host = host;

            public string Get(string key)
            {
                var type = HostType();
                var plain = type.GetMethods(Flags).FirstOrDefault(method =>
                    method.Name == "Get" && !method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
                if (plain != null) return Convert.ToString(plain.Invoke(null, new object[] { key, null }));

                var generic = type.GetMethods(Flags).FirstOrDefault(method =>
                    method.Name == "Get" && method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
                if (generic == null) throw new MissingMethodException("PCL.Settings.Get");
                return Convert.ToString(generic.MakeGenericMethod(typeof(string))
                    .Invoke(null, new object[] { key, null }));
            }

            public void Set(string key, object value)
            {
                var type = HostType();
                var plain = type.GetMethods(Flags).FirstOrDefault(method =>
                    method.Name == "Set" && !method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 3 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
                if (plain != null)
                {
                    plain.Invoke(null, new[] { (object)key, value, null });
                    return;
                }

                var generic = type.GetMethods(Flags).FirstOrDefault(method =>
                    method.Name == "Set" && method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 3 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
                if (generic == null) throw new MissingMethodException("PCL.Settings.Set");
                generic.MakeGenericMethod(value?.GetType() ?? typeof(string))
                    .Invoke(null, new[] { (object)key, value, null });
            }

            public bool HasSaved(string key) => Convert.ToBoolean(InvokeWithKey("HasSaved", key) ?? false);
            public void Reset(string key) => InvokeWithKey("Reset", key);
            public object GetDefault(string key) => InvokeWithKey("GetDefault", key);

            private object InvokeWithKey(string name, string key)
            {
                var method = HostType().GetMethods(Flags).FirstOrDefault(candidate =>
                    candidate.Name == name && candidate.GetParameters().Length >= 1 &&
                    candidate.GetParameters()[0].ParameterType == typeof(string));
                if (method == null) throw new MissingMethodException("PCL.Settings." + name);

                var parameters = method.GetParameters();
                var args = new object[parameters.Length];
                args[0] = key;
                for (var i = 1; i < args.Length; i++) args[i] = DefaultFor(parameters[i]);
                return method.Invoke(null, args);
            }

            private Type HostType()
            {
                var type = _host?.GetType("PCL.Settings", throwOnError: false);
                if (type == null) throw new MissingMethodException("PCL.Settings");
                return type;
            }
        }

        private sealed class BaseAccess : IPclBaseAccess
        {
            private readonly Assembly _host;

            public BaseAccess(Assembly host) => _host = host;

            public string GetUuid() => Convert.ToString(Invoke("GetUuid", new object[0]));

            public string ReadIni(string file, string section, string key) =>
                Convert.ToString(Invoke("ReadIni", new object[] { file, section, key }));

            public void WriteIni(string file, string section, string key, string value) =>
                Invoke("WriteIni", new object[] { file, section, key, value });

            public bool HasIniKey(string file, string section, string key) =>
                Convert.ToBoolean(Invoke("HasIniKey", new object[] { file, section, key }) ?? false);

            public void DeleteIniKey(string file, string section, string key) =>
                Invoke("DeleteIniKey", new object[] { file, section, key });

            private object Invoke(string name, object[] args)
            {
                var type = _host?.GetType("PCL.ModBase", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(candidate => candidate.Name == name &&
                                                 candidate.GetParameters().Length == args.Length);
                if (method == null) throw new MissingMethodException("PCL.ModBase." + name);
                return method.Invoke(null, args);
            }
        }

        private sealed class InstanceAccess : IPclInstanceAccess
        {
            private readonly Assembly _host;

            public InstanceAccess(Assembly host) => _host = host;

            public string FolderSelected
            {
                get => Convert.ToString(GetStatic(_host, "PCL.ModMinecraft", "McFolderSelected"));
                set => SetStatic(_host, "PCL.ModMinecraft", "McFolderSelected", value);
            }

            public object InstanceSelected
            {
                get => GetStatic(_host, "PCL.ModMinecraft", "McInstanceSelected");
                set => SetStatic(_host, "PCL.ModMinecraft", "McInstanceSelected", value);
            }

            public string GetFriendlyName(string folderName)
            {
                var method = FindMethod(_host, "PCL.ModMinecraft", "GetMcFoolName",
                    m => m.GetParameters().Length == 1);
                if (method == null) throw new MissingMethodException("PCL.ModMinecraft.GetMcFoolName");
                return Convert.ToString(method.Invoke(null, new object[] { folderName }));
            }

            public IReadOnlyList<object> List(string folder = null)
            {
                var target = string.IsNullOrEmpty(folder)
                    ? Convert.ToString(GetStatic(_host, "PCL.ModMinecraft", "McFolderSelected"))
                    : folder;

                var method = FindMethod(_host, "PCL.ModMinecraft", "InitMcInstanceListWithCache",
                                 m => m.GetParameters().Length == 1)
                             ?? FindMethod(_host, "PCL.ModMinecraft", "McFolderListLoadSub",
                                 m => m.GetParameters().Length == 0);
                if (method == null)
                    throw new MissingMethodException("PCL.ModMinecraft.InitMcInstanceListWithCache");

                var result = method.Invoke(null, method.GetParameters().Length == 0
                    ? new object[0]
                    : new object[] { target });

                var instances = new List<object>();
                if (result is IDictionary grouped)
                {
                    foreach (var value in grouped.Values)
                    {
                        if (!(value is IEnumerable items)) continue;
                        foreach (var item in items) instances.Add(item);
                    }
                }
                return instances;
            }

            public string GetName(object instance) => Convert.ToString(ReadMember(instance, "Name"));
            public string GetVersion(object instance) => Convert.ToString(ReadMember(instance, "Version"));
            public string GetPathIndie(object instance) => Convert.ToString(ReadMember(instance, "PathIndie"));
            public bool IsModable(object instance) =>
                Convert.ToBoolean(ReadMember(instance, "Modable") ?? false);
            public string GetJsonPath(object instance) => Convert.ToString(ReadMember(instance, "GetJsonPath"));
            public string GetVersionDisplayName(object instance) =>
                Convert.ToString(ReadMember(instance, "VersionDisplayName"));

            public string GetNativesFolder(object instance)
            {
                var method = FindMethod(_host, "PCL.ModLaunch", "GetNativesFolder",
                    m => m.GetParameters().Length == 1);
                if (method == null) throw new MissingMethodException("PCL.ModLaunch.GetNativesFolder");
                return Convert.ToString(method.Invoke(null, new[] { instance }));
            }
        }

        private sealed class DownloadAccess : IPclDownloadAccess
        {
            private readonly Assembly _host;

            public DownloadAccess(Assembly host) => _host = host;

            public bool PreferMojang =>
                Convert.ToBoolean(GetStatic(_host, "PCL.ModDownload", "DlSourcePreferMojang") ?? false);

            public bool PreferMojangVersionList =>
                Convert.ToBoolean(GetStatic(_host, "PCL.ModDownload", "DlVersionListPreferMojang") ?? false);

            public object ClientListGet(string source) => Call("DlClientListGet", source);
            public IReadOnlyList<string> SourceAssetsGet(string version) => Strings("DlSourceAssetsGet", version);
            public IReadOnlyList<string> SourceLibraryGet(string version) => Strings("DlSourceLibraryGet", version);
            public IReadOnlyList<string> SourceLauncherOrMetaGet(string version) =>
                Strings("DlSourceLauncherOrMetaGet", version);

            public string SourceModGet(string project) =>
                Convert.ToString(Call("DlSourceModGet", project));

            public object ModRequest(string url, string method = "GET", string body = null,
                string contentType = null)
            {
                var info = FindMethod(_host, "PCL.ModDownload", "DlModRequest",
                    m => m.GetParameters().Length == 4);
                if (info == null) throw new MissingMethodException("PCL.ModDownload.DlModRequest");

                var httpMethodType = info.GetParameters()[1].ParameterType;
                var httpMethod = string.IsNullOrEmpty(method)
                    ? null
                    : Activator.CreateInstance(httpMethodType, new object[] { method });
                return info.Invoke(null, new[] { url, httpMethod, body, contentType });
            }

            private object Call(string name, string argument)
            {
                var method = FindMethod(_host, "PCL.ModDownload", name, m => m.GetParameters().Length == 1);
                if (method == null) throw new MissingMethodException("PCL.ModDownload." + name);
                return method.Invoke(null, new object[] { argument });
            }

            private IReadOnlyList<string> Strings(string name, string argument)
            {
                var list = new List<string>();
                if (Call(name, argument) is IEnumerable items)
                {
                    foreach (var item in items) list.Add(Convert.ToString(item));
                }
                return list;
            }
        }

        private sealed class LaunchAccess : IPclLaunchAccess
        {
            private readonly Assembly _host;

            public LaunchAccess(Assembly host) => _host = host;

            public bool Start(object launchOptions)
            {
                var method = FindMethod(_host, "PCL.ModLaunch", "McLaunchStart",
                    m => m.GetParameters().Length == 1 && m.ReturnType == typeof(bool));
                if (method == null) throw new MissingMethodException("PCL.ModLaunch.McLaunchStart");
                return Convert.ToBoolean(method.Invoke(null, new[] { launchOptions }) ?? false);
            }

            public string LoginName() => Convert.ToString(Call("McLoginName"));
            public string LoginAble() => Convert.ToString(Call("McLoginAble"));

            private object Call(string name)
            {
                var method = FindMethod(_host, "PCL.ModLaunch", name, m => m.GetParameters().Length == 0);
                if (method == null) throw new MissingMethodException("PCL.ModLaunch." + name);
                return method.Invoke(null, null);
            }
        }

        private sealed class LoaderAccess : IPclLoaderAccess
        {
            private readonly Assembly _host;

            public LoaderAccess(Assembly host) => _host = host;

            public double TaskbarProgress =>
                Convert.ToDouble(Static("LoaderTaskbarProgressGet", 0).Invoke(null, null) ?? 0.0);

            public void RefreshTaskbar() => Static("LoaderTaskbarProgressRefresh", 0).Invoke(null, null);

            public void TaskbarAdd(object loaderCombo) =>
                Static("LoaderTaskbarAdd", 1).Invoke(null, new[] { loaderCombo });

            public object GetState(object loader) => ReadMember(Require(loader), "State");

            public double GetProgress(object loader) =>
                Convert.ToDouble(ReadMember(Require(loader), "Progress") ?? 0.0);

            public bool IsRunning(object loader) =>
                string.Equals(Convert.ToString(GetState(loader)), "Loading", StringComparison.OrdinalIgnoreCase);

            public void Start(object loader, object input = null, bool forceRestart = false) =>
                InstanceCall(Require(loader), "Start", input, forceRestart);

            public void Cancel(object loader) => InstanceCall(Require(loader), "Cancel");

            public void Failed(object loader, Exception error) =>
                InstanceCall(Require(loader), "Failed", error);

            private static object Require(object loader) =>
                loader ?? throw new ArgumentNullException(nameof(loader), "需要一个 LoaderBase 实例");

            private MethodInfo Static(string name, int parameterCount)
            {
                var method = FindMethod(_host, "PCL.ModLoader", name,
                    candidate => candidate.GetParameters().Length == parameterCount);
                if (method == null) throw new MissingMethodException("PCL.ModLoader." + name);
                return method;
            }

            private static void InstanceCall(object loader, string name, params object[] args)
            {
                var method = loader.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                        candidate.Name == name && candidate.GetParameters().Length == args.Length);
                if (method == null) throw new MissingMethodException(loader.GetType().FullName, name);
                method.Invoke(loader, args);
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
