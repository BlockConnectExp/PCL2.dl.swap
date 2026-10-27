using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;
using PclDlSwap.Payload.Swap;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// PCL's server announcements can be declared as "must act now" notices: the notice carries a single
    /// button and PCL shows it with <c>ForceWait</c>, so the window cannot be dismissed. The 2.99.x "悲报"
    /// notice uses exactly that shape and pushes users into an update that removes the download module.
    ///
    /// This hook keeps the notice but re-renders it through PCL's own dialog with an extra "取消" button, and
    /// remembers the decision so the update the notice triggers can be dropped. The hook is a managed method
    /// with the target's ABI; the original dialog is reached through its IL copy.
    /// </summary>
    internal static class AnnouncementGuard
    {
        private static DetourInstaller _detour;
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

                var hook = typeof(AnnouncementGuard).GetMethod(nameof(HookMyMsgBox),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _detour = DetourInstaller.Install(method, hook);
                Installed = true;
                SwapLog.Info("公告守卫已安装：" + _detour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("AnnouncementGuard 安装失败：" + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int HookMyMsgBox(string caption, string title, string button1, string button2,
            string button3, bool isWarn, bool highLight, bool forceWait,
            object button1Action, object button2Action, object button3Action)
        {
            // Cancel Button module off -> behave exactly like the untouched launcher.
            if (!SwapHost.AddonEnabled("swap.cancel-button"))
                return _detour != null
                    ? _detour.InvokeOriginal<int>(null, caption, title, button1, button2, button3,
                        isWarn, highLight, forceWait, button1Action, button2Action, button3Action)
                    : 1;

            try
            {
                if (_detour != null &&
                    IsForcedUpdateNotice(caption, title, button1, button2, button3, forceWait))
                {
                    SwapLog.Warn("拦截到无法关闭的“悲报”式更新公告，已为其追加“取消”按钮。");
                    var body = caption + Environment.NewLine + Environment.NewLine +
                               "点击“取消”可以保持当前版本继续使用，不会下载或安装更新。";
                    int result = _detour.InvokeOriginal<int>(null, body, title, button1, "取消", string.Empty,
                        isWarn, highLight, false, button1Action, button2Action, button3Action);
                    if (result == 2)
                    {
                        lock (Gate) _updateCancelled = true;
                        SwapLog.Info("用户在更新公告中选择“取消”，本次公告更新将被丢弃。");
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("公告守卫钩子异常，按原始行为继续：" + ex);
            }

            if (_detour != null)
                return _detour.InvokeOriginal<int>(null, caption, title, button1, button2, button3,
                    isWarn, highLight, forceWait, button1Action, button2Action, button3Action);
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


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
