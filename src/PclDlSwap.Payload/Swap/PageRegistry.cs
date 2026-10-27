using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using PCL2Swap.Api;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Registers settings pages into PCL's 设置 sidebar. Mechanism (measured against 2.99.0):
    ///   - a detour on PageSetupLeft.PageCheck intercepts our sidebar entries (page ids 100+),
    ///   - official navigation semantics are replayed (AniControlEnabled / IsPageSwitched /
    ///     PageChangeRun / PageID) because PageChange's switch throws on unknown ids,
    ///   - each page is a plain PCL.MyPageRight instance whose Child is the addon's view
    ///     (the class constructor builds no UI; animations follow PanContent ?? Child).
    /// </summary>
    internal static class PageRegistry
    {
        private const int FirstPageId = 100;

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, PageEntry> Pages = new Dictionary<int, PageEntry>();
        private static int _nextPageId = FirstPageId;
        private static Assembly _host;
        private static DetourInstaller _pageCheckDetour;
        private static volatile bool _installed;
        private static volatile bool _sidebarDone;

        private sealed class PageEntry
        {
            public int Id;
            public string LogicalId;
            public string Title;
            public Func<FrameworkElement> Factory;
            public object Instance;
        }

        /// <summary>Registers the two platform pages first so they own ids 100 and 101.</summary>
        public static void RegisterBuiltInPages(ISwapApi api)
        {
            RegisterPage("swap-addons", "Swap Addon", () => Views.SwapAddonView.Build(api, SwapHost.Addons));
            RegisterPage("swap-settings", "Swap Settings", () => Views.SwapSettingsView.Build(api));
        }

        /// <summary>Addon-facing registration; ids are assigned in registration order.</summary>
        public static void RegisterPage(string logicalId, string title, Func<FrameworkElement> factory)
        {
            if (factory == null) return;
            lock (Gate)
            {
                if (Pages.Values.Any(entry => entry.LogicalId == logicalId)) return;
                var id = _nextPageId++;
                Pages[id] = new PageEntry
                {
                    Id = id,
                    LogicalId = logicalId,
                    Title = string.IsNullOrEmpty(title) ? logicalId : title,
                    Factory = factory
                };
                SwapLog.Info($"Swap 页面：已登记“{(string.IsNullOrEmpty(title) ? logicalId : title)}”（PageID {id}）。");
            }
        }

        public static void Install(Assembly host)
        {
            lock (Gate)
            {
                if (_installed) return;
                _installed = true;
            }

            _host = host ?? Assembly.GetEntryAssembly();
            if (_host == null)
            {
                SwapLog.Warn("Swap 页面：宿主程序集为空，跳过安装。");
                return;
            }

            InstallPageCheckDetour();
            StartSidebarWatcher();
        }

        // ------------------------------------------------------------------ hook

        private static void InstallPageCheckDetour()
        {
            try
            {
                var type = _host.GetType("PCL.PageSetupLeft", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "PageCheck" &&
                                                 candidate.GetParameters().Length == 2);
                if (method == null)
                {
                    SwapLog.Warn("Swap 页面：未找到 PageSetupLeft.PageCheck，页面入口未安装。");
                    return;
                }

                var hook = typeof(PageRegistry).GetMethod(nameof(HookPageCheck),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _pageCheckDetour = DetourInstaller.Install(method, hook);
                SwapLog.Info("Swap 页面：PageCheck 钩子已安装（" + _pageCheckDetour.Verify() + "）");
            }
            catch (Exception ex)
            {
                SwapLog.Error("Swap 页面：PageCheck 钩子安装失败：" + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookPageCheck(object self, object sender, object eventArgs)
        {
            try
            {
                var tag = PclUi.GetMember(sender, "Tag");
                if (tag != null && int.TryParse(Convert.ToString(tag), out var id) && IsOurPage(id))
                {
                    SwitchToPage(self, id);
                    return;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("Swap 页面：PageCheck 钩子异常：" + ex);
            }

            _pageCheckDetour.InvokeOriginal(self, sender, eventArgs);
        }

        // ------------------------------------------------------------------ navigation

        private static bool IsOurPage(int id)
        {
            lock (Gate) return Pages.ContainsKey(id);
        }

        private static void SwitchToPage(object pageLeft, int id)
        {
            lock (Gate)
            {
                try
                {
                    var page = GetOrCreatePage(id);
                    if (page == null) return;

                    var aniType = _host.GetType("PCL.ModAnimation", throwOnError: false);
                    object aniBefore = null;
                    if (aniType != null)
                    {
                        aniBefore = PclUi.GetMember(aniType, "AniControlEnabled");
                        if (aniBefore is int value) PclUi.SetMember(aniType, "AniControlEnabled", value + 1);
                    }

                    try
                    {
                        // Official PageChange semantics; writing PageID alone is not enough
                        // (the switch would throw on our ids - this is the same class of bug as
                        // the 2.99.0.1 sidebar crash fixed in Alpha 2).
                        PclUi.SetMember(pageLeft, "IsPageSwitched", true);
                        var run = pageLeft.GetType().GetMethod("PageChangeRun",
                            BindingFlags.NonPublic | BindingFlags.Static);
                        run?.Invoke(null, new[] { page });

                        var idType = PclUi.GetMember(pageLeft, "PageID")?.GetType()
                                     ?? _host.GetType("PCL.FormMain+PageSubType");
                        PclUi.SetMember(pageLeft, "PageID",
                            idType == null ? (object)id : Enum.ToObject(idType, id));
                    }
                    finally
                    {
                        if (aniType != null && aniBefore is int original)
                            PclUi.SetMember(aniType, "AniControlEnabled", original);
                    }

                    SwapLog.Info($"Swap 页面：已切换到 PageID {id}。");
                }
                catch (Exception ex)
                {
                    SwapLog.Error("Swap 页面：切换失败：" + ex);
                }
            }
        }

        private static object GetOrCreatePage(int id)
        {
            PageEntry entry;
            lock (Gate)
            {
                if (!Pages.TryGetValue(id, out entry)) return null;
                if (entry.Instance != null) return entry.Instance;
            }

            var pageType = _host.GetType("PCL.MyPageRight", throwOnError: false);
            if (pageType == null)
            {
                SwapLog.Warn("Swap 页面：宿主缺少 MyPageRight。");
                return null;
            }

            var page = Activator.CreateInstance(pageType);
            FrameworkElement view = null;
            try { view = entry.Factory(); }
            catch (Exception ex) { SwapLog.Error("Swap 页面：视图构建失败：" + ex); }
            if (view != null) PclUi.SetMember(page, "Child", view);

            lock (Gate) { entry.Instance = page; }
            return page;
        }

        // ------------------------------------------------------------------ sidebar

        private static void StartSidebarWatcher()
        {
            var watcher = new Thread(() =>
            {
                var deadline = DateTime.UtcNow.AddMinutes(30);
                while (!_sidebarDone && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var modMain = _host.GetType("PCL.ModMain", throwOnError: false);
                        var pageSetupLeft = PclUi.GetMember(modMain, "FrmSetupLeft");
                        if (pageSetupLeft != null) InsertSidebarItems(pageSetupLeft);
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Error("Swap 页面：侧栏轮询异常：" + ex.Message);
                        _sidebarDone = true;
                    }

                    if (!_sidebarDone) Thread.Sleep(300);
                }

                if (!_sidebarDone) SwapLog.Warn("Swap 页面：等待设置侧栏超时（30 分钟）。");
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.SwapSidebar"
            };
            watcher.Start();
        }

        private static void InsertSidebarItems(object pageSetupLeft)
        {
            PclUi.OnUiThread(() =>
            {
                try
                {
                    if (_sidebarDone) return;

                    var panItem = PclUi.GetMember(pageSetupLeft, "PanItem");
                    var children = panItem == null ? null : PclUi.GetMember(panItem, "Children");
                    var add = children?.GetType().GetMethods()
                        .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
                    var itemType = _host.GetType("PCL.MyListItem", throwOnError: false);
                    if (panItem == null || add == null || itemType == null)
                    {
                        SwapLog.Warn("Swap 页面：侧栏容器或 MyListItem 尚未就绪，稍后重试。");
                        return;
                    }

                    var checkType = _host.GetType("PCL.MyListItem+CheckType", throwOnError: false);
                    var added = 0;
                    foreach (var entry in Snapshot())
                    {
                        var item = Activator.CreateInstance(itemType);
                        PclUi.SetProperty(item, "Title", entry.Title);
                        PclUi.SetProperty(item, "Info", BuildItemInfo(entry));
                        if (checkType != null)
                            PclUi.SetProperty(item, "Type", Enum.Parse(checkType, "RadioBox"));
                        PclUi.SetMember(item, "Tag", entry.Id.ToString());
                        SubscribeCheck(item, itemType);
                        add.Invoke(children, new[] { item });
                        added++;
                    }

                    _sidebarDone = true;
                    SwapLog.Info($"Swap 页面：已在设置侧栏插入 {added} 个条目。");
                }
                catch (Exception ex)
                {
                    SwapLog.Error("Swap 页面：插入侧栏项失败：" + ex);
                    _sidebarDone = true;
                }
            });
        }

        private static string BuildItemInfo(PageEntry entry)
        {
            if (entry.LogicalId == "swap-addons")
                return "管理 Swap 插件（" + (SwapHost.Addons?.Records.Count ?? 0) + "）";
            if (entry.LogicalId == "swap-settings")
                return "宿主信息与 Swap 设置";
            return "Swap 扩展页面";
        }

        private static void SubscribeCheck(object item, Type itemType)
        {
            try
            {
                var checkEvent = itemType.GetEvent("Check", BindingFlags.Public | BindingFlags.Instance);
                if (checkEvent == null) return;
                var handler = Delegate.CreateDelegate(checkEvent.EventHandlerType,
                    typeof(PageRegistry).GetMethod(nameof(OnSidebarCheck),
                        BindingFlags.NonPublic | BindingFlags.Static));
                checkEvent.AddEventHandler(item, handler);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("Swap 页面：侧栏项事件订阅失败：" + ex.Message);
            }
        }

        internal static void OnSidebarCheck(object sender, object e)
        {
            try
            {
                var tag = PclUi.GetMember(sender, "Tag");
                if (tag == null || !int.TryParse(Convert.ToString(tag), out var id) || !IsOurPage(id)) return;

                var modMain = _host.GetType("PCL.ModMain", throwOnError: false);
                var pageSetupLeft = PclUi.GetMember(modMain, "FrmSetupLeft");
                if (pageSetupLeft != null) SwitchToPage(pageSetupLeft, id);
            }
            catch (Exception ex)
            {
                SwapLog.Error("Swap 页面：侧栏点击处理失败：" + ex);
            }
        }

        private static List<PageEntry> Snapshot()
        {
            lock (Gate) return Pages.Values.OrderBy(entry => entry.Id).ToList();
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
