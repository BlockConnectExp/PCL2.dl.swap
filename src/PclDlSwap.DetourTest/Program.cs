using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PclDlSwap.Payload.Hook;

namespace PclDlSwap.DetourTest
{
    internal static class Program
    {
        private static int _targetRuns;
        private static string _hookUrl;
        private static string _hookPath;
        private static bool _hookFlag;
        private static List<string> _targetUrls;
        private static string _targetPath;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadDelegate(IEnumerable<string> urls, string localPath, object loader, object checker, bool flag);

        private static void Target(IEnumerable<string> urls, string localPath, object loader, object checker, bool flag)
        {
            _targetRuns++;
            _targetUrls = new List<string>(urls);
            _targetPath = localPath;
        }

        private static void Hook(IEnumerable<string> urls, string localPath, object loader, object checker, bool flag)
        {
            var list = urls == null ? new List<string>() : new List<string>(urls);
            _hookUrl = list.Count > 0 ? list[0] : "(null)";
            _hookPath = localPath;
            _hookFlag = flag;
            Trampoline(urls, localPath, loader, checker, flag);
        }

        private static DownloadDelegate Trampoline;

        private static int Main()
        {
            var targetMethod = typeof(Program).GetMethod("Target", BindingFlags.NonPublic | BindingFlags.Static);
            var hookMethod = typeof(Program).GetMethod("Hook", BindingFlags.NonPublic | BindingFlags.Static);
            DownloadDelegate caller = Target;
            caller(new[] { "warmup" }, "warmup-path", null, null, false);
            Console.WriteLine("warmup runs=" + _targetRuns);

            RuntimeHelpers.PrepareMethod(targetMethod.MethodHandle);
            RuntimeHelpers.PrepareMethod(hookMethod.MethodHandle);

            IntPtr target = targetMethod.MethodHandle.GetFunctionPointer();
            IntPtr hook = hookMethod.MethodHandle.GetFunctionPointer();
            Console.WriteLine("target=0x" + target.ToInt64().ToString("X") + " hook=0x" + hook.ToInt64().ToString("X"));

            var callerPre = (DownloadDelegate)Delegate.CreateDelegate(typeof(DownloadDelegate), targetMethod);
            IntPtr callSite = Marshal.GetFunctionPointerForDelegate(callerPre);
            Console.WriteLine("target  bytes: " + Bytes(target, 16));
            Console.WriteLine("hook    bytes: " + Bytes(hook, 16));
            Console.WriteLine("call deleg fn: " + Bytes(callSite, 16));
            Console.WriteLine("PrepareMethod target bytes: " + Bytes(target, 16));

            var handler = (DownloadDelegate)Delegate.CreateDelegate(typeof(DownloadDelegate), hookMethod);
            var detour = X86Detour.Install(target, hook);
            Trampoline = (DownloadDelegate)Marshal.GetDelegateForFunctionPointer(detour.Trampoline, typeof(DownloadDelegate));
            GC.KeepAlive(handler);

            Console.WriteLine("installed; target[0]=" + Marshal.ReadByte(target).ToString("X2"));
            Console.WriteLine("trampoline bytes: " + Bytes(detour.Trampoline, 16));

            // Isolate the trampoline BEFORE the target is patched: if the stolen prologue plus
            // jump-back is sound, calling the trampoline must reach the real body with real args.
            var trampolineProbe = (DownloadDelegate)Marshal.GetDelegateForFunctionPointer(detour.Trampoline, typeof(DownloadDelegate));
            _targetUrls = null;
            _targetPath = null;
            int runsBefore = _targetRuns;
            trampolineProbe(new[] { "trampoline-probe" }, "trampoline-path", null, null, false);
            Console.WriteLine("direct trampoline call: runs " + runsBefore + " -> " + _targetRuns +
                              " url=" + (_targetUrls != null && _targetUrls.Count > 0 ? _targetUrls[0] : "(null)") +
                              " path=" + (_targetPath ?? "(null)"));

            try
            {
                caller(new[] { "https://pcl2-server.example/update.zip" }, @"C:\tmp\update.zip", null, null, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("caller threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            Console.WriteLine("hook  saw url=" + _hookUrl + " path=" + _hookPath + " flag=" + _hookFlag);
            Console.WriteLine("target ran " + _targetRuns + " time(s); last url=" + (_targetUrls != null && _targetUrls.Count > 0 ? _targetUrls[0] : "(none)") + " path=" + _targetPath);

            bool ok = _targetRuns == 2 && _hookUrl == "https://pcl2-server.example/update.zip" && _hookPath == @"C:\tmp\update.zip";
            Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            return ok ? 0 : 1;
        }

        private static string Bytes(IntPtr pointer, int count)
        {
            var buffer = new byte[count];
            Marshal.Copy(pointer, buffer, 0, count);
            var text = new System.Text.StringBuilder();
            foreach (var b in buffer) text.Append(b.ToString("X2")).Append(' ');
            return text.ToString();
        }
    }
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->