using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Thread-safe per-addon configuration store. Legacy flat JSON is read as schema 1; new saves
    /// use a small envelope and replace the file atomically while retaining the previous version.
    /// </summary>
    internal sealed class AddonDataStore : IAddonConfig
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly string _backupPath;
        private Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private int _schemaVersion = 1;

        public AddonDataStore(string addonId)
        {
            var directory = SwapPaths.AddonDirectory(addonId);
            try { Directory.CreateDirectory(directory); } catch { }
            _path = Path.Combine(directory, "config.json");
            _backupPath = _path + ".bak";
            TryLoad();
        }

        public int SchemaVersion
        {
            get { lock (_gate) return _schemaVersion; }
        }

        public IReadOnlyList<string> Keys
        {
            get
            {
                lock (_gate)
                    return _values.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
            }
        }

        public event EventHandler Changed;

        public string Get(string key, string fallback = null)
        {
            lock (_gate)
                return key != null && _values.TryGetValue(key, out var value) ? value : fallback;
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            var changed = false;
            lock (_gate)
            {
                var normalized = value ?? string.Empty;
                if (!_values.TryGetValue(key, out var current) ||
                    !string.Equals(current, normalized, StringComparison.Ordinal))
                {
                    _values[key] = normalized;
                    changed = true;
                }
            }
            if (changed) RaiseChanged();
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var removed = false;
            lock (_gate) removed = _values.Remove(key);
            if (removed) RaiseChanged();
            return removed;
        }

        public void Save()
        {
            lock (_gate) TrySaveSnapshot(_values, _schemaVersion);
        }

        public bool Migrate(int targetVersion, Action<IAddonConfigTransaction> migration)
        {
            if (targetVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(targetVersion));
            if (migration == null) throw new ArgumentNullException(nameof(migration));

            lock (_gate)
            {
                if (targetVersion <= _schemaVersion) return false;
                var staged = new Dictionary<string, string>(_values, StringComparer.Ordinal);
                migration(new ConfigTransaction(staged));
                if (!TrySaveSnapshot(staged, targetVersion))
                    throw new IOException("模块配置迁移落盘失败：" + _path);
                _values = staged;
                _schemaVersion = targetVersion;
            }
            RaiseChanged();
            return true;
        }

        private void TryLoad()
        {
            if (TryLoadFrom(_path, out var values, out var version))
            {
                _values = values;
                _schemaVersion = version;
                return;
            }

            if (!File.Exists(_backupPath)) return;
            if (!TryLoadFrom(_backupPath, out values, out version))
            {
                SwapLog.Warn($"Addon 配置主文件与备份均无法读取：{_path}");
                return;
            }

            _values = values;
            _schemaVersion = version;
            SwapLog.Warn($"Addon 配置主文件读取失败，已从备份恢复：{_backupPath}");
        }

        private bool TryLoadFrom(string path, out Dictionary<string, string> values, out int version)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            version = 1;
            try
            {
                if (!File.Exists(path)) return false;
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var map = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (map == null) return false;

                if (map.TryGetValue("_schema", out var rawSchema) &&
                    map.TryGetValue("_values", out var rawValues) &&
                    rawValues is Dictionary<string, object> nested)
                {
                    version = Math.Max(1, Convert.ToInt32(rawSchema));
                    foreach (var pair in nested)
                        values[pair.Key] = Convert.ToString(pair.Value) ?? string.Empty;
                }
                else
                {
                    // Pre-API-1.10 files were a flat string dictionary.
                    version = 1;
                    foreach (var pair in map)
                        values[pair.Key] = Convert.ToString(pair.Value) ?? string.Empty;
                }
                return true;
            }
            catch (Exception ex)
            {
                SwapLog.Warn($"Addon 配置读取失败（{path}）：" + ex.Message);
                return false;
            }
        }

        private bool TrySaveSnapshot(Dictionary<string, string> values, int schemaVersion)
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? string.Empty);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var envelope = new Dictionary<string, object>
                {
                    ["_schema"] = schemaVersion,
                    ["_updatedUtc"] = DateTime.UtcNow.ToString("O"),
                    ["_values"] = new Dictionary<string, string>(values, StringComparer.Ordinal)
                };
                File.WriteAllText(temporary, serializer.Serialize(envelope), new UTF8Encoding(false));

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temporary, _path, _backupPath, true);
                    }
                    catch
                    {
                        // File.Replace is unavailable on a few network/FAT targets. Keep a complete
                        // previous copy before the fallback write so recovery remains possible.
                        File.Copy(_path, _backupPath, true);
                        File.Copy(temporary, _path, true);
                        File.Delete(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, _path);
                }
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                SwapLog.Warn($"Addon 配置写入失败（{_path}）：" + ex.Message);
                return false;
            }
        }

        private void RaiseChanged()
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    SwapLog.Warn("[config] Changed subscriber failed: " + ex.Message);
                }
            }
        }

        private sealed class ConfigTransaction : IAddonConfigTransaction
        {
            private readonly Dictionary<string, string> _values;

            public ConfigTransaction(Dictionary<string, string> values) => _values = values;

            public IReadOnlyList<string> Keys =>
                _values.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();

            public string Get(string key, string fallback = null) =>
                key != null && _values.TryGetValue(key, out var value) ? value : fallback;

            public void Set(string key, string value)
            {
                if (string.IsNullOrEmpty(key)) return;
                _values[key] = value ?? string.Empty;
            }

            public bool Remove(string key) =>
                !string.IsNullOrEmpty(key) && _values.Remove(key);
        }
    }
}
