using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CtorDelegate(IntPtr self);

        private static LoaderInitDelegate _loaderInitHook;
        private static CtorDelegate _ctorHook;
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

        private static void InstallSidebar(Assembly host)
        {
            try
            {
                var type = host.GetType("PCL.PageDownloadLeft", throwOnError: false);
                var ctor = type?.GetConstructors(Flags).FirstOrDefault(c => c.GetParameters().Length == 0);
                if (ctor == null)
                {
                    SwapLog.Warn("下载侧栏恢复：未找到 PageDownloadLeft..ctor。");
                    return;
                }

                _ctorHook = OnSidebarCtor;
                var detour = DetourInstaller.Install(ctor, _ctorHook);
                _sidebarOriginal = detour.Original<CtorDelegate>();
                SidebarInstalled = true;
                SwapLog.Info("下载侧栏恢复已安装：" + detour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载侧栏恢复安装失败：" + ex);
            }
        }

        private static CtorDelegate _sidebarOriginal;

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

                var loadMinecraft = PclUi.GetField(self, "LoadMinecraft");
                var panLoad = PclUi.GetField(self, "PanLoad");
                var panAllBack = PclUi.GetField(self, "PanAllBack");

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

        private static void OnSidebarCtor(IntPtr selfPtr)
        {
            if (_sidebarOriginal != null)
                _sidebarOriginal(selfPtr);

            try
            {
                var self = ObjectPointer.ToObject(selfPtr);
                if (self == null) return;

                var host = Assembly.GetEntryAssembly();
                var pageSubType = host?.GetType("PCL.FormMain+PageSubType");
                var itemInstall = PclUi.GetField(self, "ItemInstall");
                var itemMod = PclUi.GetField(self, "ItemMod");

                var visibilityProperty = itemInstall.GetType().GetProperty("Visibility", Flags);
                PclUi.SetProperty(itemInstall, "Visibility",
                    Enum.Parse(visibilityProperty.PropertyType, "Visible"));
                if (pageSubType != null)
                    PclUi.SetField(self, "PageID", Enum.Parse(pageSubType, "Default"));
                PclUi.SetProperty(itemMod, "Checked", false);

                SwapLog.Info("下载侧栏“原版游戏”项已恢复可见，并把默认子页切回原版游戏。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("下载侧栏恢复失败：" + ex);
            }
        }
    }
}
