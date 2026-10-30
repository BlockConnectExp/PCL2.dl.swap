using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
        private static DetourInstaller _pageGetDetour;
        private static volatile bool _installed;
        private static volatile bool _sidebarDone;
        private static int _pageIdWriteSkipped;
        private static readonly List<object> _insertedItems = new List<object>();

        /// <summary>
        /// True while the host's <c>PageSetupLeft.PageGet</c> is under our detour. Writing one of
        /// our page ids into the host's PageID field is only safe in that state - the host's own
        /// switch throws for unknown ids, which is exactly the "未知的设置子页面种类：101" crash.
        /// </summary>
        private static bool PageIdWritable => _pageGetDetour != null;

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

        /// <summary>
        /// Drops a page (used when a module is uninstalled) and rebuilds the sidebar. The page id is
        /// never reused, so a stale PageID that still points at it is answered by the PageGet
        /// fallback instead of reaching the host's throwing switch.
        /// </summary>
        public static bool UnregisterPage(string logicalId)
        {
            PageEntry removed = null;
            lock (Gate)
            {
                removed = Pages.Values.FirstOrDefault(entry => entry.LogicalId == logicalId);
                if (removed != null) Pages.Remove(removed.Id);
            }
            if (removed == null) return false;

            SwapLog.Info($"[page] 已注销“{removed.Title}”（PageID {removed.Id}）");
            RefreshSidebar(new[] { removed.Id });
            return true;
        }

        /// <summary>
        /// Drops the cached page instance so the next visit rebuilds its view, and re-enters the
        /// page right away when the user is looking at it. Used by the module toggles so the list
        /// reflects the new state without a restart.
        /// </summary>
        public static void InvalidatePage(string logicalId)
        {
            PageEntry entry;
            lock (Gate) entry = Pages.Values.FirstOrDefault(candidate => candidate.LogicalId == logicalId);
            if (entry == null) return;

            lock (Gate) entry.Instance = null;

            try
            {
                var modMain = _host?.GetType("PCL.ModMain", throwOnError: false);
                var pageLeft = PclUi.GetMember(modMain, "FrmSetupLeft");
                if (pageLeft == null) return;

                var current = Convert.ToInt32(PclUi.GetMember(pageLeft, "PageID") ?? -1);
                if (current != entry.Id) return;

                SwapLog.Info($"[page] 模块状态变化：重建并重新进入 {entry.LogicalId}（id={entry.Id}）");
                SwitchToPage(pageLeft, entry.Id);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[page] 页面刷新失败：" + ex.Message);
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
            InstallPageGetDetour();
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

        /// <summary>
        /// PCL resolves the settings sub-page through PageGet whenever the user re-enters 设置
        /// (FormMain.PageChangeActual -> FrmSetupLeft.PageGet(SubType = -1)), and PageGet's own
        /// switch throws for any id it does not know. Without this hook the leftover PageID
        /// 100/101 from our switch surfaces as "未知的设置子页面种类：101" the moment the user
        /// leaves 设置 and comes back (field log 02:32:50). Returning the cached page keeps the
        /// launcher's own导航 semantics intact and restores the Swap page on re-entry.
        /// The enum parameter travels as int32; the original needs the boxed enum from the
        /// target signature, so it is rebuilt before the fallthrough call.
        ///
        /// If this hook cannot be installed, <see cref="SwitchToPage"/> stops writing our id into
        /// the host's PageID field (see <c>PageIdWritable</c>): a leftover unknown PageID is the
        /// only thing that can make the *host* throw, so the failure degrades to "re-entering 设置
        /// shows a native sub-page" instead of crashing the launcher.
        /// </summary>
        private static void InstallPageGetDetour()
        {
            try
            {
                var type = _host.GetType("PCL.PageSetupLeft", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "PageGet" &&
                                                 candidate.GetParameters().Length == 1);
                if (method == null)
                {
                    SwapLog.Warn("Swap 页面：未找到 PageSetupLeft.PageGet，重进设置页可能报未知子页面。");
                    return;
                }

                var hook = typeof(PageRegistry).GetMethod(nameof(HookPageGet),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _pageGetDetour = DetourInstaller.Install(method, hook);
                SwapLog.Info("Swap 页面：PageGet 钩子已安装（" + _pageGetDetour.Verify() + "）");
            }
            catch (Exception ex)
            {
                SwapLog.Error("Swap 页面：PageGet 钩子安装失败：" + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookPageCheck(object self, object sender, object eventArgs)
        {
            try
            {
                var tag = PclUi.GetMember(sender, "Tag");
                SwapLog.Info($"[page] PageCheck 钩子进入（tag={tag ?? "(null)"}，" +
                             $"sender={sender?.GetType().Name ?? "null"}，线程 {Thread.CurrentThread.ManagedThreadId}）");
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object HookPageGet(object self, int id)
        {
            try
            {
                var pageId = id;
                if (pageId < 0)
                    pageId = Convert.ToInt32(PclUi.GetMember(self, "PageID") ?? -1);

                if (IsOurPage(pageId))
                {
                    SwapLog.Info($"[page] PageGet 命中 Swap 页（id={pageId}），返回已缓存的页面实例。");
                    var page = GetOrCreatePage(pageId);
                    if (page != null) return page;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("[page] PageGet 钩子异常：" + ex);
            }

            var parameters = _pageGetDetour?.Method?.GetParameters();
            var parameterType = parameters != null && parameters.Length > 0 ? parameters[0].ParameterType : null;
            object original = id;
            if (parameterType != null && parameterType.IsEnum)
            {
                original = Enum.ToObject(parameterType, id);
                if (id >= 0 && !Enum.IsDefined(parameterType, original))
                {
                    // The host's own switch throws for ids outside its enum, and FormMain.PageChangeActual
                    // resolves the leftover PageID through here - a stale Swap id would surface as
                    // "未知的设置子页面种类：N". Degrade to the host's first/default sub-page instead.
                    var names = Enum.GetNames(parameterType);
                    var fallbackName = names.Contains("Default") ? "Default" : names.FirstOrDefault();
                    if (fallbackName != null)
                    {
                        SwapLog.Warn($"[page] PageGet({id}) 不是宿主已知的设置子页，按 {fallbackName} 处理，避免宿主抛异常。");
                        original = Enum.Parse(parameterType, fallbackName);
                    }
                }
            }
            return _pageGetDetour.InvokeOriginal(self, original);
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
                SwapLog.Info($"[page] SwitchToPage 进入（id={id}，线程 {Thread.CurrentThread.ManagedThreadId}）");
                try
                {
                    var page = GetOrCreatePage(id);
                    if (page == null) return;

                    var aniType = _host.GetType("PCL.ModAnimation", throwOnError: false);
                    object aniBefore = null;
                    if (aniType != null)
                    {
                        // The animation gate raises while the switch is in flight; a failure here
                        // must degrade to "no gate" instead of aborting the whole page change.
                        try
                        {
                            aniBefore = PclUi.GetMember(aniType, "AniControlEnabled");
                            if (aniBefore is int value) PclUi.SetMember(aniType, "AniControlEnabled", value + 1);
                        }
                        catch (Exception ex)
                        {
                            aniBefore = null;
                            SwapLog.Warn("[page] 动画门控加减失败（继续切换）：" + ex.Message);
                        }
                    }

                    try
                    {
                        // Official PageChange semantics; writing PageID alone is not enough
                        // (the switch would throw on our ids - this is the same class of bug as
                        // the 2.99.0.1 sidebar crash fixed in Alpha 2).
                        PclUi.SetMember(pageLeft, "IsPageSwitched", true);
                        SwapLog.Info("[page] 调用宿主的 PageChangeRun（含 130ms 动画序列）");
                        var run = pageLeft.GetType().GetMethod("PageChangeRun",
                            BindingFlags.NonPublic | BindingFlags.Static);
                        run?.Invoke(null, new[] { page });

                        if (PageIdWritable)
                        {
                            var idType = PclUi.GetMember(pageLeft, "PageID")?.GetType()
                                         ?? _host.GetType("PCL.FormMain+PageSubType");
                            PclUi.SetMember(pageLeft, "PageID",
                                idType == null ? (object)id : Enum.ToObject(idType, id));
                        }
                        else if (Interlocked.Increment(ref _pageIdWriteSkipped) == 1)
                        {
                            SwapLog.Warn("[page] PageGet 钩子不可用，本次运行不再把 Swap 的 PageID 写进宿主：" +
                                         "再次进入设置页会回到宿主自己的子页，而不是报“未知的设置子页面种类”。");
                        }
                    }
                    finally
                    {
                        if (aniType != null && aniBefore is int original)
                            PclUi.SetMember(aniType, "AniControlEnabled", original);
                    }

                    SwapLog.Info($"[page] SwitchToPage 同步部分完成（id={id}）。");
                }
                catch (Exception ex)
                {
                    SwapLog.Error($"[page] SwitchToPage 失败（id={id}）：" + ex);
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
            try
            {
                SwapLog.Info($"[page] 构建视图（id={id}，{entry.LogicalId}）");
                // WPF controls must be created on an STA thread. The host calls us from its UI
                // thread, but PageGet can also arrive from a worker (or from the console harness),
                // so marshal the factory through the host's Dispatcher when needed.
                view = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
                    ? entry.Factory()
                    : PclUi.OnUiThread(entry.Factory, null);
                if (view == null)
                    SwapLog.Warn($"[page] 视图构建没有返回内容（id={id}，{entry.LogicalId}）。");
            }
            catch (Exception ex) { SwapLog.Error("Swap 页面：视图构建失败：" + ex); }
            if (view != null)
            {
                // Mirror the shape of PCL's own pages: PanContent carries the view (animations use
                // "PanContent ?? Child") and PanAlways gets an empty container so no host helper
                // ever has to reason about a null field.
                try { PclUi.SetMember(page, "PanAlways", new Grid()); } catch { }
                try { PclUi.SetMember(page, "PanContent", view); } catch { }
                PclUi.SetMember(page, "Child", view);
            }
            SwapLog.Info($"[page] 页面实例已创建（id={id}，view={view?.GetType().FullName ?? "null"}）");

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
            if (_sidebarDone) return;
            PclUi.OnUiThread(() => InsertSidebarItemsCore(pageSetupLeft));
        }

        /// <summary>Inserts the current page set; must already be running on the host UI thread.</summary>
        private static void InsertSidebarItemsCore(object pageSetupLeft)
        {
            try
            {
                if (_sidebarDone) return;
                if (_pageCheckDetour == null)
                {
                    // Without our PageCheck detour the host's own handler would run
                    // PageChange(<our id>) and throw; better to show no entry at all.
                    SwapLog.Error("Swap 页面：PageCheck 钩子未安装，已跳过侧栏插入（否则点击会掉进宿主的未知子页面分支）。");
                    _sidebarDone = true;
                    return;
                }

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
                    DisableHoverScale(item);
                    if (checkType != null)
                        PclUi.SetProperty(item, "Type", Enum.Parse(checkType, "RadioBox"));
                    PclUi.SetMember(item, "Tag", entry.Id.ToString());
                    SubscribeCheck(item, itemType);
                    add.Invoke(children, new[] { item });
                    _insertedItems.Add(item);
                    added++;
                }

                _sidebarDone = true;
                SwapLog.Info($"Swap 页面：已在设置侧栏插入 {added} 个条目（悬停缩放动画已关闭，" +
                             "与 PCL 自带设置项一致）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("Swap 页面：插入侧栏项失败：" + ex);
                _sidebarDone = true;
            }
        }

        /// <summary>
        /// Rebuilds the Swap section of the settings sidebar after pages were added or removed, and
        /// pops back to the host's own sub-page when the page the user is looking at just vanished.
        /// Runs on the host UI thread; safe to call from a module lifecycle call.
        /// </summary>
        public static void RefreshSidebar(IEnumerable<int> removedPageIds = null)
        {
            try
            {
                var modMain = _host?.GetType("PCL.ModMain", throwOnError: false);
                var pageSetupLeft = PclUi.GetMember(modMain, "FrmSetupLeft");
                if (pageSetupLeft == null) return;   // sidebar not created yet: the watcher will fill it

                PclUi.OnUiThread(() =>
                {
                    try
                    {
                        var removed = removedPageIds == null
                            ? null
                            : new HashSet<int>(removedPageIds);
                        if (removed != null && removed.Count > 0)
                            LeaveRemovedPage(pageSetupLeft, removed);
                        RemoveInsertedItems(pageSetupLeft);
                        InsertSidebarItemsCore(pageSetupLeft);
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Error("[page] 重建设置侧栏失败：" + ex);
                    }
                });
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[page] RefreshSidebar 失败：" + ex.Message);
            }
        }

        /// <summary>Drops the items we inserted so the rebuild starts from the host's own rows.</summary>
        private static void RemoveInsertedItems(object pageSetupLeft)
        {
            if (_insertedItems.Count == 0) return;
            var panItem = PclUi.GetMember(pageSetupLeft, "PanItem");
            var children = panItem == null ? null : PclUi.GetMember(panItem, "Children");
            var remove = children?.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "Remove" && m.GetParameters().Length == 1);
            if (children == null || remove == null) return;

            foreach (var item in _insertedItems)
            {
                try { remove.Invoke(children, new[] { item }); }
                catch (Exception ex) { SwapLog.Warn("[page] 移除侧栏项失败：" + ex.Message); }
            }
            _insertedItems.Clear();
            _sidebarDone = false;
        }

        /// <summary>
        /// The host resolves its current sub-page through PageID on every entry into 设置; if that id
        /// belonged to a module we just uninstalled, hand the host back its own Default page first.
        /// </summary>
        private static void LeaveRemovedPage(object pageSetupLeft, HashSet<int> removedIds)
        {
            try
            {
                var current = Convert.ToInt32(PclUi.GetMember(pageSetupLeft, "PageID") ?? -1);
                if (!removedIds.Contains(current)) return;

                var subType = _host.GetType("PCL.FormMain+PageSubType", throwOnError: false);
                if (subType == null) return;
                var defaultId = Enum.Parse(subType, "Default");
                SwapLog.Info("[page] 当前页面所属模块已卸载，切回宿主默认设置页。");
                PclUi.Invoke(pageSetupLeft, "PageChange", defaultId);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[page] 切回宿主默认设置页失败：" + ex.Message);
            }
        }

        private static string BuildItemInfo(PageEntry entry)
        {
            var i18n = SwapI18n.Current;
            if (entry.LogicalId == "swap-addons")
                return i18n.T("管理 Swap 插件", "Manage Swap addons") +
                       " (" + (SwapHost.Addons?.Records.Count ?? 0) + ")";
            if (entry.LogicalId == "swap-settings")
                return i18n.T("宿主信息与 Swap 设置", "Host info & Swap settings");
            return i18n.T("Swap 扩展页面", "Swap extension page");
        }

        /// <summary>
        /// PCL's own 设置 rows turn the hover-scale animation off in XAML; a MyListItem created
        /// through reflection keeps the default <c>true</c>. MyListItem.RefreshColor then casts
        /// RenderTransform to ScaleTransform, while MyPageLeft.TriggerShowAnimation parks a
        /// TranslateTransform on every row it animates - so the first hover (or the animation's own
        /// RefreshColor callback, which fires while the pointer may already rest on the new row)
        /// throws InvalidCastException, which PCL reports as "程序出现未知错误" and then exits.
        /// Measured on 2.99.0: field log 01:40:39 PCL.MyListItem.RefreshColor
        /// "无法将类型为 TranslateTransform 的对象强制转换为类型 ScaleTransform".
        /// </summary>
        private static void DisableHoverScale(object item)
        {
            try
            {
                PclUi.SetMember(item, "IsScaleAnimationEnabled", false);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("Swap 页面：未能关闭侧栏项的悬停缩放动画（" + ex.Message +
                             "）——悬停可能触发宿主 InvalidCastException。");
            }
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
                SwapLog.Info($"[page] 侧栏项点击（tag={tag ?? "(null)"}，线程 {Thread.CurrentThread.ManagedThreadId}）");
                if (tag == null || !int.TryParse(Convert.ToString(tag), out var id) || !IsOurPage(id)) return;

                var modMain = _host.GetType("PCL.ModMain", throwOnError: false);
                var pageSetupLeft = PclUi.GetMember(modMain, "FrmSetupLeft");
                if (pageSetupLeft == null)
                {
                    SwapLog.Warn("[page] 侧栏点击：FrmSetupLeft 为空，无法切换。");
                    return;
                }
                SwitchToPage(pageSetupLeft, id);
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
