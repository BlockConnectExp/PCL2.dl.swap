using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>Read-only, path-confined view over an addon's packaged assets directory.</summary>
    internal sealed class AddonAssetStore : IAddonAssets
    {
        private readonly string _root;

        public AddonAssetStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                _root = string.Empty;
                return;
            }
            var full = Path.GetFullPath(root);
            _root = Directory.Exists(full) ? full : string.Empty;
        }

        public string Root => _root;

        public IReadOnlyList<AddonAssetInfo> Assets
        {
            get
            {
                if (!Directory.Exists(_root)) return Array.Empty<AddonAssetInfo>();
                return Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderBy(info => info.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(info => new AddonAssetInfo
                    {
                        Path = Relative(info.FullName),
                        Length = info.Length,
                        Sha256 = Hash(info.FullName)
                    })
                    .ToList();
            }
        }

        public IReadOnlyList<string> List(string relativeDirectory = null, string searchPattern = "*",
            bool recursive = true)
        {
            if (string.IsNullOrEmpty(_root)) return Array.Empty<string>();
            var directory = Resolve(relativeDirectory);
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.GetFiles(directory,
                    string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(Relative)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool Exists(string relativePath)
        {
            if (string.IsNullOrEmpty(_root)) return false;
            var path = Resolve(relativePath);
            return File.Exists(path) || Directory.Exists(path);
        }

        public string GetPath(string relativePath) => Resolve(relativePath, requireFile: true);

        public string ReadText(string relativePath) =>
            File.ReadAllText(Resolve(relativePath, requireFile: true), Encoding.UTF8);

        public byte[] ReadBytes(string relativePath) =>
            File.ReadAllBytes(Resolve(relativePath, requireFile: true));

        public Stream OpenRead(string relativePath) =>
            File.Open(Resolve(relativePath, requireFile: true), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);

        private string Resolve(string relativePath, bool requireFile = false)
        {
            if (string.IsNullOrEmpty(_root))
                throw new FileNotFoundException("This module does not ship an assets directory.",
                    relativePath);
            var relative = (relativePath ?? string.Empty).Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative))
                throw new ArgumentException("Asset paths must be relative to the package.", nameof(relativePath));

            var full = Path.GetFullPath(Path.Combine(_root, relative));
            var basePath = _root.TrimEnd(Path.DirectorySeparatorChar);
            var prefix = basePath + Path.DirectorySeparatorChar;
            if (!string.Equals(full, basePath, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Asset path escaped the package root.");
            if (requireFile && !File.Exists(full))
                throw new FileNotFoundException("Addon asset not found.", relativePath);
            return full;
        }

        private string Relative(string path) =>
            path.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty)
                    .ToLowerInvariant();
        }
    }

    /// <summary>
    /// Per-module translation catalog. It deliberately never mutates the process-wide host
    /// dictionary, so unload/disable requires no rollback and cannot leave stale translations.
    /// </summary>
    internal sealed class AddonLocalizationStore : IAddonLocalization
    {
        private readonly string _i18nDirectory;
        private readonly SwapI18n _host;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Dictionary<string, string>> _packs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _registered =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _active;
        private string _activeLocale;

        public AddonLocalizationStore(string packageDirectory, SwapI18n host)
        {
            _i18nDirectory = string.IsNullOrWhiteSpace(packageDirectory)
                ? string.Empty
                : Path.Combine(Path.GetFullPath(packageDirectory), "i18n");
            _host = host ?? SwapI18n.Current;
            Reload();
        }

        public event EventHandler Changed;

        public string CurrentLocale => _host.IsEnglish ? "en-US" : "zh-CN";
        public bool IsEnglish => _host.IsEnglish;

        public IReadOnlyList<string> AvailableLocales
        {
            get
            {
                lock (_gate)
                    return _packs.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public int Count
        {
            get
            {
                lock (_gate) return ActiveDictionary().Count;
            }
        }

        public string T(string chinese, string english = null)
        {
            if (string.IsNullOrEmpty(chinese)) return chinese;
            lock (_gate)
            {
                if (ActiveDictionary().TryGetValue(chinese, out var value) &&
                    !string.IsNullOrEmpty(value))
                    return value;
            }

            if (_host.IsEnglish)
            {
                if (!string.IsNullOrEmpty(english)) return english;
                return _host.Translate(chinese);
            }
            return chinese;
        }

        public string Format(string chineseFormat, string englishFormat, params object[] arguments)
        {
            var template = _host.IsEnglish ? (englishFormat ?? chineseFormat) : chineseFormat;
            var translated = T(chineseFormat, template);
            if (arguments == null || arguments.Length == 0) return translated;
            try { return string.Format(CultureInfo.CurrentUICulture, translated, arguments); }
            catch (FormatException)
            {
                SwapLog.Warn("[i18n] addon format string is invalid: " + (chineseFormat ?? string.Empty));
                return translated;
            }
        }

        public bool TryTranslate(string chinese, out string translated)
        {
            translated = null;
            if (string.IsNullOrEmpty(chinese)) return false;
            lock (_gate)
            {
                if (ActiveDictionary().TryGetValue(chinese, out translated) &&
                    !string.IsNullOrEmpty(translated))
                    return true;
            }
            if (_host.IsEnglish && _host.TryTranslate(chinese, out translated)) return true;
            translated = null;
            return false;
        }

        public void Register(string chinese, string english)
        {
            if (string.IsNullOrWhiteSpace(chinese) || string.IsNullOrEmpty(english)) return;
            lock (_gate)
            {
                _registered[chinese.Trim()] = english;
                _active = null;
            }
        }

        public int RegisterMany(IReadOnlyDictionary<string, string> entries)
        {
            if (entries == null || entries.Count == 0) return 0;
            var changed = 0;
            lock (_gate)
            {
                foreach (var pair in entries)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
                    var key = pair.Key.Trim();
                    if (_registered.TryGetValue(key, out var current) &&
                        string.Equals(current, pair.Value, StringComparison.Ordinal))
                        continue;
                    _registered[key] = pair.Value;
                    changed++;
                }
                if (changed > 0) _active = null;
            }
            return changed;
        }

        public bool Reload()
        {
            var loaded = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!string.IsNullOrEmpty(_i18nDirectory) && Directory.Exists(_i18nDirectory))
                {
                    foreach (var path in Directory.GetFiles(_i18nDirectory, "*.json",
                                 SearchOption.TopDirectoryOnly)
                                 .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var locale = Path.GetFileNameWithoutExtension(path);
                            var parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                                .Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
                            if (parsed == null) continue;
                            loaded[locale] = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                        }
                        catch (Exception ex)
                        {
                            SwapLog.Warn("[i18n] addon language pack failed: " + path + " | " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] addon language directory failed: " + ex.Message);
            }

            var changed = false;
            lock (_gate)
            {
                changed = loaded.Count != _packs.Count ||
                          loaded.Any(pair => !PackEquals(_packs.TryGetValue(pair.Key,
                              out var current) ? current : null, pair.Value));
                _packs.Clear();
                foreach (var pair in loaded) _packs[pair.Key] = pair.Value;
                _active = null;
            }

            EventHandler handlers;
            lock (_gate) handlers = Changed;
            if (changed && handlers != null)
            {
                foreach (EventHandler handler in handlers.GetInvocationList())
                {
                    try { handler(this, EventArgs.Empty); }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("[i18n] addon Localization.Changed subscriber failed: " + ex.Message);
                    }
                }
            }
            return changed;
        }

        private static bool PackEquals(Dictionary<string, string> left,
            Dictionary<string, string> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            return right.All(pair => left.TryGetValue(pair.Key, out var value) &&
                                     string.Equals(value, pair.Value, StringComparison.Ordinal));
        }

        private Dictionary<string, string> ActiveDictionary()
        {
            var locale = CurrentLocale;
            if (_active != null && string.Equals(_activeLocale, locale, StringComparison.OrdinalIgnoreCase))
                return _active;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            _packs.TryGetValue(locale, out var pack);
            if (pack != null)
                foreach (var pair in pack) result[pair.Key] = pair.Value;
            if (IsEnglish)
                foreach (var pair in _registered) result[pair.Key] = pair.Value;
            _activeLocale = locale;
            _active = result;
            return _active;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
