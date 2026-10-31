using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// PCL2 ships Chinese-only text, so English arrives as a runtime translation layer keyed by
    /// the Chinese original. The built-in dictionary covers the launcher's main navigation,
    /// settings and swap pages; a disk copy at &lt;data&gt;\i18n\en-US.json (same shape) can extend
    /// or override it without rebuilding the payload.
    /// </summary>
    internal sealed partial class SwapI18n : II18n
    {
        private static readonly object Gate = new object();
        private static SwapI18n _current;

        private readonly Dictionary<string, string> _english =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private SwapLanguage _language;

        /// <summary>The process-wide instance the UI views and HostTranslator read.</summary>
        public static SwapI18n Current => _current ?? (_current = new SwapI18n());

        public SwapI18n()
        {
            foreach (var pair in BuiltInEnglish) _english[pair.Key] = pair.Value;
            ApplyExtraEnglish(_english);
            ApplyExtraPhrases(PhraseEnglish);
            LoadDiskOverrides();
            _language = LoadLanguage();
            SwapLog.Info($"[i18n] 词典 {_english.Count} 条，语言 {CurrentLanguage}（{_language}）。");
        }

        public event EventHandler LanguageChanged;

        public SwapLanguage Language => _language;

        public string CurrentLanguage => IsEnglish ? "en-US" : "zh-CN";

        public bool IsEnglish => _language == SwapLanguage.English ||
                                 (_language == SwapLanguage.Auto && SystemIsNonChinese());

        public int Count
        {
            get { lock (Gate) return _english.Count; }
        }

        public bool Has(string chinese) => Lookup(chinese) != null;

        public string T(string chinese)
        {
            if (string.IsNullOrEmpty(chinese) || !IsEnglish) return chinese;
            return Lookup(chinese) ?? chinese;
        }

        public string T(string chinese, string english) =>
            IsEnglish ? (english ?? chinese) : chinese;

        public void Register(string chinese, string english)
        {
            if (string.IsNullOrWhiteSpace(chinese) || string.IsNullOrEmpty(english)) return;
            lock (Gate) _english[chinese.Trim()] = english;
        }

        public int RegisterMany(IReadOnlyDictionary<string, string> entries)
        {
            if (entries == null || entries.Count == 0) return 0;
            var changed = 0;
            lock (Gate)
            {
                foreach (var pair in entries)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value))
                        continue;
                    var key = pair.Key.Trim();
                    if (_english.TryGetValue(key, out var current) &&
                        string.Equals(current, pair.Value, StringComparison.Ordinal))
                        continue;
                    _english[key] = pair.Value;
                    changed++;
                }
            }
            return changed;
        }

        public string Format(string chineseFormat, params object[] arguments)
        {
            var format = T(chineseFormat);
            if (arguments == null || arguments.Length == 0) return format;
            try
            {
                return string.Format(CultureInfo.CurrentUICulture, format, arguments);
            }
            catch (FormatException)
            {
                // A dictionary override with an invalid placeholder must not break the host UI.
                SwapLog.Warn("[i18n] 格式字符串占位符无效：" + (chineseFormat ?? string.Empty));
                return format;
            }
        }

        public bool TryTranslate(string chinese, out string translated)
        {
            translated = Lookup(chinese);
            return translated != null;
        }

        public void SetLanguage(SwapLanguage language)
        {
            var before = IsEnglish;
            var beforeSetting = _language;
            _language = language;
            SaveLanguage(language);
            SwapLog.Info($"[i18n] 语言切换为 {language}（生效：{CurrentLanguage}）。");
            var changed = beforeSetting != language || before != IsEnglish;
            if (changed)
                HostTranslator.ReapplyAll();
            if (changed) RaiseLanguageChanged();
        }

        private void RaiseLanguageChanged()
        {
            var handlers = LanguageChanged;
            if (handlers == null) return;
            foreach (var subscriber in handlers.GetInvocationList())
            {
                try { ((EventHandler)subscriber)(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    SwapLog.Warn("[i18n] LanguageChanged subscriber failed: " + ex.Message);
                }
            }
        }

        /// <summary>Translation by dictionary only - the host translator uses this per string.</summary>
        internal string Translate(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsEnglish) return text;
            var exact = Lookup(text);
            if (exact != null) return exact;

            // Dynamic rows ("错误：…", "钩子抛错 3 次…") carry runtime values, so the mapped part
            // is replaced phrase-by-phrase after the exact lookup misses.
            var updated = text;
            foreach (var pair in PhraseEnglish)
                updated = updated.Replace(pair.Key, pair.Value);

            // Version rows keep Chinese full-width parentheses around numbers ("（415.50）");
            // in English mode normalise just those numeric groups, leaving prose untouched.
            updated = System.Text.RegularExpressions.Regex.Replace(
                updated, "（(\\d+(?:\\.\\d+)*)）", "($1)");
            return updated;
        }

        /// <summary>English value -> Chinese original, so a language switch can revert cleanly.</summary>
        internal bool TryGetOriginal(string english, out string chinese)
        {
            chinese = null;
            if (string.IsNullOrEmpty(english)) return false;
            lock (Gate)
            {
                foreach (var pair in _english)
                {
                    if (string.Equals(pair.Value, english, StringComparison.Ordinal))
                    {
                        chinese = pair.Key;
                        return true;
                    }
                }
            }

            return false;
        }

        private string Lookup(string text)
        {
            lock (Gate)
            {
                if (_english.TryGetValue(text, out var exact)) return exact;
                var trimmed = text.Trim();
                if (trimmed.Length == text.Length) return null;
                return _english.TryGetValue(trimmed, out var relaxed) ? relaxed : null;
            }
        }

        private static bool SystemIsNonChinese()
        {
            try
            {
                var name = CultureInfo.CurrentUICulture.Name;
                return !string.IsNullOrEmpty(name) &&
                       !name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SettingsPath => Path.Combine(SwapPaths.DataRoot, "ui.json");

        private static SwapLanguage LoadLanguage()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return SwapLanguage.Auto;
                var text = File.ReadAllText(SettingsPath).Trim();
                if (text.IndexOf("\"en", StringComparison.OrdinalIgnoreCase) >= 0) return SwapLanguage.English;
                if (text.IndexOf("中文", StringComparison.Ordinal) >= 0) return SwapLanguage.Chinese;
                return SwapLanguage.Auto;
            }
            catch
            {
                return SwapLanguage.Auto;
            }
        }

        private static void SaveLanguage(SwapLanguage language)
        {
            try
            {
                Directory.CreateDirectory(SwapPaths.DataRoot);
                var value = language == SwapLanguage.English ? "en-US"
                    : language == SwapLanguage.Chinese ? "zh-CN" : "auto";
                File.WriteAllText(SettingsPath,
                    "{\"language\":\"" + value + "\"}", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 语言设置保存失败：" + ex.Message);
            }
        }

        private void LoadDiskOverrides()
        {
            try
            {
                var directory = Path.Combine(SwapPaths.DataRoot, "i18n");
                var files = new List<string>();
                var primary = Path.Combine(directory, "en-US.json");
                if (File.Exists(primary)) files.Add(primary);

                var packDirectory = Path.Combine(directory, "en-US");
                if (Directory.Exists(packDirectory))
                    files.AddRange(Directory.GetFiles(packDirectory, "*.json")
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

                var loaded = 0;
                foreach (var path in files)
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        var parsed = new System.Web.Script.Serialization.JavaScriptSerializer()
                            .Deserialize<Dictionary<string, string>>(json);
                        if (parsed == null) continue;
                        foreach (var pair in parsed) _english[pair.Key] = pair.Value;
                        loaded += parsed.Count;
                        SwapLog.Info("[i18n] 磁盘覆盖已加载：" + path + "（" + parsed.Count + " 条）。");
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("[i18n] 语言包加载失败：" + path + " | " + ex.Message);
                    }
                }
                if (loaded > 0)
                    SwapLog.Info("[i18n] 磁盘覆盖合计 " + loaded + " 条（目录 " + packDirectory + "）。");
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[i18n] 磁盘词典加载失败：" + ex.Message);
            }
        }

        /// <summary>
        /// Built-in Chinese -> English dictionary. Keys are the exact strings PCL renders, so the
        /// host translator can match them without touching layout.
        /// </summary>
        private static readonly Dictionary<string, string> BuiltInEnglish =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- top navigation and common chrome --------------------------------------
                { "启动", "Launch" },
                { "下载", "Download" },
                { "设置", "Settings" },
                { "更多", "More" },
                { "正版", "Online" },
                { "离线", "Offline" },
                { "启动日志", "Launch log" },
                { "版本列表", "Version list" },
                { "搜索", "Search" },
                { "搜索帮助", "Search help" },

                // ---- launch page -------------------------------------------------------------
                { "启动游戏", "Launch Game" },
                { "版本选择", "Versions" },
                { "版本设置", "Version Settings" },

                // ---- download page -----------------------------------------------------------
                { "原版游戏", "Vanilla" },
                { "整合包", "Modpacks" },
                { "数据包", "Data Packs" },
                { "资源包", "Resource Packs" },
                { "光影包", "Shader Packs" },
                { "最新版本", "Latest versions" },
                { "最新正式版", "Latest release" },
                { "最新预览版", "Latest snapshot" },
                { "正式版", "Releases" },
                { "预览版", "Snapshots" },
                { "远古版", "Ancient" },
                { "愚人节版", "April Fools" },

                // ---- more pages ---------------------------------------------------------------
                { "帮助", "Help" },
                { "关于与鸣谢", "About & Credits" },
                { "百宝箱", "Toolbox" },
                { "反馈", "Feedback" },
                { "新功能投票", "Feature Votes" },
                { "指南", "Guides" },
                { "百科", "Wiki" },
                { "启动器", "Launcher" },
                { "个性化", "Appearance" },
                { "关于", "About" },
                { "特别鸣谢", "Special thanks" },
                { "赞助 PCL!", "Sponsor PCL!" },
                { "赞助 PCL !", "Sponsor PCL!" },
                { "检查更新", "Check for updates" },
                { "当前版本", "Current version" },
                { "开发版", "Dev build" },

                // ---- settings sidebar and cards -------------------------------------------------
                { "其他", "Misc" },
                { "启动选项", "Launch options" },
                { "内存分配", "Memory allocation" },
                { "默认版本隔离", "Default version isolation" },
                { "游戏窗口标题", "Game window title" },
                { "自定义信息", "Custom info" },
                { "启动器可见性", "Launcher visibility" },
                { "进程优先级", "Process priority" },
                { "窗口大小", "Window size" },
                { "自动配置", "Automatic" },
                { "自定义", "Custom" },
                { "启动游戏前进行内存优化", "Optimize memory before launch" },
                { "已使用内存", "Memory in use" },
                { "游戏分配", "Allocated to game" },
                { "隔离所有版本", "Isolate every version" },
                { "默认", "Default" },
                { "游戏启动后仍保持不变", "Stay unchanged after launch" },
                { "高（优先保证游戏运行，游戏性能更佳，但可能造成其他程序卡顿）",
                  "High (game first; smoother game, other apps may stutter)" },
                { "普通", "Normal" },
                { "低", "Low" },
                { "常规", "General" },
                { "高级", "Advanced" },
                { "主题", "Theme" },
                { "深色模式", "Dark mode" },
                { "浅色模式", "Light mode" },
                { "正版登录", "Microsoft login" },
                { "离线登录", "Offline login" },
                { "用户名", "Username" },
                { "密码", "Password" },
                { "登录", "Sign in" },
                { "更多设置", "More settings" },
                { "启动器设置", "Launcher settings" },
                { "游戏设置", "Game settings" },

                // ---- help page ---------------------------------------------------------------
                { "Minecraft 教程", "Minecraft guide" },
                { "整合包制作指南", "Modpack guide" },
                { "资源安装指南", "Resource install guide" },
                { "由社区编写的 Minecraft 教程", "Community-written Minecraft guides" },
                { "介绍对整合包制作可能有用的 PCL 相关功能",
                  "PCL features that help with modpack building" },
                { "介绍如何安装下载到的各种游戏资源，例如 Mod、存档、纹理包（材质包）、光影等",
                  "How to install downloaded content such as mods, worlds, resource packs and shaders" },

                // ---- about page --------------------------------------------------------------
                { "Plain Craft Launcher 的开发者！", "Developer of Plain Craft Launcher!" },
                { "Plain Craft Launcher 的协力开发者！", "Co-developer of Plain Craft Launcher!" },
                { "提供了 Mod 名称的中文翻译和更多相关信息！",
                  "Chinese mod names and related metadata" },
                { "提供了 MCIM 社区资源镜像源和帮助图床！",
                  "MCIM community mirrors and image hosting" },
                { "赞助镜像源", "Sponsor mirror" },
                { "打开百科", "Open wiki" },
                { "MCIM 主页", "MCIM homepage" },

                // ---- common dialogs and buttons -----------------------------------------------
                { "确定", "OK" },
                { "取消", "Cancel" },
                { "安装", "Install" },
                { "更新", "Update" },
                { "关闭", "Close" },
                { "打开", "Open" },
                { "保存", "Save" },
                { "删除", "Delete" },
                { "重置", "Reset" },
                { "应用", "Apply" },
                { "是", "Yes" },
                { "否", "No" },
                { "你想向哪一方提交反馈?", "Which project is this feedback for?" },
                { "你想向哪一方提交反馈？", "Which project is this feedback for?" },
                { "PCL 启动器", "PCL Launcher" },

                // ---- swap pages ------------------------------------------------------------------
                { "宿主信息", "Host information" },
                { "交付形状", "Delivery shape" },
                { "API 版本", "API version" },
                { "进程 PID", "Process ID" },
                { "日志目录", "Log directory" },
                { "Addon 目录", "Addon directory" },
                { "查毒策略", "Scan policy" },
                { "查毒引擎", "Scan engines" },
                { "目录", "Directories" },
                { "界面语言", "Language" },
                { "跟随系统", "Follow system" },
                { "中文", "Chinese" },
                { "立即翻译宿主界面", "Translate launcher now" },
                { "恢复原始中文", "Restore original Chinese" },
                { "内置，默认启用（本机扫描，不联网）", "Built in, enabled by default (local scan, offline)" },
                { "检测到本机安装即参与", "Runs when installed locally" },
                { "需要 API Key，默认关闭", "Requires an API key; disabled by default" },
                { "打开日志目录", "Open log folder" },
                { "打开 Addon 目录", "Open addon folder" },
                { "已安装的 Addon", "Installed addons" },
                { "已启用", "Enabled" },
                { "已禁用", "Disabled" },
                { "内置", "Built-in" },
                { "详情", "Details" },
                { "启用", "Enable" },
                { "禁用", "Disable" },
                { "日志", "Log" },
                { "卸载", "Uninstall" },
                { "模块管理", "Module manager" },
                { "刷新", "Refresh" },
                { "宿主版本", "Host version" },
            };

        /// <summary>Partial replacements for rows whose tail is runtime data.</summary>
        private static readonly Dictionary<string, string> PhraseEnglish =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "错误：", "Error: " },
                { "钩子抛错 ", "Hook errors: " },
                { "次（失败的钩子已自动停用，详见该模块日志）",
                  " (failed hooks were disabled; see the module log)" },
                { "  ·  内置", "  ·  Built-in" },
                { "  ·  钩子 ", "  ·  hooks: " },
                { "  ·  已安装 ", "  ·  installed " },
                { "管理 Swap 插件 (", "Manage Swap addons (" },
                { "宿主信息与 Swap 设置", "Host info & Swap settings" },
                { "登录加载已开始", "Login loading started" },
                { "登录方式：离线（Dissociate）", "Login: Offline (Dissociate)" },
                { "登录加载已结束", "Login loading finished" },
                { "最新正式版，发布于 ", "Latest release, released " },
                { "最新预览版，发布于 ", "Latest snapshot, released " },
                { "提供 BMCLAPI 镜像源和 Forge 安装工具，详见 ",
                  "Provides the BMCLAPI mirror and Forge installer, see " },
                { "版本 ", "Version " },
                { " · 下载恢复、静默更新拦截与云下载查毒沙箱",
                  " · Download restore, silent OTA blocking and cloud-download AV sandbox" },
                { "当前版本：", "Current version: " },
                { "正式版 ", "Release " },
                { "提供了 ", "Provides " },
                { "社区资源镜像源和帮助图床！", " community mirrors and image hosting!" },
                { "MCIM 社区资源镜像源和帮助图床！", "MCIM community mirrors and image hosting!" },
                { "MCIM 社区资源镜像源和帮助图床", "MCIM community mirrors and image hosting" },
                { "尚未发现任何 Addon。把 .swapaddon 包放进 Addon 目录后重启 PCL 即可加载。",
                  "No addons yet. Drop a .swapaddon package into the addon directory and restart PCL." },
                { "安装模块…", "Install module..." },
                { "钩子链（模块作用域）", "Hook chain (module scope)" },
                { "同一个宿主方法只有一条链：第一个钩子负责装 gate，后续模块加入这条链；链上 handler 全部失效时调用直通原方法。",
                  "Each host method has one chain: the first hook installs the gate, later modules join it, and once every handler is inactive the call goes straight to the original." },
                { "说明", "Notes" },
                { "开关立即生效并写入 modules.json（内置功能的钩子按开关放行/拦截）；模块状态、配置与已安装的 Addon 都放在数据目录里，可在 Swap Settings 打开。",
                  "Toggles apply immediately and persist to modules.json (built-in hooks gate on them); module state, config and installed addons live under the data directory, reachable from Swap Settings." },
                { "注意：结构类修复（原版游戏页 / 下载侧栏）是启动时对宿主对象的一次性修复，关闭模块后要重启 PCL 才会完全回退；OTA、弹窗类拦截则立即停用。",
                  "Note: structural repairs (vanilla download page / sidebar) are applied once at startup, so fully reverting them needs a PCL restart; OTA and dialog hooks stop immediately." },
                { "安装会把包归档到 Addon 目录并立即启动，不需要重启 PCL；卸载会停掉模块、撤下它的设置页并删除模块目录（入口 DLL 被占用时留到下次启动清理）。",
                  "Installing archives the package into the addon directory and loads it immediately; uninstalling stops the module, removes its pages and deletes its directory (a locked entry DLL is cleaned up on the next start)." },
            };
    }
}
