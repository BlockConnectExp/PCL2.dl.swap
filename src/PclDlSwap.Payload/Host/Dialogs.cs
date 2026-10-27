using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Owner-facing prompts. Every prompt goes through PCL's own dialog / hint system
    /// (<see cref="PclUi"/>) so it looks and behaves like a native PCL window; the Win32 message box
    /// below is only the fallback for the rare moment where the host UI cannot be reached.
    /// </summary>
    internal static class Dialogs
    {
        private const uint MB_OK = 0x0;
        private const uint MB_YESNOCANCEL = 0x3;
        private const uint MB_ICONERROR = 0x10;
        private const uint MB_ICONWARNING = 0x30;
        private const uint MB_ICONINFORMATION = 0x40;
        private const uint MB_TOPMOST = 0x40000;
        private const uint MB_SETFOREGROUND = 0x10000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public enum OtaChoice
        {
            Cancel,
            Update,
            AbortAutoUpdate
        }

        internal static bool TestMode => Environment.GetEnvironmentVariable("PCL2SWAP_TEST") == "1";

        /// <summary>
        /// Explicit replacement for the silent OTA path. Returns Cancel when the owner must not be
        /// updated automatically.
        /// </summary>
        public static OtaChoice AskAboutSilentUpdate(string detail)
        {
            if (TestMode)
            {
                SwapLog.Warn("测试模式：静默 OTA 询问返回“取消”。来源=" + detail.Replace(Environment.NewLine, " "));
                return OtaChoice.Cancel;
            }

            var body = new StringBuilder();
            body.AppendLine("检测到 PCL 试图在后台静默更新启动器（OTA）。");
            body.AppendLine();
            body.AppendLine("静默更新会在你不注意时替换启动器本体，且下载内容不会先经过查毒沙箱。");
            body.AppendLine("本工具已把它改为显式询问：");
            body.AppendLine();
            body.AppendLine("  更新 PCL  = 现在更新（先下载到沙箱，多引擎查毒通过后才落盘）");
            body.AppendLine("  跳过本次  = 取消本次更新，保持当前版本");
            body.AppendLine("  保持禁用  = 取消并继续保持不自动更新");
            body.AppendLine();
            body.AppendLine("来源信息：");
            body.AppendLine(detail);

            int result = PclUi.MsgBox(body.ToString(), "PCL2 Swap · 拦截到静默更新",
                "更新 PCL", "跳过本次", "保持禁用", isWarn: true);

            switch (result)
            {
                case 1: return OtaChoice.Update;
                case 2: return OtaChoice.AbortAutoUpdate;
                default: return OtaChoice.Cancel;
            }
        }

        public static void BlockedDownload(string destination, string report)
        {
            if (TestMode)
            {
                SwapLog.Warn("测试模式：跳过拦截对话框。目标=" + destination);
                return;
            }

            var body = new StringBuilder();
            body.AppendLine("PCL 云服务下载未通过安全检查，已阻止落盘。");
            body.AppendLine();
            body.AppendLine("目标路径：");
            body.AppendLine(destination);
            body.AppendLine();
            body.AppendLine("检测报告：");
            body.AppendLine(report);

            PclUi.MsgBox(body.ToString(), "PCL2 Swap · 已拦截下载", "我知道了", "", "",
                isWarn: true, highLight: false);
        }

        public static void Info(string title, string text)
        {
            MessageBoxW(IntPtr.Zero, text, title, MB_OK | MB_ICONINFORMATION | MB_TOPMOST | MB_SETFOREGROUND);
        }

        /// <summary>Win32 fallback for a two-button question (1 = first button, 2 = second).</summary>
        public static int Confirm(string title, string text, string button1, string button2, bool isWarn = false)
        {
            var body = text + Environment.NewLine + Environment.NewLine + button1 + " = 确定，" + button2 + " = 取消";
            int result = MessageBoxW(IntPtr.Zero, body, title,
                MB_YESNOCANCEL | (isWarn ? MB_ICONWARNING : MB_ICONINFORMATION) | MB_TOPMOST | MB_SETFOREGROUND);
            switch (result)
            {
                case 6: return 1;   // Yes
                case 7: return 2;   // No
                default: return 0;  // Cancel / failure
            }
        }

        /// <summary>
        /// Raises a prompt on a dedicated STA thread. Kept for callers that must not block the host's
        /// UI thread pool while the user decides.
        /// </summary>
        public static T OnStaThread<T>(Func<T> body, T fallback)
        {
            T result = fallback;
            var thread = new Thread(() =>
            {
                try { result = body(); }
                catch (Exception ex) { SwapLog.Error("对话线程异常：" + ex); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
