using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// PCL's server announcements can be declared as "must act now" notices: the notice carries a
    /// single button and PCL shows it with <c>ForceWait</c>, so the window cannot be dismissed. The
    /// 2.99.x "悲报" notice (id 230, "为遵守相关法律法规，PCL 不再提供 MC 下载功能……") uses exactly
    /// that shape and pushes users into an update that removes the download module.
    ///
    /// This hook keeps the notice but re-renders it through PCL's own dialog with an extra "取消"
    /// button, and remembers the decision so the update the notice triggers can be dropped.
    /// </summary>
    internal static class AnnouncementGuard
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MyMsgBoxDelegate(IntPtr caption, IntPtr title, IntPtr button1, IntPtr button2,
            IntPtr button3, byte isWarn, byte highLight, byte forceWait,
            IntPtr button1Action, IntPtr button2Action, IntPtr button3Action);

        private static MyMsgBoxDelegate _original;
        private static bool _updateCancelled;
        private static readonly object Gate = new object();

        public static bool Installed { get; private set; }

        public static void Install(Assembly host)
        {
            try
            {
                var type = host.GetType("PCL.ModMain", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "MyMsgBox" && m.GetParameters().Length >= 8 &&
                                         m.GetParameters()[0].ParameterType == typeof(string));
                if (method == null)
                {
                    SwapLog.Warn("AnnouncementGuard：未找到 ModMain.MyMsgBox，未安装。");
                    return;
                }

                _hookDelegate = OnMsgBox;
                var detour = DetourInstaller.Install(method, _hookDelegate);
                _original = detour.Original<MyMsgBoxDelegate>();
                Installed = true;
                SwapLog.Info("公告守卫已安装：" + detour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("AnnouncementGuard 安装失败：" + ex);
            }
        }

        private static MyMsgBoxDelegate _hookDelegate;

        private static int OnMsgBox(IntPtr captionPtr, IntPtr titlePtr, IntPtr button1Ptr, IntPtr button2Ptr,
            IntPtr button3Ptr, byte isWarn, byte highLight, byte forceWait,
            IntPtr button1Action, IntPtr button2Action, IntPtr button3Action)
        {
            try
            {
                var caption = ObjectPointer.ToObject<string>(captionPtr);
                var title = ObjectPointer.ToObject<string>(titlePtr);
                var button1 = ObjectPointer.ToObject<string>(button1Ptr);
                var button2 = ObjectPointer.ToObject<string>(button2Ptr);
                var button3 = ObjectPointer.ToObject<string>(button3Ptr);

                if (_original != null &&
                    IsForcedUpdateNotice(caption, title, button1, button2, button3, forceWait != 0))
                {
                    SwapLog.Warn("拦截到无法关闭的“悲报”式更新公告，已为其追加“取消”按钮。");
                    var body = caption + Environment.NewLine + Environment.NewLine +
                               "点击“取消”可以保持当前版本继续使用，不会下载或安装更新。";
                    var bodyPtr = ObjectPointer.ToPointer(body);
                    var cancelPtr = ObjectPointer.ToPointer("取消");
                    var emptyPtr = ObjectPointer.ToPointer(string.Empty);
                    int result = _original(bodyPtr, titlePtr, button1Ptr, cancelPtr, emptyPtr,
                        isWarn, highLight, 0, button1Action, button2Action, button3Action);
                    GC.KeepAlive(body);
                    if (result == 2)
                    {
                        lock (Gate) _updateCancelled = true;
                        SwapLog.Info("用户在更新公告中选择“取消”，本次公告更新将被丢弃。");
                    }
                    // PCL clamps the result into the notice's own button range (1), so the update action
                    // still runs; ConsumeCancelledUpdate() makes the update itself a no-op.
                    return result;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("公告守卫钩子异常，按原始行为继续：" + ex);
            }

            if (_original != null)
                return _original(captionPtr, titlePtr, button1Ptr, button2Ptr, button3Ptr, isWarn, highLight, forceWait,
                    button1Action, button2Action, button3Action);
            return 1;
        }

        /// <summary>True once, when the user cancelled an announcement-driven launcher update.</summary>
        public static bool ConsumeCancelledUpdate()
        {
            lock (Gate)
            {
                if (!_updateCancelled) return false;
                _updateCancelled = false;
                return true;
            }
        }

        private static bool IsForcedUpdateNotice(string caption, string title, string button1,
            string button2, string button3, bool forceWait)
        {
            if (!forceWait) return false;
            if (!string.IsNullOrEmpty(button2) || !string.IsNullOrEmpty(button3)) return false;
            if (string.IsNullOrEmpty(caption)) return false;

            if (!string.IsNullOrEmpty(title) && title.IndexOf("悲报", StringComparison.Ordinal) >= 0)
                return true;
            if (caption.IndexOf("不再提供 MC 下载功能", StringComparison.Ordinal) >= 0) return true;
            if (caption.IndexOf("不再提供MC下载功能", StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
