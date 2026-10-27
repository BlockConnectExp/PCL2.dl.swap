using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// Restores the "原版游戏" (vanilla) download entry on builds that gate it in-process:
    ///
    ///  * 2.99.0+ replaces the loader handed to <c>PageLoaderInit</c> with a task whose work action
    ///    throws "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能"; the real loader
    ///    (<c>ModDownload.DlClientListLoader</c> / <c>DlClientListMain</c>) is still compiled in.
    ///  * 2.99.0.1 additionally collapses the sidebar item and switches the default sub-page to the
    ///    mod list inside <c>PageDownloadLeft..ctor</c>.
    ///
    /// Both gates are undone here, so the injected build keeps the same behaviour as the disk patch.
    /// </summary>
    internal static class DownloadPageRestore
    {
        private static DetourInstaller _loaderDetour;
        private static volatile bool _sidebarFixed;
        private static volatile bool _realLoaderSeen;
        private static int _sidebarAttempts;

        /// <summary>
        /// A repair that lands on an unfinished visual tree is retried instead of silencing the
        /// sidebar for the rest of the session (the Alpha 4 2.99.0.1 run lost the entry after one
        /// cross-thread failure).
        /// </summary>
        private const int MaxSidebarAttempts = 40;

        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Static | BindingFlags.Instance;

        public static bool LoaderInstalled { get; private set; }
        public static bool SidebarInstalled { get; private set; }

        public static void Install(Assembly host)
        {
            InstallLoader(host);
            InstallSidebar(host);
            StartRedriveWatcher(host);
        }

        /// <summary>
        /// The loader detour only fires when LoaderInit runs. If the download page already exists by
        /// the time the payload is injected ("open the page first, inject later"), its gate-era
        /// loader has already failed and nothing would ever re-run it. This watcher re-drives
        /// LoaderInit on such a page - but only while our real-loader path has never completed, so a
        /// healthy page is left alone.
        /// </summary>
        private static void StartRedriveWatcher(Assembly host)
        {
            var watcher = new Thread(() =>
            {
                var deadline = DateTime.UtcNow.AddMinutes(30);
                var lastAttempt = DateTime.MinValue;
                while (!_realLoaderSeen && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var modMain = host.GetType("PCL.ModMain", throwOnError: false);
                        var page = PclUi.GetMember(modMain, "FrmDownloadInstall");
                        if (page != null && (DateTime.UtcNow - lastAttempt).TotalSeconds >= 5)
                        {
                            lastAttempt = DateTime.UtcNow;
                            SwapLog.Info("下载页重驱：检测到既有页面且真实装载器未运行，重跑 LoaderInit。");
                            PclUi.OnUiThread(() =>
                            {
                                try { PclUi.Invoke(page, "LoaderInit"); }
                                catch (Exception ex) { SwapLog.Error("下载页重驱失败：" + ex); }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Warn("下载页重驱探测异常：" + ex.Message);
                    }

                    Thread.Sleep(300);
                }

                if (_realLoaderSeen) SwapLog.Info("下载页重驱：真实装载器已在运行，看门结束。");
                else SwapLog.Warn("下载页重驱：等待超时（30 分钟），未捕获到既有页面。");
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.DownloadRedrive"
            };
            watcher.Start();
        }

        private static void InstallLoader(Assembly host)
        {
            try
            {
                var type = host.GetType("PCL.PageDownloadInstall", throwOnError: false);
                var method = type?.GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "LoaderInit" && m.GetParameters().Length == 0);
                if (method == null)
                {
                    SwapLog.Warn("下载页恢复：未找到 PageDownloadInstall.LoaderInit。");
                    return;
                }

                if (!LooksLikeGatedLoader(method))
                {
                    SwapLog.Info("下载页恢复：未检测到下载 gate（该构建自带可用下载流程），跳过恢复。");
                    return;
                }

                var hook = typeof(DownloadPageRestore).GetMethod(nameof(HookLoaderInit),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _loaderDetour = DetourInstaller.Install(method, hook);
                LoaderInstalled = true;
                SwapLog.Info("下载页装载器恢复已安装：" + _loaderDetour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载页装载器恢复安装失败：" + ex);
            }
        }

        /// <summary>
        /// 2.99.x replaces the download loader with a stub whose body carries the "关闭下载" task name
        /// and the "不再提供 MC 下载功能" notice. Detecting those strings in the IL keeps stock builds
        /// (2.12.8.1 and friends) untouched - their LoaderInit carries no such markers.
        /// </summary>
        private static bool LooksLikeGatedLoader(MethodInfo method)
        {
            try
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return true;
                var module = method.Module;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x72) continue;   // ldstr
                    var token = BitConverter.ToInt32(il, i + 1);
                    string text;
                    try { text = module.ResolveString(token); }
                    catch { continue; }
                    if (string.IsNullOrEmpty(text)) continue;
                    if (text.IndexOf("关闭下载", StringComparison.Ordinal) >= 0) return true;
                    if (text.IndexOf("不再提供", StringComparison.Ordinal) >= 0) return true;
                    if (text.IndexOf("暂时不再", StringComparison.Ordinal) >= 0) return true;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("下载页恢复：gate 探测失败，按需恢复处理：" + ex.Message);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 2.99.0.1 collapses the vanilla entry inside PageDownloadLeft..ctor. That constructor's JIT
        /// prologue is not safe to relocate, so this watcher waits for ModMain.FrmDownloadLeft to
        /// appear and repairs the already-built page instead (and does nothing on builds where the
        /// entry is still visible).
        /// </summary>
        private static void InstallSidebar(Assembly host)
        {
            var watcher = new Thread(() => WatchSidebar(host))
            {
                IsBackground = true,
                Name = "PCL2Swap.SidebarRestore"
            };
            watcher.Start();
            SidebarInstalled = true;
            SwapLog.Info("下载侧栏恢复已启用（等待下载页首次创建）。");
        }

        private static void WatchSidebar(Assembly host)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
            while (!_sidebarFixed && DateTime.UtcNow < deadline)
            {
                try
                {
                    var modMain = host.GetType("PCL.ModMain", throwOnError: false);
                    var page = PclUi.GetMember(modMain, "FrmDownloadLeft");
                    if (page != null) TryRestoreSidebar(page);
                }
                catch (Exception ex)
                {
                    SwapLog.Error("下载侧栏恢复轮询异常：" + ex.Message);
                    _sidebarFixed = true;
                }

                if (!_sidebarFixed) Thread.Sleep(300);
            }
        }

        private static void TryRestoreSidebar(object page)
        {
            PclUi.OnUiThread(() =>
            {
                try
                {
                    var itemInstall = PclUi.GetMember(page, "ItemInstall");
                    var itemMod = PclUi.GetMember(page, "ItemMod");
                    if (itemInstall == null)
                    {
                        NoteSidebarFailure("下载侧栏恢复：页面没有 ItemInstall", new MissingMemberException("ItemInstall"));
                        return;
                    }

                    var visibility = PclUi.GetMember(itemInstall, "Visibility");
                    if (visibility == null)
                    {
                        NoteSidebarFailure("下载侧栏恢复：ItemInstall 没有 Visibility", new MissingMemberException("Visibility"));
                        return;
                    }

                    var host = Assembly.GetEntryAssembly();
                    var pageSubType = host?.GetType("PCL.FormMain+PageSubType", throwOnError: false);
                    var defaultId = pageSubType == null ? null : Enum.Parse(pageSubType, "Default");
                    var modMain = host?.GetType("PCL.ModMain", throwOnError: false);

                    var currentId = PclUi.GetMember(page, "PageID");
                    var installPage = modMain == null ? null : PclUi.GetMember(modMain, "FrmDownloadInstall");
                    bool alreadyVisible = string.Equals(Convert.ToString(visibility), "Visible", StringComparison.Ordinal);
                    bool alreadyDefault = defaultId == null || Equals(currentId, defaultId);

                    if (alreadyVisible && alreadyDefault && installPage != null)
                    {
                        // 2.99.0 and older already show the entry on the Default sub-page.
                        _sidebarFixed = true;
                        SwapLog.Info("下载侧栏“原版游戏”项本就可见且页面已就绪，无需修复。");
                        return;
                    }

                    // 1) Restore the entry itself.
                    var visibleValue = visibility.GetType().IsEnum
                        ? Enum.Parse(visibility.GetType(), "Visible")
                        : (object)"Visible";
                    PclUi.SetMember(itemInstall, "Visibility", visibleValue);

                    // 2) Let PCL's own navigation run. Setting Checked drives Check -> PageCheck ->
                    //    PageChange(Default) inside the real launcher, which is what creates
                    //    ModMain.FrmDownloadInstall through PageGet. Writing the PageID field directly
                    //    used to skip PageGet entirely, and the next click then died in
                    //    ItemInstall_Click (FrmDownloadInstall.ExitSelectPage on a null reference) -
                    //    the crash in the field logs at 22:23:35 and 22:25:28.
                    var checkedState = PclUi.GetMember(itemInstall, "Checked");
                    if (!(checkedState is bool isChecked && isChecked))
                        PclUi.SetMember(itemInstall, "Checked", true);

                    // 3) Fallback for shapes where the Checked change cannot raise the event: run the
                    //    launcher's own sub-page switch. PageChange returns early when PageID already
                    //    equals its target, so this also covers a half-restored state.
                    currentId = PclUi.GetMember(page, "PageID");
                    if (defaultId != null && !Equals(currentId, defaultId))
                    {
                        PclUi.Invoke(page, "PageChange", defaultId);
                        currentId = PclUi.GetMember(page, "PageID");
                    }

                    // 4) Make sure the sub-page object exists: create it through PCL's own PageGet
                    //    (identical to what the launcher does) when navigation did not.
                    installPage = modMain == null ? null : PclUi.GetMember(modMain, "FrmDownloadInstall");
                    if (installPage == null && defaultId != null)
                    {
                        try { installPage = PclUi.Invoke(page, "PageGet", defaultId); }
                        catch (Exception ex) { SwapLog.Warn("下载侧栏恢复：PageGet(Default) 失败：" + ex.Message); }
                    }

                    if (itemMod != null)
                    {
                        // Checked=false never raises the Check event, so this cannot bounce the
                        // sub-page back to the mod list.
                        PclUi.SetMember(itemMod, "Checked", false);
                    }

                    if (installPage == null)
                    {
                        NoteSidebarFailure("下载侧栏恢复：FrmDownloadInstall 仍未创建",
                            new InvalidOperationException("FrmDownloadInstall=null"));
                        return;
                    }

                    _sidebarFixed = true;
                    SwapLog.Info("下载侧栏“原版游戏”项已恢复：项可见、子页为 Default、FrmDownloadInstall 已就绪。");
                }
                catch (Exception ex)
                {
                    NoteSidebarFailure("下载侧栏恢复失败", ex);
                }
            });
        }

        private static void NoteSidebarFailure(string label, Exception ex)
        {
            var attempts = Interlocked.Increment(ref _sidebarAttempts);
            if (attempts >= MaxSidebarAttempts)
            {
                _sidebarFixed = true;
                SwapLog.Error($"{label}（第 {attempts} 次仍失败，停止重试）：{ex}");
            }
            else if (attempts == 1)
            {
                SwapLog.Warn($"{label}（第 {attempts} 次，将继续重试）：{ex.Message}");
            }
            else if (attempts % 10 == 0)
            {
                SwapLog.Info($"{label}（第 {attempts} 次失败，继续重试）。");
            }
        }

        /// <summary>
        /// Replaces the gated loader with PCL's own download list loader: the page keeps its regular
        /// loader UI and finish callback, only the work action is swapped back to the real one.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookLoaderInit(object self)
        {
            try
            {
                if (self == null)
                {
                    InvokeOriginal(self);
                    return;
                }

                var pageType = self.GetType();
                var host = Assembly.GetEntryAssembly();
                var download = host?.GetType("PCL.ModDownload", throwOnError: false);
                var main = download?.GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "DlClientListMain" && m.GetParameters().Length == 1);
                var realLoader = download?.GetFields(Flags)
                    .FirstOrDefault(f => f.Name == "DlClientListLoader")?.GetValue(null);

                if (main == null || realLoader == null)
                {
                    SwapLog.Error("下载页恢复：宿主缺少 DlClientListMain/DlClientListLoader，回退到原始 LoaderInit。" +
                                  " 该构建可能已经删除了下载模块。");
                    InvokeOriginal(self);
                    return;
                }

                _page = self;
                PclUi.Invoke(self, "ControlInit");

                // These are VB.NET properties on PCL's page class; a field-only lookup yields null
                // and PageLoaderInit then dereferences the nulls inside its own body.
                var loadMinecraft = PclUi.GetMember(self, "LoadMinecraft");
                var panLoad = PclUi.GetMember(self, "PanLoad");
                var panAllBack = PclUi.GetMember(self, "PanAllBack");
                if (loadMinecraft == null || panLoad == null || panAllBack == null)
                {
                    SwapLog.Error("下载页恢复：页面控件未就绪（LoadMinecraft=" + (loadMinecraft != null) +
                                  ", PanLoad=" + (panLoad != null) + ", PanAllBack=" + (panAllBack != null) +
                                  "），回退到原始 LoaderInit。");
                    InvokeOriginal(self);
                    return;
                }

                var baseType = pageType.BaseType;
                var definition = baseType?.GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "PageLoaderInit" && m.IsGenericMethodDefinition &&
                                         m.GetGenericArguments().Length == 2);
                if (definition == null)
                {
                    SwapLog.Error("下载页恢复：未找到 PageLoaderInit<,>，回退到原始 LoaderInit。");
                    InvokeOriginal(self);
                    return;
                }

                var loaderArguments = realLoader.GetType().GetGenericArguments();
                var closed = definition.MakeGenericMethod(loaderArguments);

                // The page's own finish callback (LoadMinecraft_OnFinish) rebuilds the version list;
                // rebuild an equivalent delegate for the closed generic signature.
                object finish = null;
                try
                {
                    var finishParameter = closed.GetParameters()[5].ParameterType;
                    var bridge = typeof(DownloadPageRestore)
                        .GetMethod(nameof(FinishBridge), BindingFlags.NonPublic | BindingFlags.Static)
                        .MakeGenericMethod(realLoader.GetType());
                    finish = Delegate.CreateDelegate(finishParameter, bridge);
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("下载页恢复：未能重建完成回调，将只使用通用流程：" + ex.Message);
                }

                closed.Invoke(self, new[] { loadMinecraft, panLoad, panAllBack, null, realLoader, finish, null, true });
                _realLoaderSeen = true;
                SwapLog.Info("下载页已使用真实装载器初始化（DlClientListLoader）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载页装载器恢复异常，回退到原始 LoaderInit：" + ex);
                InvokeOriginal(self);
            }
        }

        private static object _page;

        private static void FinishBridge<T>(T task)
        {
            try
            {
                if (_page == null) return;
                PclUi.Invoke(_page, "LoadMinecraft_OnFinish");
            }
            catch (Exception ex)
            {
                SwapLog.Warn("下载页完成回调失败：" + ex.Message);
            }
        }

        private static void InvokeOriginal(object self)
        {
            try
            {
                if (_loaderDetour != null) _loaderDetour.InvokeOriginal(self);
            }
            catch (Exception ex)
            {
                SwapLog.Error("调用原始 LoaderInit 失败：" + ex.Message);
            }
        }
    }
}
