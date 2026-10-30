using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
            Ui = new UiAccess(host);
            Logs = new LogAccess(host);
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
        public IPclUiAccess Ui { get; }
        public IPclLogAccess Logs { get; }

        /// <summary>Live-UI reads plus the translation pass (API 1.1).</summary>
        private sealed class UiAccess : IPclUiAccess
        {
            private readonly Assembly _host;

            public UiAccess(Assembly host) => _host = host;

            public string CurrentPage
            {
                get
                {
                    try
                    {
                        var main = PclUi.GetMember(_host?.GetType("PCL.ModMain", false), "FrmMain");
                        var page = PclUi.GetMember(main, "PageCurrent")
                                   ?? PclUi.GetMember(main, "PageNow")
                                   ?? PclUi.GetMember(main, "PageCurrentType");
                        return page?.GetType().Name ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }

            public object MainWindow =>
                PclUi.GetMember(_host?.GetType("PCL.ModMain", false), "FrmMain");

            public object FindPage(string memberName) =>
                PclUi.GetMember(_host?.GetType("PCL.ModMain", false), memberName);

            public int Translate(object visualRoot) => HostTranslator.Apply(visualRoot);

            public int Restore(object visualRoot) => HostTranslator.Restore(visualRoot);
        }

        /// <summary>PCL's own log file (API 1.1).</summary>
        private sealed class LogAccess : IPclLogAccess
        {
            private readonly Assembly _host;

            public LogAccess(Assembly host) => _host = host;

            public string LogPath
            {
                get
                {
                    try
                    {
                        var location = _host?.Location;
                        if (string.IsNullOrEmpty(location)) return string.Empty;
                        var side = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(location) ?? string.Empty, "PCL", "Log1.txt");
                        return System.IO.File.Exists(side) ? side : string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }

            public string Tail(int lines = 120)
            {
                var path = LogPath;
                if (string.IsNullOrEmpty(path)) return string.Empty;
                try
                {
                    var capacity = Math.Max(1, lines);
                    var queue = new Queue<string>(capacity);
                    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Open,
                               System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (queue.Count == capacity) queue.Dequeue();
                            queue.Enqueue(line);
                        }
                    }

                    return string.Join(Environment.NewLine, queue);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

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

        /// <summary>Writes an instance member by name, covering plain fields and VB.NET properties.</summary>
        internal static void WriteMember(object instance, string name, object value)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = instance.GetType();

            var field = type.GetField(name, flags);
            if (field != null) { field.SetValue(instance, value); return; }
            var property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite) { property.SetValue(instance, value, null); return; }
            throw new MissingMemberException(type.FullName, name);
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

            public string GetName(object loader) => Convert.ToString(ReadMember(Require(loader), "Name"));

            public double GetProgressWeight(object loader) =>
                Convert.ToDouble(ReadMember(Require(loader), "ProgressWeight") ?? 1.0);

            public void SetProgressWeight(object loader, double weight) =>
                WriteMember(Require(loader), "ProgressWeight", weight);

            public Exception GetError(object loader) => ReadMember(Require(loader), "Error") as Exception;

            public IDisposable Watch(object loader, Action<double> onProgress, Action<string> onStateChanged) =>
                new LoaderWatch(Require(loader), onProgress, onStateChanged);

            public IDisposable WatchPreviewFinish(object loader, Action<object> onPreviewFinish)
            {
                var target = Require(loader);
                var preview = target.GetType().GetEvent("PreviewFinish",
                    BindingFlags.Public | BindingFlags.Instance);
                if (preview == null) throw new MissingMemberException(target.GetType().FullName, "PreviewFinish");

                var handler = DelegateBridge.Create(preview.EventHandlerType,
                    args => onPreviewFinish?.Invoke(args.Length > 0 ? args[0] : null));
                preview.AddEventHandler(target, handler.Handler);
                return new Unsubscriber(() =>
                {
                    preview.RemoveEventHandler(target, handler.Handler);
                    handler.Dispose();
                });
            }

            public object CreateCombo(string name, IReadOnlyList<object> loaders, Type inputType = null)
            {
                var comboOpen = _host.GetType("PCL.ModLoader+LoaderCombo`1", throwOnError: false);
                var loaderBase = _host.GetType("PCL.ModLoader+LoaderBase", throwOnError: false);
                if (comboOpen == null || loaderBase == null)
                    throw new MissingMemberException(_host.FullName, "PCL.ModLoader+LoaderCombo`1");

                var comboType = comboOpen.MakeGenericType(inputType ?? typeof(object));
                var listType = typeof(List<>).MakeGenericType(loaderBase);
                var list = (IList)Activator.CreateInstance(listType);
                if (loaders != null)
                {
                    foreach (var loader in loaders)
                    {
                        if (loader == null || !loaderBase.IsInstanceOfType(loader)) continue;
                        list.Add(loader);
                    }
                }
                return Activator.CreateInstance(comboType, name ?? ("Swap 组合任务 " + list.Count), list);
            }

            public IReadOnlyList<object> ListTasks(object loader, bool onlyRunning = false)
            {
                var target = Require(loader);
                var list = new List<object>();

                // A LoaderCombo answers this itself...
                var instance = target.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "GetLoaderList" &&
                                                 candidate.GetParameters().Length == 1 &&
                                                 candidate.GetParameters()[0].ParameterType == typeof(bool));
                if (instance != null)
                {
                    if (instance.Invoke(target, new object[] { onlyRunning }) is IEnumerable direct)
                    {
                        foreach (var item in direct) list.Add(item);
                    }
                    return list;
                }

                // ...anything else (a plain LoaderTask) goes through the host's own static walker,
                // which recurses into nested combos.
                var loaderBase = _host.GetType("PCL.ModLoader+LoaderBase", throwOnError: false);
                var comboOpen = _host.GetType("PCL.ModLoader+LoaderCombo`1", throwOnError: false);
                if (loaderBase == null || comboOpen == null)
                    throw new MissingMemberException(_host.FullName, "PCL.ModLoader+LoaderCombo`1");

                var walker = comboOpen.MakeGenericType(typeof(object))
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(candidate => candidate.Name == "GetLoaderList" &&
                                                 candidate.GetParameters().Length == 3);
                if (walker == null) throw new MissingMethodException(comboOpen.FullName, "GetLoaderList");

                var filled = Activator.CreateInstance(typeof(List<>).MakeGenericType(loaderBase));
                var args = new[] { target, filled, (object)onlyRunning };
                walker.Invoke(null, args);
                if (args[1] is IEnumerable walked)
                {
                    foreach (var item in walked) list.Add(item);
                }
                return list;
            }

            public IReadOnlyList<object> ListAll(bool onlyRunning = false)
            {
                var modLoader = _host.GetType("PCL.ModLoader", throwOnError: false);
                if (modLoader == null) throw new MissingMemberException(_host.FullName, "PCL.ModLoader");

                // ModLoader.LoaderTaskbar is the launcher's own list of loaders currently shown on
                // the taskbar - the honest "what is PCL busy with" source.
                var field = modLoader.GetField("LoaderTaskbar",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (!(field?.GetValue(null) is IEnumerable source))
                    throw new MissingMemberException(modLoader.FullName, "LoaderTaskbar");

                var result = new List<object>();
                foreach (var item in source)
                {
                    if (item == null) continue;
                    if (onlyRunning &&
                        !string.Equals(Convert.ToString(ReadMember(item, "State")), "Loading",
                            StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(item);
                }
                return result;
            }

            private static object Require(object loader) =>
                loader ?? throw new ArgumentNullException(nameof(loader), "需要一个 LoaderBase 实例");

            /// <summary>
            /// One loader subscription. The host exposes real .NET events
            /// (<c>OnStateChangedUi(LoaderBase, LoadState, LoadState)</c> and
            /// <c>ProgressChanged(double, double)</c>), so this detaches cleanly on dispose instead of
            /// polling; the enum/double parameters are bridged by <see cref="DelegateBridge"/>.
            /// </summary>
            private sealed class LoaderWatch : IDisposable
            {
                private readonly object _loader;
                private readonly Action<double> _onProgress;
                private readonly Action<string> _onStateChanged;
                private readonly EventInfo _stateEvent;
                private readonly EventInfo _progressEvent;
                private readonly DelegateBridge.Handle _stateHandler;
                private readonly DelegateBridge.Handle _progressHandler;
                private bool _disposed;

                public LoaderWatch(object loader, Action<double> onProgress, Action<string> onStateChanged)
                {
                    _loader = loader;
                    _onProgress = onProgress;
                    _onStateChanged = onStateChanged;

                    var type = loader.GetType();
                    _stateEvent = type.GetEvent("OnStateChangedUi", BindingFlags.Public | BindingFlags.Instance);
                    _progressEvent = type.GetEvent("ProgressChanged", BindingFlags.Public | BindingFlags.Instance);

                    if (_stateEvent != null && onStateChanged != null)
                    {
                        _stateHandler = DelegateBridge.Create(_stateEvent.EventHandlerType, OnStateBridge);
                        _stateEvent.AddEventHandler(loader, _stateHandler.Handler);
                    }
                    if (_progressEvent != null && onProgress != null)
                    {
                        _progressHandler = DelegateBridge.Create(_progressEvent.EventHandlerType, OnProgressBridge);
                        _progressEvent.AddEventHandler(loader, _progressHandler.Handler);
                    }

                    // Seed the current values so a module starts from what is already on screen.
                    SafeProgress(Convert.ToDouble(ReadMember(loader, "Progress") ?? 0.0));
                    SafeState(Convert.ToString(ReadMember(loader, "State")));
                }

                private void OnStateBridge(object[] args)
                {
                    var state = args.Length > 1 ? Convert.ToString(args[1]) : null;
                    SafeState(state);

                    // Finished/Failed/Canceled end the loader's life: stop watching so a module that
                    // forgets to dispose cannot leak a handler onto a reused loader object.
                    if (string.Equals(state, "Finished", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Canceled", StringComparison.OrdinalIgnoreCase))
                        Dispose();
                }

                private void OnProgressBridge(object[] args)
                {
                    var value = args.Length > 0 ? Convert.ToDouble(args[0]) : 0.0;
                    SafeProgress(value);
                }

                private void SafeProgress(double value)
                {
                    try { _onProgress?.Invoke(value); }
                    catch (Exception ex) { SwapLog.Warn("Loader watch 进度回调异常：" + ex.Message); }
                }

                private void SafeState(string state)
                {
                    try { _onStateChanged?.Invoke(state); }
                    catch (Exception ex) { SwapLog.Warn("Loader watch 状态回调异常：" + ex.Message); }
                }

                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    try
                    {
                        if (_stateEvent != null && _stateHandler != null)
                        {
                            _stateEvent.RemoveEventHandler(_loader, _stateHandler.Handler);
                            _stateHandler.Dispose();
                        }
                        if (_progressEvent != null && _progressHandler != null)
                        {
                            _progressEvent.RemoveEventHandler(_loader, _progressHandler.Handler);
                            _progressHandler.Dispose();
                        }
                    }
                    catch (Exception ex) { SwapLog.Warn("Loader watch 取消订阅失败：" + ex.Message); }
                }
            }

            private sealed class Unsubscriber : IDisposable
            {
                private readonly Action _dispose;
                private bool _disposed;

                public Unsubscriber(Action dispose) => _dispose = dispose;

                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    try { _dispose?.Invoke(); }
                    catch (Exception ex) { SwapLog.Warn("取消订阅失败：" + ex.Message); }
                }
            }

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

        /// <summary>
        /// Adapter for host events whose parameters are value types (<c>LoadState</c> enums, doubles):
        /// an Action-based module callback cannot bind to those delegates, so this compiles a small
        /// delegate of the host's exact event type that boxes the arguments into an <c>object[]</c> and
        /// calls the callback. One entry per subscription; <see cref="Handle.Dispose"/> frees it.
        /// </summary>
        private static class DelegateBridge
        {
            private static readonly Dictionary<long, Action<object[]>> Slots =
                new Dictionary<long, Action<object[]>>();
            private static long _next;

            public sealed class Handle : IDisposable
            {
                private readonly long _id;
                public Delegate Handler { get; }
                private bool _disposed;

                internal Handle(Delegate handler, long id)
                {
                    Handler = handler;
                    _id = id;
                }

                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    lock (Slots) Slots.Remove(_id);
                }
            }

            public static Handle Create(Type delegateType, Action<object[]> sink)
            {
                var id = System.Threading.Interlocked.Increment(ref _next);
                lock (Slots) Slots[id] = sink;

                var invoke = delegateType.GetMethod("Invoke");
                var parameters = invoke.GetParameters()
                    .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                    .ToArray();
                var boxed = Expression.NewArrayInit(typeof(object),
                    parameters.Select(parameter => (Expression)Expression.Convert(parameter, typeof(object))));
                var body = Expression.Call(
                    typeof(DelegateBridge).GetMethod(nameof(Dispatch), BindingFlags.NonPublic | BindingFlags.Static),
                    Expression.Constant(id),
                    boxed);
                var lambda = Expression.Lambda(delegateType, body, parameters);
                return new Handle(lambda.Compile(), id);
            }

            private static void Dispatch(long id, object[] args)
            {
                Action<object[]> sink;
                lock (Slots) Slots.TryGetValue(id, out sink);
                try { sink?.Invoke(args); }
                catch (Exception ex) { SwapLog.Warn("Loader 事件桥接异常：" + ex.Message); }
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
