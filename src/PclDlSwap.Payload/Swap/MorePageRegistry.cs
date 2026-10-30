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
    /// The "更多" (More) sidebar, PCL.PageOtherLeft. It mirrors PageSetupLeft almost exactly -
    /// PageCheck / PageGet / PageChangeRun / PanItem / PageID - so the set-up trick carries over:
    /// detour PageCheck for our ids, replay the official switch, and answer PageGet with our page.
    /// Ids start at 200 so the two sidebars never collide in the shared FormMain.PageSubType enum.
    /// </summary>
    internal static class MorePageRegistry
    {
        private const int FirstPageId = 200;

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Entry> Pages = new Dictionary<int, Entry>();
        private static readonly List<object> InsertedItems = new List<object>();
        private static readonly HashSet<string> InsertedLogicals =
            new HashSet<string>(StringComparer.Ordinal);
        private static int _nextPageId = FirstPageId;
        private static Assembly _host;
        private static DetourInstaller _pageCheckDetour;
        private static DetourInstaller _pageGetDetour;
        private static volatile bool _installed;
        private static volatile bool _sidebarDone;

        private sealed class Entry
        {
            public int Id;
            public string LogicalId;
            public string Title;
            public Func<FrameworkElement> Factory;
            public object Instance;
        }

        private static bool PageIdWritable => _pageGetDetour != null;

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
                SwapLog.Warn("[more] 宿主程序集为空，更多页槽位未安装。");
                return;
            }

            InstallPageCheckDetour();
            InstallPageGetDetour();
            StartSidebarWatcher();
        }

        public static void Register(string logicalId, string title, Func<FrameworkElement> factory)
        {
            if (factory == null || string.IsNullOrWhiteSpace(logicalId)) return;
            lock (Gate)
            {
                if (Pages.Values.Any(entry => entry.LogicalId == logicalId)) return;
                var id = _nextPageId++;
                Pages[id] = new Entry
                {
                    Id = id,
                    LogicalId = logicalId,
                    Title = string.IsNullOrEmpty(title) ? logicalId : title,
                    Factory = factory
                };
                _sidebarDone = false;
                SwapLog.Info($"[more] 已登记“{title}”（PageID {id}）。");
            }

            RefreshSidebar();
        }

        public static bool Unregister(string logicalId)
        {
            Entry removed;
            lock (Gate)
            {
                removed = Pages.Values.FirstOrDefault(entry => entry.LogicalId == logicalId);
                if (removed != null) Pages.Remove(removed.Id);
            }

            if (removed == null) return false;
            SwapLog.Info($"[more] 已注销“{removed.Title}”（PageID {removed.Id}）。");
            RefreshSidebar();
            return true;
        }

        public static void Invalidate(string logicalId)
        {
            lock (Gate)
            {
                var entry = Pages.Values.FirstOrDefault(candidate => candidate.LogicalId == logicalId);
                if (entry != null) entry.Instance = null;
            }
        }

        public static void RefreshSidebar()
        {
            try
            {
                var modMain = _host?.GetType("PCL.ModMain", throwOnError: false);
                var otherLeft = PclUi.GetMember(modMain, "FrmOtherLeft");
                if (otherLeft == null) return;   // not created yet; the watcher fills it

                PclUi.OnUiThread(() =>
                {
                    RemoveInsertedItems(otherLeft);
                    InsertSidebarItemsCore(otherLeft);
                });
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[more] 重建侧栏失败：" + ex.Message);
            }
        }

        private static void InstallPageCheckDetour()
        {
            try
            {
                var type = _host.GetType("PCL.PageOtherLeft", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "PageCheck" &&
                                                 candidate.GetParameters().Length == 2);
                if (method == null)
                {
                    SwapLog.Warn("[more] 未找到 PageOtherLeft.PageCheck，更多页槽位不可用。");
                    return;
                }

                var hook = typeof(MorePageRegistry).GetMethod(nameof(HookPageCheck),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _pageCheckDetour = DetourInstaller.Install(method, hook);
                SwapLog.Info("[more] PageCheck 钩子已安装（" + _pageCheckDetour.Verify() + "）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("[more] PageCheck 钩子安装失败：" + ex);
            }
        }

        private static void InstallPageGetDetour()
        {
            try
            {
                var type = _host.GetType("PCL.PageOtherLeft", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "PageGet" &&
                                                 candidate.GetParameters().Length == 1);
                if (method == null)
                {
                    SwapLog.Warn("[more] 未找到 PageOtherLeft.PageGet。");
                    return;
                }

                var hook = typeof(MorePageRegistry).GetMethod(nameof(HookPageGet),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _pageGetDetour = DetourInstaller.Install(method, hook);
                SwapLog.Info("[more] PageGet 钩子已安装（" + _pageGetDetour.Verify() + "）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("[more] PageGet 钩子安装失败：" + ex);
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
                SwapLog.Error("[more] PageCheck 钩子异常：" + ex);
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
                    var page = GetOrCreatePage(pageId);
                    if (page != null) return page;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("[more] PageGet 钩子异常：" + ex);
            }

            return _pageGetDetour.InvokeOriginal(self, id);
        }

        private static bool IsOurPage(int id)
        {
            lock (Gate) return Pages.ContainsKey(id);
        }

        private static void SwitchToPage(object otherLeft, int id)
        {
            try
            {
                var page = GetOrCreatePage(id);
                if (page == null) return;

                var aniType = _host.GetType("PCL.ModAnimation", throwOnError: false);
                object aniBefore = null;
                try
                {
                    aniBefore = PclUi.GetMember(aniType, "AniControlEnabled");
                    if (aniBefore is int value) PclUi.SetMember(aniType, "AniControlEnabled", value + 1);
                }
                catch
                {
                    aniBefore = null;
                }

                try
                {
                    PclUi.SetMember(otherLeft, "IsPageSwitched", true);
                    var run = otherLeft.GetType().GetMethod("PageChangeRun",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    run?.Invoke(null, new[] { page });

                    if (PageIdWritable)
                    {
                        var idType = PclUi.GetMember(otherLeft, "PageID")?.GetType()
                                     ?? _host.GetType("PCL.FormMain+PageSubType");
                        PclUi.SetMember(otherLeft, "PageID",
                            idType == null ? (object)id : Enum.ToObject(idType, id));
                    }
                }
                finally
                {
                    if (aniBefore is int original) PclUi.SetMember(aniType, "AniControlEnabled", original);
                }

                SwapLog.Info($"[more] 已切换到“{id}”（PageID {id}）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error($"[more] 页面切换失败（id={id}）：" + ex);
            }
        }

        private static object GetOrCreatePage(int id)
        {
            Entry entry;
            lock (Gate)
            {
                if (!Pages.TryGetValue(id, out entry)) return null;
                if (entry.Instance != null) return entry.Instance;
            }

            var pageType = _host.GetType("PCL.MyPageRight", throwOnError: false);
            if (pageType == null) return null;

            var page = Activator.CreateInstance(pageType);
            FrameworkElement view = null;
            try
            {
                view = entry.Factory();
                if (view != null) PclUi.SetMember(page, "Child", view);
            }
            catch (Exception ex)
            {
                SwapLog.Error($"[more] 构建页面失败（{entry.LogicalId}）：" + ex);
            }

            lock (Gate) entry.Instance = page;
            return page;
        }

        private static void StartSidebarWatcher()
        {
            var watcher = new Thread(() =>
            {
                var deadline = DateTime.UtcNow.AddMinutes(30);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var modMain = _host.GetType("PCL.ModMain", throwOnError: false);
                        var otherLeft = PclUi.GetMember(modMain, "FrmOtherLeft");
                        if (otherLeft != null)
                        {
                            PclUi.OnUiThread(() => EnsureSidebarItems(otherLeft));
                        }
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("[more] 侧栏轮询异常：" + ex.Message);
                    }

                    Thread.Sleep(500);
                }
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.MoreSidebar"
            };
            watcher.Start();
        }

        /// <summary>Must run on the host UI thread.</summary>
        private static void EnsureSidebarItems(object otherLeft)
        {
            try
            {
                var panItem = PclUi.GetMember(otherLeft, "PanItem");
                var children = panItem == null ? null : PclUi.GetMember(panItem, "Children");
                var stillAttached = children != null && InsertedItems.Count > 0 &&
                                    InsertedItems.All(item => ChildrenContains(children, item));
                if (!stillAttached)
                {
                    InsertedItems.Clear();
                    InsertedLogicals.Clear();
                }

                if (Snapshot().Count != InsertedLogicals.Count)
                    InsertSidebarItemsCore(otherLeft);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[more] 侧栏补齐失败：" + ex.Message);
            }
        }

        private static bool ChildrenContains(object children, object item)
        {
            if (!(children is System.Collections.IEnumerable enumerable)) return false;
            foreach (var child in enumerable)
            {
                if (ReferenceEquals(child, item)) return true;
            }

            return false;
        }

        /// <summary>Must run on the host UI thread.</summary>
        private static void InsertSidebarItemsCore(object otherLeft)
        {
            try
            {
                if (_pageCheckDetour == null)
                {
                    SwapLog.Warn("[more] PageCheck 钩子不可用，跳过更多页侧栏插入。");
                    return;
                }

                var panItem = PclUi.GetMember(otherLeft, "PanItem");
                var children = panItem == null ? null : PclUi.GetMember(panItem, "Children");
                var add = children?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
                var itemType = _host.GetType("PCL.MyListItem", throwOnError: false);
                if (children == null || add == null || itemType == null) return;

                var checkType = _host.GetType("PCL.MyListItem+CheckType", throwOnError: false);
                var added = 0;
                foreach (var entry in Snapshot())
                {
                    if (InsertedLogicals.Contains(entry.LogicalId)) continue;

                    var item = Activator.CreateInstance(itemType);
                    PclUi.SetProperty(item, "Title", entry.Title);
                    PclUi.SetProperty(item, "Info", "PCL2 Swap");
                    try { PclUi.SetMember(item, "IsScaleAnimationEnabled", false); } catch { }
                    if (checkType != null)
                        PclUi.SetProperty(item, "Type", Enum.Parse(checkType, "RadioBox"));
                    PclUi.SetMember(item, "Tag", entry.Id.ToString());

                    SubscribeCheck(item, itemType);

                    add.Invoke(children, new[] { item });
                    InsertedItems.Add(item);
                    InsertedLogicals.Add(entry.LogicalId);
                    added++;
                }

                _sidebarDone = true;
                if (added > 0)
                    SwapLog.Info($"[more] 已在“更多”侧栏插入 {added} 个条目。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("[more] 插入侧栏项失败：" + ex);
            }
        }

        private static void RemoveInsertedItems(object otherLeft)
        {
            if (InsertedItems.Count == 0) return;
            try
            {
                var panItem = PclUi.GetMember(otherLeft, "PanItem");
                var children = panItem == null ? null : PclUi.GetMember(panItem, "Children");
                var remove = children?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Remove" && m.GetParameters().Length == 1);
                if (children == null || remove == null) return;

                foreach (var item in InsertedItems)
                {
                    try { remove.Invoke(children, new[] { item }); } catch { }
                }
            }
            catch
            {
                // ignore - a stale row is preferable to a crash
            }
            finally
            {
                InsertedItems.Clear();
                InsertedLogicals.Clear();
                _sidebarDone = false;
            }
        }

        private static void SubscribeCheck(object item, Type itemType)
        {
            try
            {
                var checkEvent = itemType.GetEvent("Check", BindingFlags.Public | BindingFlags.Instance);
                if (checkEvent == null) return;
                var handler = Delegate.CreateDelegate(checkEvent.EventHandlerType,
                    typeof(MorePageRegistry).GetMethod(nameof(OnSidebarCheck),
                        BindingFlags.NonPublic | BindingFlags.Static));
                checkEvent.AddEventHandler(item, handler);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[more] 侧栏项事件订阅失败：" + ex.Message);
            }
        }

        internal static void OnSidebarCheck(object sender, object e)
        {
            try
            {
                var tag = PclUi.GetMember(sender, "Tag");
                if (tag == null || !int.TryParse(Convert.ToString(tag), out var id) || !IsOurPage(id))
                    return;

                var modMain = _host.GetType("PCL.ModMain", throwOnError: false);
                var otherLeft = PclUi.GetMember(modMain, "FrmOtherLeft");
                if (otherLeft == null) return;
                SwitchToPage(otherLeft, id);
            }
            catch (Exception ex)
            {
                SwapLog.Error("[more] 侧栏点击处理失败：" + ex);
            }
        }

        private static List<Entry> Snapshot()
        {
            lock (Gate) return Pages.Values.OrderBy(entry => entry.Id).ToList();
        }
    }
}
