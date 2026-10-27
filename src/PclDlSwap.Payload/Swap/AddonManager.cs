using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Owns the addon list: three built-ins that back today's features plus any .swapaddon
    /// archives dropped into %TEMP%\PCL2Swap\addons. Every addon gets its own scoped API (private
    /// config store) and a failure in one addon never breaks the host or the others.
    /// </summary>
    internal sealed class AddonManager
    {
        public sealed class AddonRecord
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public string Description { get; set; }
            public string Directory { get; set; }
            public string ArchivePath { get; set; }
            public bool BuiltIn { get; set; }
            public bool Enabled { get; set; } = true;
            public string Status { get; set; } = "registered";
            public ISwapAddon Instance { get; set; }
            public AddonManifest Manifest { get; set; }
            public Exception Failure { get; set; }
        }

        private readonly ISwapApi _api;
        private readonly List<AddonRecord> _records = new List<AddonRecord>();
        private readonly Dictionary<string, bool> _savedState =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool _stateLoaded;

        public AddonManager(ISwapApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

        public IReadOnlyList<AddonRecord> Records => _records;

        /// <summary>
        /// The user's enable state for an addon. Hooks installed by the host keep their detours
        /// (nothing hot-unloads yet) but consult this flag before acting, so a disabled addon
        /// behaves exactly like an absent one. Unknown ids stay enabled.
        /// </summary>
        public bool IsEnabled(string addonId)
        {
            var record = Find(addonId);
            return record == null || record.Enabled;
        }

        public AddonRecord Find(string addonId) =>
            string.IsNullOrEmpty(addonId)
                ? null
                : _records.FirstOrDefault(record =>
                    string.Equals(record.Id, addonId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Flips an addon on or off and persists the choice; drives the built-in features.</summary>
        public bool SetEnabled(string addonId, bool enabled)
        {
            var record = Find(addonId);
            if (record == null)
            {
                SwapLog.Warn("Addon 开关：未找到 " + addonId);
                return false;
            }
            if (record.Enabled == enabled) return true;

            try
            {
                if (enabled)
                {
                    if (record.Instance == null) record.Instance = CreateInstance(record);
                    var scoped = new ScopedApi(_api, new AddonDataStore(record.Id), record.Id);
                    var context = new SwapContext(scoped, record.Manifest,
                        record.Directory ?? AppDomain.CurrentDomain.BaseDirectory);
                    record.Instance?.OnLoad(context);
                    record.Instance?.OnEnable();
                    record.Status = "enabled";
                    record.Failure = null;
                }
                else
                {
                    try { record.Instance?.OnDisable(); }
                    catch (Exception ex) { SwapLog.Warn($"Addon {record.Id} OnDisable 抛错：" + ex.Message); }
                    record.Status = "disabled";
                }

                record.Enabled = enabled;
                SaveState();
                var note = record.BuiltIn
                    ? (enabled
                        ? "（内置功能的钩子立即放行）"
                        : "（钩子类拦截立即停用；已完成的页面/侧栏修复要重启 PCL 才会完全回退）")
                    : string.Empty;
                SwapLog.Info($"Addon 已{(enabled ? "启用" : "禁用")}：{record.Id}" + note);
                return true;
            }
            catch (Exception ex)
            {
                record.Status = "failed";
                record.Failure = ex;
                SwapLog.Error($"Addon {(enabled ? "启用" : "禁用")}失败：{record.Id}", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ module state

        private void LoadState()
        {
            if (_stateLoaded) return;
            _stateLoaded = true;
            try
            {
                if (!File.Exists(SwapPaths.ModulesFile)) return;
                var serializer = new JavaScriptSerializer();
                var map = serializer.Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(SwapPaths.ModulesFile));
                if (map == null) return;

                foreach (var pair in map)
                {
                    if (pair.Value is Dictionary<string, object> entry &&
                        entry.TryGetValue("enabled", out var enabled))
                        _savedState[pair.Key] = Convert.ToBoolean(enabled);
                    else
                        _savedState[pair.Key] = Convert.ToBoolean(pair.Value);
                }
                SwapLog.Info($"模块状态已读取：{SwapPaths.ModulesFile}（{_savedState.Count} 条）");
            }
            catch (Exception ex)
            {
                SwapLog.Warn("模块状态读取失败：" + ex.Message);
            }
        }

        private void SaveState()
        {
            try
            {
                var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in _records)
                {
                    if (string.IsNullOrEmpty(record.Id)) continue;
                    map[record.Id] = new Dictionary<string, object> { { "enabled", record.Enabled } };
                }

                Directory.CreateDirectory(SwapPaths.DataRoot);
                File.WriteAllText(SwapPaths.ModulesFile,
                    new JavaScriptSerializer().Serialize(map), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                SwapLog.Warn("模块状态写入失败：" + ex.Message);
            }
        }

        private void ApplySavedState()
        {
            LoadState();
            foreach (var record in _records)
            {
                if (record.Id != null && _savedState.TryGetValue(record.Id, out var enabled))
                    record.Enabled = enabled;
            }
        }

        // ------------------------------------------------------------------ built-ins

        public void RegisterBuiltIns()
        {
            AddBuiltIn(new BuiltInDownloadMcAddon(), "swap.download-mc", "Download MC", "1.0.0",
                "恢复原版游戏下载页（LoaderInit 重驱动 + 2.99.0.1 侧栏修复）。");
            AddBuiltIn(new BuiltInOtaProtectorAddon(), "swap.ota-protector", "OTA Protector", "1.0.0",
                "静默 OTA 显式确认；启动器文件下载完成后查毒（游戏内容不查）。");
            AddBuiltIn(new BuiltInCancelButtonAddon(), "swap.cancel-button", "Cancel Button", "1.0.0",
                "给本来没有取消按钮的弹窗追加一个取消（MyMsgBox 家族）。");
        }

        private void AddBuiltIn(ISwapAddon instance, string id, string name, string version, string description)
        {
            _records.Add(new AddonRecord
            {
                Id = id,
                Name = name,
                Version = version,
                Description = description,
                BuiltIn = true,
                Instance = instance,
                Manifest = new AddonManifest { Id = id, Name = name, Version = version, ApiVersion = ">=1.0 <2.0" },
                Status = "registered"
            });
        }

        // ------------------------------------------------------------------ disk addons

        public void LoadFromDisk()
        {
            try
            {
                var root = SwapPaths.AddonsDirectory;
                Directory.CreateDirectory(root);
                foreach (var archive in Directory.GetFiles(root, "*.swapaddon"))
                    TryLoadArchive(archive);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("Addon 目录扫描失败：" + ex.Message);
            }
        }

        private void TryLoadArchive(string archive)
        {
            AddonRecord record = null;
            try
            {
                using (var zip = ZipFile.OpenRead(archive))
                {
                    var manifestEntry = zip.GetEntry("manifest.json");
                    if (manifestEntry == null) throw new InvalidOperationException("包内缺少 manifest.json");

                    string json;
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                        json = reader.ReadToEnd();

                    var manifest = AddonManifestReader.Parse(json);
                    var error = Validate(manifest);
                    if (error != null) throw new InvalidOperationException(error);

                    record = new AddonRecord
                    {
                        Manifest = manifest,
                        Id = manifest.Id,
                        Name = string.IsNullOrEmpty(manifest.Name) ? manifest.Id : manifest.Name,
                        Version = manifest.Version,
                        Description = manifest.Description,
                        ArchivePath = archive,
                        Enabled = true,
                        Status = "unpacked"
                    };

                    var target = Path.Combine(SwapPaths.AddonDirectory(manifest.Id),
                        SwapPaths.Sanitize(manifest.Version ?? "0"));
                    var targetRoot = Path.GetFullPath(target);
                    Directory.CreateDirectory(targetRoot);

                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;   // directory marker
                        var path = Path.GetFullPath(Path.Combine(targetRoot, entry.FullName));
                        if (!path.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            SwapLog.Warn("Addon 包包含越界路径，已跳过：" + entry.FullName);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetRoot);
                        entry.ExtractToFile(path, true);
                    }

                    record.Directory = targetRoot;
                    _records.Add(record);
                    SwapLog.Info($"Addon 已解包：{manifest.Id} {manifest.Version} -> {targetRoot}");
                }
            }
            catch (Exception ex)
            {
                if (record != null)
                {
                    record.Status = "failed";
                    record.Enabled = false;
                    record.Failure = ex;
                    _records.Add(record);
                }
                SwapLog.Error("Addon 加载失败：" + archive, ex);
            }
        }

        private static string Validate(AddonManifest manifest)
        {
            if (manifest == null) return "manifest 解析失败";
            if (string.IsNullOrWhiteSpace(manifest.Id)) return "缺少 id";
            if (string.IsNullOrWhiteSpace(manifest.Version)) return "缺少 version";
            if (string.IsNullOrWhiteSpace(manifest.Entry)) return "缺少 entry";
            var hostApi = ApiVersionRange.ParseVersion(HostApi.ApiVersion);
            if (!ApiVersionRange.IsSatisfied(manifest.ApiVersion, hostApi))
                return $"apiVersion 不满足：{manifest.ApiVersion}（宿主 API {HostApi.ApiVersion}）";
            return null;
        }

        // ------------------------------------------------------------------ lifecycle

        public void EnableAll()
        {
            ApplySavedState();
            foreach (var record in _records.ToList())
            {
                if (!record.Enabled)
                {
                    record.Status = "disabled";
                    SwapLog.Info($"Addon 已跳过（用户禁用）：{record.Id}");
                    continue;
                }
                if (record.Instance == null && string.IsNullOrEmpty(record.Directory)) continue;
                try
                {
                    if (record.Instance == null) record.Instance = CreateInstance(record);
                    if (record.Instance == null) continue;

                    var scoped = new ScopedApi(_api, new AddonDataStore(record.Id), record.Id);
                    var context = new SwapContext(scoped, record.Manifest,
                        record.Directory ?? AppDomain.CurrentDomain.BaseDirectory);
                    record.Instance.OnLoad(context);
                    record.Instance.OnEnable();
                    record.Status = "enabled";
                    SwapLog.Info($"Addon 已启用：{record.Id}（{record.Name} {record.Version}）");
                }
                catch (Exception ex)
                {
                    record.Status = "failed";
                    record.Enabled = false;
                    record.Failure = ex;
                    SwapLog.Error($"Addon 启用失败：{record.Id}", ex);
                }
            }
        }

        private static ISwapAddon CreateInstance(AddonRecord record)
        {
            var entryName = record.Manifest?.Entry;
            if (string.IsNullOrEmpty(entryName) || string.IsNullOrEmpty(record.Directory))
                throw new InvalidOperationException("Addon 记录缺少 entry 或解包目录");

            foreach (var dll in Directory.GetFiles(record.Directory, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var type = assembly.GetType(entryName, throwOnError: false);
                    if (type == null || !typeof(ISwapAddon).IsAssignableFrom(type)) continue;
                    return Activator.CreateInstance(type) as ISwapAddon;
                }
                catch
                {
                    // try the next assembly; a broken dependency must not abort the scan
                }
            }
            throw new InvalidOperationException($"在包内未找到 ISwapAddon 实现：{entryName}");
        }

        private sealed class ScopedApi : ISwapApi
        {
            private readonly ISwapApi _inner;

            public ScopedApi(ISwapApi inner, IData data, string addonId)
            {
                _inner = inner;
                Data = data;
                Hooks = new ScopedHooks(inner.Hooks, inner as HostApi, addonId);
            }

            public IHostInfo Host => _inner.Host;
            public IHooks Hooks { get; }
            public IUi Ui => _inner.Ui;
            public IData Data { get; }
            public IReflection Reflect => _inner.Reflect;
            public ISwapSurface Surface => _inner.Surface;
            public IPclAccess Pcl => _inner.Pcl;
            public void Log(string message) => _inner.Log(message);
            public void LogWarning(string message) => _inner.LogWarning(message);
            public void LogError(string message, Exception exception = null) => _inner.LogError(message, exception);
        }

        /// <summary>
        /// Per-addon hook view. New hooks are installed behind a module gate, so switching the
        /// module off - or disposing the attach token - stops them immediately (the gate forwards
        /// to the original method from then on). Event subscriptions stay on the shared bus.
        /// </summary>
        private sealed class ScopedHooks : IHooks
        {
            private readonly IHooks _inner;
            private readonly HostApi _host;
            private readonly string _addonId;

            public ScopedHooks(IHooks inner, HostApi host, string addonId)
            {
                _inner = inner;
                _host = host;
                _addonId = addonId;
            }

            public event EventHandler<DownloadEventArgs> DownloadCompleted
            {
                add => _inner.DownloadCompleted += value;
                remove => _inner.DownloadCompleted -= value;
            }

            public event EventHandler<DialogEventArgs> DialogOpening
            {
                add => _inner.DialogOpening += value;
                remove => _inner.DialogOpening -= value;
            }

            public event EventHandler<UpdateEventArgs> UpdateRequested
            {
                add => _inner.UpdateRequested += value;
                remove => _inner.UpdateRequested -= value;
            }

            public IDisposable Attach(string declaringType, string methodName, string hookId,
                Delegate handler, HookMode mode)
            {
                if (_host == null)
                    return _inner.Attach(declaringType, methodName, hookId, handler, mode);
                return _host.AttachAddonHook(_addonId, declaringType, methodName, hookId, mode, handler);
            }

            public bool IsHooked(string declaringType, string methodName) =>
                _inner.IsHooked(declaringType, methodName);
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
