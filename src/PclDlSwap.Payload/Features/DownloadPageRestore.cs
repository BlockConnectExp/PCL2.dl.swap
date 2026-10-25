using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LoaderInitDelegate(IntPtr self);

        private static LoaderInitDelegate _loaderInitHook;
        private static volatile bool _sidebarFixed;
        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Static | BindingFlags.Instance;

        public static bool LoaderInstalled { get; private set; }
        public static bool SidebarInstalled { get; private set; }

        public static void Install(Assembly host)
        {
            InstallLoader(host);
            InstallSidebar(host);
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

                _loaderInitHook = OnLoaderInit;
                var detour = DetourInstaller.Install(method, _loaderInitHook);
                _loaderInitOriginal = detour.Original<LoaderInitDelegate>();
                LoaderInstalled = true;
                SwapLog.Info("下载页装载器恢复已安装：" + detour.Verify());
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
                        SwapLog.Warn("下载侧栏恢复：页面没有 ItemInstall，跳过。");
                        _sidebarFixed = true;
                        return;
                    }

                    var visibility = PclUi.GetMember(itemInstall, "Visibility");
                    if (visibility == null)
                    {
                        SwapLog.Warn("下载侧栏恢复：ItemInstall 没有 Visibility，跳过。");
                        _sidebarFixed = true;
                        return;
                    }

                    if (string.Equals(Convert.ToString(visibility), "Visible", StringComparison.Ordinal))
                    {
                        // 2.99.0 and older already show the entry - nothing to repair.
                        _sidebarFixed = true;
                        SwapLog.Info("下载侧栏“原版游戏”项本就可见，无需修复。");
                        return;
                    }

                    var visibleValue = visibility.GetType().IsEnum
                        ? Enum.Parse(visibility.GetType(), "Visible")
                        : (object)"Visible";
                    PclUi.SetMember(itemInstall, "Visibility", visibleValue);

                    var host = Assembly.GetEntryAssembly();
                    var pageSubType = host?.GetType("PCL.FormMain+PageSubType");
                    if (pageSubType != null)
                        PclUi.SetMember(page, "PageID", Enum.Parse(pageSubType, "Default"));
                    if (itemMod != null)
                        PclUi.SetMember(itemMod, "Checked", false);

                    _sidebarFixed = true;
                    SwapLog.Info("下载侧栏“原版游戏”项已恢复可见，并把默认子页切回原版游戏。");
                }
                catch (Exception ex)
                {
                    SwapLog.Error("下载侧栏恢复失败：" + ex);
                    _sidebarFixed = true;
                }
            });
        }

        /// <summary>
        /// Replaces the gated loader with PCL's own download list loader: the page keeps its regular
        /// loader UI and finish callback, only the work action is swapped back to the real one.
        /// </summary>
        private static void OnLoaderInit(IntPtr selfPtr)
        {
            try
            {
                var self = ObjectPointer.ToObject(selfPtr);
                if (self == null)
                {
                    InvokeOriginal(selfPtr);
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
                    InvokeOriginal(selfPtr);
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
                    InvokeOriginal(selfPtr);
                    return;
                }

                var baseType = pageType.BaseType;
                var definition = baseType?.GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "PageLoaderInit" && m.IsGenericMethodDefinition &&
                                         m.GetGenericArguments().Length == 2);
                if (definition == null)
                {
                    SwapLog.Error("下载页恢复：未找到 PageLoaderInit<,>，回退到原始 LoaderInit。");
                    InvokeOriginal(selfPtr);
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
                SwapLog.Info("下载页已使用真实装载器初始化（DlClientListLoader）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载页装载器恢复异常，回退到原始 LoaderInit：" + ex);
                InvokeOriginal(selfPtr);
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

        private static void InvokeOriginal(IntPtr selfPtr)
        {
            try
            {
                if (_loaderInitOriginal != null) _loaderInitOriginal(selfPtr);
            }
            catch (Exception ex)
            {
                SwapLog.Error("调用原始 LoaderInit 失败：" + ex.Message);
            }
        }

        private static LoaderInitDelegate _loaderInitOriginal;
    }
}
