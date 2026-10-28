using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
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

            /// <summary>Settings pages this addon registered, so an uninstall can drop them again.</summary>
            public List<string> PageIds { get; } = new List<string>();
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

            // Flip the flag first: the module's own lifecycle hooks (and any hook it attaches)
            // must see the new state, otherwise a re-enable would install its hooks as "disabled".
            var previous = record.Enabled;
            record.Enabled = enabled;
            try
            {
                if (enabled)
                {
                    EnableRecord(record);
                    if (record.Status != "enabled") return false;
                }
                else
                {
                    try { record.Instance?.OnDisable(); }
                    catch (Exception ex) { SwapLog.Warn($"Addon {record.Id} OnDisable 抛错：" + ex.Message); }
                    record.Status = "disabled";
                }

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
                record.Enabled = previous;
                record.Status = "failed";
                record.Failure = ex;
                SaveState();
                SwapLog.Error($"Addon {(enabled ? "启用" : "禁用")}失败：{record.Id}", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ install / uninstall

        /// <summary>
        /// Installs (or replaces) a .swapaddon from anywhere on disk: validate it, keep a copy in the
        /// addon store, unpack it and start it right away. Re-installing the same id retires the
        /// previous copy first, so its hooks stop and its settings pages leave the sidebar - this is
        /// the hot module update path, no PCL restart involved.
        /// </summary>
        public AddonRecord Install(string archivePath, bool enable = true)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("找不到模块包：" + archivePath);

            var storeCopy = EnsureStoreCopy(archivePath);
            var manifest = ReadManifest(storeCopy);
            var existing = Find(manifest.Id);
            if (existing != null)
            {
                if (existing.BuiltIn)
                    throw new InvalidOperationException($"“{manifest.Id}”是内置模块，不能被同名包覆盖。");
                SwapLog.Info($"Addon 更新：{manifest.Id} {existing.Version} -> {manifest.Version}");
                Retire(existing, removeArchive: true, keepSavedState: true);
            }

            var record = Unpack(storeCopy, manifest);
            _records.Add(record);
            SaveState();

            if (enable) EnableRecord(record);
            else
            {
                record.Enabled = false;
                record.Status = "disabled";
                SaveState();
            }
            return record;
        }

        /// <summary>
        /// Removes a module: stop it, drop its settings pages, delete the store copy and the
        /// unpacked files. A loaded entry assembly keeps its DLL locked for the rest of the
        /// process, so anything still locked is queued for deletion at the next start instead of
        /// failing the uninstall.
        /// </summary>
        public bool Uninstall(string addonId)
        {
            var record = Find(addonId);
            if (record == null)
            {
                SwapLog.Warn("Addon 卸载：未找到 " + addonId);
                return false;
            }
            if (record.BuiltIn)
            {
                SwapLog.Warn("Addon 卸载：内置模块只能禁用，不能卸载。");
                return false;
            }

            Retire(record, removeArchive: true, keepSavedState: false);
            _savedState.Remove(record.Id);
            SaveState();
            SwapLog.Info($"Addon 已卸载：{record.Id}（{record.Name} {record.Version}）");
            return true;
        }

        /// <summary>Stops an addon, drops its pages and (optionally) deletes its files.</summary>
        private void Retire(AddonRecord record, bool removeArchive, bool keepSavedState)
        {
            try { record.Instance?.OnDisable(); }
            catch (Exception ex) { SwapLog.Warn($"Addon {record.Id} OnDisable 抛错：" + ex.Message); }
            try { record.Instance?.OnUnload(); }
            catch (Exception ex) { SwapLog.Warn($"Addon {record.Id} OnUnload 抛错：" + ex.Message); }

            foreach (var pageId in record.PageIds.ToList())
                PageRegistry.UnregisterPage(pageId);
            record.PageIds.Clear();
            record.Instance = null;
            record.Status = "removed";

            if (!keepSavedState) _savedState.Remove(record.Id);
            if (!record.BuiltIn) DeleteOrQueue(record, removeArchive, removeModuleData: !keepSavedState);

            _records.Remove(record);
        }

        /// <summary>
        /// Keeps the package inside the addon store. A package picked from Downloads is copied, so
        /// uninstall only ever deletes files the platform put there itself.
        /// </summary>
        private static string EnsureStoreCopy(string archivePath)
        {
            var full = Path.GetFullPath(archivePath);
            var store = Path.GetFullPath(SwapPaths.AddonsDirectory);
            if (full.StartsWith(store, StringComparison.OrdinalIgnoreCase)) return full;

            Directory.CreateDirectory(store);
            var manifest = ReadManifest(full);
            var name = SwapPaths.Sanitize(manifest.Id + "-" + (manifest.Version ?? "0")) + ".swapaddon";
            var destination = Path.Combine(store, name);
            File.Copy(full, destination, true);
            SwapLog.Info($"Addon 包已归档到模块目录：{destination}");
            return destination;
        }

        private static void DeleteOrQueue(AddonRecord record, bool removeArchive, bool removeModuleData)
        {
            var targets = new List<string>();
            // A full uninstall removes the module's whole folder (unpacked versions + config.json);
            // an update only retires the unpacked version.
            if (removeModuleData && !string.IsNullOrEmpty(record.Id))
                targets.Add(SwapPaths.AddonDirectory(record.Id));
            else if (!string.IsNullOrEmpty(record.Directory)) targets.Add(record.Directory);
            if (removeArchive && !string.IsNullOrEmpty(record.ArchivePath)) targets.Add(record.ArchivePath);

            var queued = new List<string>();
            foreach (var path in targets)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    queued.Add(path);
                    SwapLog.Warn($"Addon 文件暂被占用，将在下次启动删除：{path}（{ex.Message}）");
                }
            }

            // Drop the now-empty <addons>\<id> folder too; a locked entry dll keeps it alive, and
            // then the pending marker above cleans both levels at the next start.
            if (!removeModuleData && !string.IsNullOrEmpty(record.Directory) && queued.Count == 0)
            {
                var parent = Path.GetDirectoryName(record.Directory.TrimEnd(Path.DirectorySeparatorChar));
                try
                {
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
                        Directory.GetFileSystemEntries(parent).Length == 0)
                        Directory.Delete(parent);
                }
                catch { /* left for the user; an empty folder is harmless */ }
            }

            if (queued.Count > 0) WritePendingRemoval(record.Id, queued);
        }

        private static void WritePendingRemoval(string addonId, IEnumerable<string> paths)
        {
            try
            {
                var directory = PendingRemovalDirectory;
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory,
                    SwapPaths.Sanitize(addonId) + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt");
                File.WriteAllLines(file, paths);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("记录待删除模块文件失败：" + ex.Message);
            }
        }

        private static string PendingRemovalDirectory =>
            Path.Combine(SwapPaths.AddonsDirectory, ".pending");

        /// <summary>
        /// Retries the deletions that were blocked by locked module DLLs in a previous run. Called
        /// before the addon scan so a removed module cannot come back from a half-deleted folder.
        /// </summary>
        public void CleanupPendingRemovals()
        {
            try
            {
                var directory = PendingRemovalDirectory;
                if (!Directory.Exists(directory)) return;
                foreach (var marker in Directory.GetFiles(directory, "*.txt"))
                {
                    var remaining = new List<string>();
                    foreach (var path in File.ReadAllLines(marker))
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        try
                        {
                            if (Directory.Exists(path)) Directory.Delete(path, true);
                            else if (File.Exists(path)) File.Delete(path);
                        }
                        catch { remaining.Add(path); }
                    }

                    if (remaining.Count == 0)
                    {
                        try { File.Delete(marker); } catch { }
                        SwapLog.Info("待删除模块文件已清理完成：" + Path.GetFileName(marker));
                    }
                    else
                    {
                        File.WriteAllLines(marker, remaining);
                        SwapLog.Warn("待删除模块文件仍被占用：" + Path.GetFileName(marker));
                    }
                }

                // The marker folder itself goes away once nothing is queued any more.
                try
                {
                    if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                        Directory.Delete(directory);
                }
                catch { /* an empty .pending folder is harmless */ }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("清理待删除模块文件失败：" + ex.Message);
            }
        }

        /// <summary>
        /// Starts one addon. Shape is the platform's scope: a module that declares
        /// <c>shapes: ["injected"]</c> is skipped (not failed) while PCL runs the patched shape.
        /// </summary>
        private void EnableRecord(AddonRecord record)
        {
            if (!IsShapeApplicable(record))
            {
                record.Status = "skipped-shape";
                record.Failure = null;
                SwapLog.Info($"Addon 跳过（作用域不匹配）：{record.Id} 只声明了 " +
                             string.Join(", ", record.Manifest?.Shapes ?? new List<string>()) +
                             $"，当前形状 {SwapHost.Shape}。");
                return;
            }

            if (record.Instance == null && string.IsNullOrEmpty(record.Directory)) return;

            // Flip the flag first: hooks the module attaches during OnLoad/OnEnable must already see
            // the module as enabled, otherwise the gate would keep them transparent.
            record.Enabled = true;
            try
            {
                if (record.Instance == null) record.Instance = CreateInstance(record);
                if (record.Instance == null) return;

                var scoped = new ScopedApi(_api, record);
                var context = new SwapContext(scoped, record.Manifest,
                    record.Directory ?? AppDomain.CurrentDomain.BaseDirectory);
                record.Instance.OnLoad(context);
                record.Instance.OnEnable();
                record.Status = "enabled";
                record.Failure = null;
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

        private static bool IsShapeApplicable(AddonRecord record)
        {
            var shapes = record.Manifest?.Shapes;
            if (shapes == null || shapes.Count == 0) return true;
            var current = SwapHost.Shape.ToString();
            return shapes.Any(shape =>
                string.Equals(shape, current, StringComparison.OrdinalIgnoreCase) || shape == "*");
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
                CleanupPendingRemovals();
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
                var manifest = ReadManifest(archive);
                record = Unpack(archive, manifest);
                _records.Add(record);
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

        /// <summary>Reads and validates manifest.json; throws a readable message when it cannot be used.</summary>
        private static AddonManifest ReadManifest(string archive)
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
                return manifest;
            }
        }

        /// <summary>Extracts a validated package under &lt;addons&gt;\&lt;id&gt;\&lt;version&gt; with a zip-slip guard.</summary>
        private static AddonRecord Unpack(string archive, AddonManifest manifest)
        {
            var record = new AddonRecord
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

            var targetRoot = Path.GetFullPath(Path.Combine(SwapPaths.AddonDirectory(manifest.Id),
                SwapPaths.Sanitize(manifest.Version ?? "0")));
            Directory.CreateDirectory(targetRoot);

            using (var zip = ZipFile.OpenRead(archive))
            {
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
            }

            record.Directory = targetRoot;
            SwapLog.Info($"Addon 已解包：{manifest.Id} {manifest.Version} -> {targetRoot}");
            return record;
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
                EnableRecord(record);
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

            public ScopedApi(ISwapApi inner, AddonRecord record)
            {
                _inner = inner;
                Data = new AddonDataStore(record.Id);
                Hooks = new ScopedHooks(inner.Hooks, inner as HostApi, record.Id);
                Ui = new ScopedUi(inner.Ui, record);
            }

            public IHostInfo Host => _inner.Host;
            public IHooks Hooks { get; }
            public IUi Ui { get; }
            public IData Data { get; }
            public IReflection Reflect => _inner.Reflect;
            public ISwapSurface Surface => _inner.Surface;
            public IPclAccess Pcl => _inner.Pcl;
            public void Log(string message) => _inner.Log(message);
            public void LogWarning(string message) => _inner.LogWarning(message);
            public void LogError(string message, Exception exception = null) => _inner.LogError(message, exception);
        }

        /// <summary>
        /// Per-addon UI view. Settings pages are namespaced with the module id and remembered on the
        /// record, so an uninstall can drop exactly the pages that module added - and nothing else.
        /// </summary>
        private sealed class ScopedUi : IUi
        {
            private readonly IUi _inner;
            private readonly AddonRecord _record;

            public ScopedUi(IUi inner, AddonRecord record)
            {
                _inner = inner;
                _record = record;
            }

            public void RegisterSettingsPage(string id, string title, Func<FrameworkElement> viewFactory)
            {
                var logical = _record.Id + "/" +
                              (string.IsNullOrEmpty(id) ? "page" + (_record.PageIds.Count + 1) : id);
                _record.PageIds.Add(logical);
                _inner.RegisterSettingsPage(logical, title, viewFactory);
            }

            public void Hint(string text) => _inner.Hint(text);

            public int MsgBox(string caption, string title, string button1 = "确定", string button2 = "") =>
                _inner.MsgBox(caption, title, button1, button2);

            public FrameworkElement Card(string title, params FrameworkElement[] content) =>
                _inner.Card(title, content);

            public FrameworkElement Text(string text, double size = 12.5, bool secondary = false) =>
                _inner.Text(text, size, secondary);

            public FrameworkElement Button(string text, Action onClick, string colorType = null) =>
                _inner.Button(text, onClick, colorType);

            public FrameworkElement InfoRow(string label, string value) => _inner.InfoRow(label, value);

            public FrameworkElement Buttons(params FrameworkElement[] buttons) => _inner.Buttons(buttons);

            public FrameworkElement Note(string text) => _inner.Note(text);

            public FrameworkElement CheckBox(string label, bool initial, Action<bool> onChange) =>
                _inner.CheckBox(label, initial, onChange);

            public FrameworkElement Combo(string label, IReadOnlyList<string> options, int selectedIndex,
                Action<int> onChange) => _inner.Combo(label, options, selectedIndex, onChange);
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
