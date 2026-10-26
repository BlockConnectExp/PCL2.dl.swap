using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// "更多 → 反馈" used to drop the user straight into PCL's own feedback flow, which is wrong for
    /// a build that ships extra behaviour: a Swap bug must go to Swap, an upstream PCL bug to PCL.
    /// The hook asks which side the report belongs to first, then runs the matching flow.
    ///
    /// The hook is a static managed method with the target's exact ABI (see DetourInstaller) and reaches the
    /// original through its IL copy.
    /// </summary>
    internal static class FeedbackRouter
    {
        private const string SwapIssuesUrl = "https://github.com/BlockConnectExp/PCL2.dl.swap/issues";

        private static DetourInstaller _detour;

        public static bool Installed { get; private set; }

        public static void Install(Assembly host)
        {
            try
            {
                var type = host.GetType("PCL.PageOtherLeft", throwOnError: false);
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryFeedback" && m.GetParameters().Length == 0);
                if (method == null)
                {
                    SwapLog.Warn("FeedbackRouter：未找到 PageOtherLeft.TryFeedback。");
                    return;
                }

                var hook = typeof(FeedbackRouter).GetMethod(nameof(HookTryFeedback),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _detour = DetourInstaller.Install(method, hook);
                Installed = true;
                SwapLog.Info("反馈分流已安装：" + _detour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("FeedbackRouter 安装失败：" + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookTryFeedback()
        {
            try
            {
                var body = "你想向哪一方提交反馈？" + Environment.NewLine + Environment.NewLine +
                           "  PCL 启动器：Plain Craft Launcher 本身的 Bug 或建议（转交 PCL 官方反馈渠道）" +
                           Environment.NewLine +
                           "  PCL2 Swap：本工具（下载恢复 / 更新拦截 / 界面改动）的问题（转交 BlockConnect 项目）";

                int choice = PclUi.MsgBox(body, "反馈", "PCL 启动器", "PCL2 Swap", "取消");
                switch (choice)
                {
                    case 1:
                        SwapLog.Info("反馈分流：用户选择 PCL 启动器，走官方流程。");
                        if (_detour != null) _detour.InvokeOriginal(null);
                        return;
                    case 2:
                        SwapLog.Info("反馈分流：用户选择 PCL2 Swap，打开项目 Issues。");
                        PclUi.Hint("正在打开 PCL2 Swap 的反馈页面……", "Blue");
                        PclUi.OpenWebsite(SwapIssuesUrl);
                        return;
                    default:
                        SwapLog.Info("反馈分流：用户取消。");
                        return;
                }
            }
            catch (Exception ex)
            {
                SwapLog.Error("反馈分流异常，回退到官方流程：" + ex);
                try { if (_detour != null) _detour.InvokeOriginal(null); } catch { }
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
