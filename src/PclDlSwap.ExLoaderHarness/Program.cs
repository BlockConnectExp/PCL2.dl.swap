using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PclDlSwap.ExLoaderHarness
{
    public delegate int CalcHook(int a, int b);
    public delegate int ManagedHook(IEnumerable<string> urls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UpdateProbeDelegate(bool silent, IEnumerable<string> urls);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RawProbeHook(IntPtr text, IntPtr payload);

    /// <summary>Throwaway targets for the detour-engine self test.</summary>
    public static class EngineProbe
    {
        public static int UpdateProbeCalls;
        public static int UpdateProbeHookInvocations;
        private static UpdateProbeDelegate _updateProbeOriginal;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Calc(int a, int b)
        {
            var pad = "engine.calc." + a;
            return pad.Length > 0 ? a + b : a - b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Managed(IEnumerable<string> urls)
        {
            var pad = "engine.managed";
            return pad.Length > 0 ? (urls == null ? -1 : urls.Count()) : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateProbe(bool silent, IEnumerable<string> urls)
        {
            var pad = "engine.update." + silent;
            UpdateProbeCalls += pad.Length > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateProbe2(bool silent, IEnumerable<string> urls)
        {
            var pad = "engine.update2." + silent;
            UpdateProbeCalls += pad.Length > 0 ? 1 : 0;
        }

        public static UpdateProbeDelegate MakeNestedHook()
        {
            return (silent, urls) =>
            {
                UpdateProbeHookInvocations++;
                _updateProbeOriginal(silent, urls);
            };
        }

        public static void SetNestedHookOriginal(UpdateProbeDelegate original)
        {
            _updateProbeOriginal = original;
        }

        public static string RawProbeText;
        public static int RawProbeCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RawProbe(string text, object payload)
        {
            RawProbeText = "raw:" + text + ":" + (payload == null ? "null" : payload.GetType().Name);
            RawProbeCalls += RawProbeText.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>
    /// Call counters the payload's hooks are expected to drive. Kept in its own type so the PCL
    /// stand-ins stay tiny and readable.
    /// </summary>
    public static class Probes
    {
        public static int UpdateStartCalls;
        public static int LoaderInitCalls;
        public static int SidebarCtorCalls;
        public static int AboutCtorCalls;
        public static int DownloadByLoaderCalls;
        public static int DownloadByClientCalls;
        public static int FeedbackCalls;
    }

    /// <summary>
    /// x64 .NET Framework host that mimics PCL: it exposes the same PCL.* type surface, loads the
    /// ExLoader shim with LoadLibrary, and then proves that
    ///   shim -&gt; CLR hosting -&gt; payload -&gt; x64 detours -&gt; original trampolines
    /// all work without the real launcher (and without ExLoader).
    /// </summary>
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length >= 2 && args[0] == "--engine") return RunEngineTest(args[1]);

            var reversedOta = args.Contains("--reversed-ota");
            var positional = args.Where(a => !a.StartsWith("--")).ToArray();

            if (positional.Length < 1)
            {
                Console.Error.WriteLine("usage: PclDlSwap.ExLoaderHarness <path-to-Pcl2Swap.Shim.x64.dll> [--reversed-ota]");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --engine <path-to-PclDlSwap.Payload.dll>");
                return 2;
            }

            var shim = Path.GetFullPath(positional[0]);
            if (!File.Exists(shim))
            {
                Console.Error.WriteLine("shim not found: " + shim);
                return 2;
            }

            // Make every prompt path non-interactive before the payload reads the environment.
            Environment.SetEnvironmentVariable("PCL2SWAP_TEST", "1");
            Environment.SetEnvironmentVariable("PCL2SWAP_TEST_NOOP", "1");

            ResetLogs();

            Console.WriteLine("[harness] process   : " + (Environment.Is64BitProcess ? "x64" : "x86") +
                              ", clr=" + Environment.Version);
            Console.WriteLine("[harness] shim      : " + shim);

            // Dump the JIT prologues before anything is hooked: these bytes are what the x64 detour
            // engine must relocate, and they make precode-vs-body failures self-diagnosing.
            Console.WriteLine("[harness] prologue dump (pre-injection):");
            DumpPrologue("ModSecret.UpdateStart", typeof(PCL.ModSecret).GetMethod("UpdateStart"));
            DumpPrologue("ModNet.NetDownloadByLoader(IEnumerable<string>)",
                typeof(PCL.ModNet).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "NetDownloadByLoader" &&
                                m.GetParameters()[0].ParameterType != typeof(string)));
            DumpPrologue("PageDownloadInstall.LoaderInit",
                typeof(PCL.PageDownloadInstall).GetMethod("LoaderInit"));
            DumpPrologue("PageDownloadLeft..ctor",
                typeof(PCL.PageDownloadLeft).GetConstructors().First());
            DumpPrologue("PageOtherAbout..ctor",
                typeof(PCL.PageOtherAbout).GetConstructors().First());

            var module = LoadLibraryW(shim);
            Console.WriteLine("[harness] LoadLibrary -> 0x" + module.ToInt64().ToString("X") +
                              " (win32=" + Marshal.GetLastWin32Error() + ")");
            if (module == IntPtr.Zero) return 3;

            var compiledAt = DateTime.UtcNow;
            var payloadLog = WaitForPayloadLog(compiledAt, TimeSpan.FromSeconds(120));
            var payloadText = payloadLog == null ? string.Empty : SafeRead(payloadLog);

            var checks = new List<(string Name, bool Ok, string Detail)>();
            checks.Add(("payload assembled inside the harness",
                payloadText.Contains("注入式装配完成。"),
                payloadLog == null ? "no %TEMP%\\PCL2Swap log found" : "log=" + payloadLog));

            // -- behavioural checks: hooks must change host behaviour while trampolines stay callable --

            int otaBefore = Probes.UpdateStartCalls;
            bool silentOtaOk;
            bool explicitOtaOk;
            if (reversedOta)
            {
                Console.WriteLine("[step] explicit OTA first (reversed order) ...");
                PCL.ModSecret.UpdateStart(false, null);
                explicitOtaOk = Probes.UpdateStartCalls == otaBefore + 1;
                Console.WriteLine("[step] explicit OTA returned; calling silent OTA ...");
                var afterExplicit = Probes.UpdateStartCalls;
                PCL.ModSecret.UpdateStart(true, null);
                silentOtaOk = Probes.UpdateStartCalls == afterExplicit;
                Console.WriteLine("[step] silent OTA returned.");
            }
            else
            {
                Console.WriteLine("[step] silent OTA first ...");
                PCL.ModSecret.UpdateStart(true, null);
                silentOtaOk = Probes.UpdateStartCalls == otaBefore;
                Console.WriteLine("[step] silent OTA returned; calling explicit OTA ...");
                PCL.ModSecret.UpdateStart(false, null);
                explicitOtaOk = Probes.UpdateStartCalls == otaBefore + 1;
                Console.WriteLine("[step] explicit OTA returned.");
            }
            checks.Add(("silent OTA intercepted (original not called)", silentOtaOk,
                "calls=" + Probes.UpdateStartCalls));
            checks.Add(("explicit update passes through the trampoline", explicitOtaOk,
                "calls=" + Probes.UpdateStartCalls));

            int dlBefore = Probes.DownloadByLoaderCalls;
            Console.WriteLine("[step] non-cloud download ...");
            PCL.ModNet.NetDownloadByLoader(new[] { "https://example.com/file.bin" },
                Path.Combine(Path.GetTempPath(), "harness-download.bin"), null, null, false);
            Console.WriteLine("[step] non-cloud download returned.");
            checks.Add(("non-cloud download passes through the trampoline",
                Probes.DownloadByLoaderCalls == dlBefore + 1,
                "calls=" + Probes.DownloadByLoaderCalls));

            // -- announcement guard: the forced "悲报" notice gains a 取消 button and its update is dropped --
            Console.WriteLine("[step] forced announcement ...");
            PCL.ModMain.NextResult = 2;
            var msgCallsBefore = PCL.ModMain.MsgBoxCalls;
            PCL.ModMain.MyMsgBox("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能",
                "悲报", "更新", "", "", false, true, true);
            var noticeRewritten = PCL.ModMain.MsgBoxCalls == msgCallsBefore + 1 &&
                                  PCL.ModMain.LastButton2 == "取消" && !PCL.ModMain.LastForceWait;
            Console.WriteLine("[step] forced announcement returned; button2=" + PCL.ModMain.LastButton2 +
                              ", forceWait=" + PCL.ModMain.LastForceWait);
            var otaCallsBeforeDrop = Probes.UpdateStartCalls;
            PCL.ModSecret.UpdateStart(true, null);
            var updateDropped = Probes.UpdateStartCalls == otaCallsBeforeDrop;
            checks.Add(("forced announcement gains 取消 and drops the follow-up update",
                noticeRewritten && updateDropped,
                "button2=" + PCL.ModMain.LastButton2 + " forceWait=" + PCL.ModMain.LastForceWait +
                " updateOriginalCalls=" + Probes.UpdateStartCalls));

            // -- feedback routing: the host dialog is consulted first --
            Console.WriteLine("[step] feedback routing ...");
            PCL.ModMain.NextResult = 0;
            var feedbackMsgBefore = PCL.ModMain.MsgBoxCalls;
            PCL.PageOtherLeft.TryFeedback();
            var feedbackRouted = PCL.ModMain.MsgBoxCalls == feedbackMsgBefore + 1 &&
                                 Probes.FeedbackCalls == 0;
            Console.WriteLine("[step] feedback routing returned; msgBoxCalls=" + PCL.ModMain.MsgBoxCalls +
                              ", originalCalls=" + Probes.FeedbackCalls);
            checks.Add(("feedback routing asks the host dialog first", feedbackRouted,
                "msgBoxCalls=" + PCL.ModMain.MsgBoxCalls + " originalCalls=" + Probes.FeedbackCalls));

            // -- LoaderInit: the real download loader must be re-driven with live page controls --
            Console.WriteLine("[step] LoaderInit re-drive ...");
            PCL.MyPageRight.LoaderInitCalls = 0;
            PCL.PageDownloadInstall.ControlInitCalls = 0;
            PCL.PageDownloadInstall.OnFinishCalls = 0;
            var installer = new PCL.PageDownloadInstall();
            installer.LoaderInit();
            var loaderOk = PCL.MyPageRight.LoaderInitCalls == 1 &&
                           PCL.MyPageRight.LastLoaderUi != null &&
                           PCL.MyPageRight.LastPanLoader != null &&
                           PCL.MyPageRight.LastPanContent != null &&
                           PCL.PageDownloadInstall.OnFinishCalls == 1;
            Console.WriteLine("[step] LoaderInit returned; PageLoaderInit calls=" +
                              PCL.MyPageRight.LoaderInitCalls + ", onFinish=" +
                              PCL.PageDownloadInstall.OnFinishCalls + ", controls resolved=" +
                              (PCL.MyPageRight.LastLoaderUi != null) + "/" +
                              (PCL.MyPageRight.LastPanLoader != null) + "/" +
                              (PCL.MyPageRight.LastPanContent != null));
            checks.Add(("LoaderInit re-drives the real loader with live controls", loaderOk,
                "pageLoaderInit=" + PCL.MyPageRight.LoaderInitCalls +
                " onFinish=" + PCL.PageDownloadInstall.OnFinishCalls));

            // -- poll-based UI repairs: give both watchers a page to find --
            Console.WriteLine("[step] poll-based sidebar/About repairs ...");
            var sidebar = new PCL.PageDownloadLeft();
            var about = new PCL.PageOtherAbout();
            PCL.ModMain.FrmDownloadLeft = sidebar;
            PCL.ModMain.FrmOtherAbout = about;
            var pollDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < pollDeadline)
            {
                if (sidebar.ItemInstall.Visibility == PCL.StubVisibility.Visible &&
                    sidebar.PageID == PCL.FormMain.PageSubType.Default &&
                    about.PanMain.Children.Count == 2)
                    break;
                Thread.Sleep(100);
            }
            checks.Add(("sidebar poller restores the collapsed entry",
                sidebar.ItemInstall.Visibility == PCL.StubVisibility.Visible &&
                sidebar.PageID == PCL.FormMain.PageSubType.Default &&
                sidebar.ItemMod.Checked == false,
                "visibility=" + sidebar.ItemInstall.Visibility + " pageId=" + sidebar.PageID +
                " modChecked=" + sidebar.ItemMod.Checked));
            checks.Add(("About poller inserts the brand row",
                about.PanMain.Children.Count == 2 &&
                about.PanMain.Children[1] is PCL.MyListItem &&
                ((PCL.MyListItem)about.PanMain.Children[1]).Title == "PCL2 Swap By BlockConnect",
                "children=" + about.PanMain.Children.Count));

            // keep the instances alive past the checks (the JIT must not see them as dead stores)
            GC.KeepAlive(sidebar);
            GC.KeepAlive(about);
            GC.KeepAlive(installer);

            Console.WriteLine();
            var failed = 0;
            foreach (var (name, ok, detail) in checks)
            {
                Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  [" + detail + "]");
                if (!ok) failed++;
            }

            Console.WriteLine();
            Console.WriteLine("[harness] --- payload log highlights ---");
            foreach (var line in DumpHighlights(payloadText))
                Console.WriteLine("  " + line);

            var nativeLog = Path.Combine(Path.GetTempPath(), "pcl2swap-native.log");
            if (File.Exists(nativeLog))
            {
                Console.WriteLine("[harness] --- native shim log ---");
                foreach (var line in SafeReadUnicode(nativeLog).Split('\n').Take(24))
                    Console.WriteLine("  " + line.TrimEnd());
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "[harness] RESULT: PASS (" + checks.Count + "/" + checks.Count + ")"
                : "[harness] RESULT: FAIL (" + (checks.Count - failed) + "/" + checks.Count + ")");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Direct exercise of the payload's x64 detour engine without the shim: proves the trampoline
        /// for a blittable signature and for a managed-reference signature (the shape every real hook
        /// in this project uses).
        /// </summary>
        private static int RunEngineTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full))
            {
                Console.Error.WriteLine("payload not found: " + full);
                return 2;
            }

            var assembly = Assembly.LoadFrom(full);
            var detourType = assembly.GetType("PclDlSwap.Payload.Hook.X64Detour", throwOnError: true);
            var installMethod = detourType.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
            var trampolineProp = detourType.GetProperty("Trampoline", BindingFlags.Public | BindingFlags.Instance);
            var failures = 0;

            // Mirror the shim: preload the x64 disassembler before any detour runs.
            var capstonePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".nuget\packages\gee.external.capstone\2.3.0\runtimes\win-x64\native\capstone.dll");
            if (File.Exists(capstonePath))
            {
                var handle = LoadLibraryW(capstonePath);
                Console.WriteLine("  [step] capstone preloaded: 0x" + handle.ToInt64().ToString("X"));
            }
            else
            {
                Console.WriteLine("  [warn] x64 capstone.dll not found: " + capstonePath);
            }

            Console.WriteLine("[engine] A) blittable signature (int, int)");
            var calcMethod = typeof(EngineProbe).GetMethod(nameof(EngineProbe.Calc));
            DumpPrologue("EngineProbe.Calc", calcMethod);
            RuntimeHelpers.PrepareMethod(calcMethod.MethodHandle);
            var calcTarget = calcMethod.MethodHandle.GetFunctionPointer();
            CalcHook calcHook = (a, b) => a * b;
            Console.WriteLine("  [step] hook delegate pointer ...");
            var calcHookPointer = Marshal.GetFunctionPointerForDelegate(calcHook);
            Console.WriteLine("  [step] installing detour ...");
            object calcDetour;
            try
            {
                calcDetour = installMethod.Invoke(null, new object[] { calcTarget, calcHookPointer });
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [error] detour install threw: " + ex.GetType().FullName + ": " + ex.Message +
                                  " | inner=" + (ex.InnerException == null ? "(none)" : ex.InnerException.ToString()));
                return 1;
            }
            Console.WriteLine("  [step] detour installed; reading trampoline ...");
            var calcTrampoline = (IntPtr)trampolineProp.GetValue(calcDetour);
            Console.WriteLine("  [step] trampoline = 0x" + calcTrampoline.ToInt64().ToString("X") +
                              "; creating reverse delegate ...");
            var calcOriginal = Marshal.GetDelegateForFunctionPointer<CalcHook>(calcTrampoline);

            Console.WriteLine("  [step] invoking hooked Calc ...");
            var hookedCalc = (int)calcMethod.Invoke(null, new object[] { 3, 4 });
            Console.WriteLine("  [step] hooked Calc returned, invoking trampoline ...");
            var trampolinedCalc = calcOriginal(3, 4);
            Console.WriteLine("  hooked  Calc(3,4)   = " + hookedCalc + " (expect 12)");
            Console.WriteLine("  original Calc(3,4) = " + trampolinedCalc + " (expect 7)");
            if (hookedCalc != 12) failures++;
            if (trampolinedCalc != 7) failures++;
            GC.KeepAlive(calcHook);

            Console.WriteLine("[engine] B) managed-reference signature (IEnumerable<string>)");
            var managedMethod = typeof(EngineProbe).GetMethod(nameof(EngineProbe.Managed));
            DumpPrologue("EngineProbe.Managed", managedMethod);
            RuntimeHelpers.PrepareMethod(managedMethod.MethodHandle);
            var managedTarget = managedMethod.MethodHandle.GetFunctionPointer();
            ManagedHook managedHook = urls => 42;
            var managedDetour = installMethod.Invoke(null,
                new object[] { managedTarget, Marshal.GetFunctionPointerForDelegate(managedHook) });
            var managedTrampoline = (IntPtr)trampolineProp.GetValue(managedDetour);

            Console.WriteLine("  [step] invoking hooked Managed ...");
            var hookedManaged = (int)managedMethod.Invoke(null, new object[] { null });
            Console.WriteLine("  hooked  Managed(null)   = " + hookedManaged + " (expect 42)");
            if (hookedManaged != 42) failures++;

            var managedOriginal = Marshal.GetDelegateForFunctionPointer<ManagedHook>(managedTrampoline);
            Console.WriteLine("  [step] invoking trampoline Managed ...");
            var trampolinedManaged = managedOriginal(null);
            Console.WriteLine("  original Managed(null) = " + trampolinedManaged + " (expect -1)");
            if (trampolinedManaged != -1) failures++;
            GC.KeepAlive(managedHook);

            Console.WriteLine("[engine] C) UpdateStart-shaped delegate (Cdecl, bool + IEnumerable<string>)");
            var updateMethod = typeof(EngineProbe).GetMethod(nameof(EngineProbe.UpdateProbe));
            DumpPrologue("EngineProbe.UpdateProbe", updateMethod);
            RuntimeHelpers.PrepareMethod(updateMethod.MethodHandle);
            var updateTarget = updateMethod.MethodHandle.GetFunctionPointer();
            UpdateProbeDelegate flatHook = (silent, urls) => { };
            var updateDetour = installMethod.Invoke(null,
                new object[] { updateTarget, Marshal.GetFunctionPointerForDelegate(flatHook) });
            var updateTrampoline = (IntPtr)trampolineProp.GetValue(updateDetour);
            var updateOriginal =
                (UpdateProbeDelegate)Marshal.GetDelegateForFunctionPointer(updateTrampoline, typeof(UpdateProbeDelegate));
            Console.WriteLine("  [step] C: calling trampoline directly ...");
            var updateBefore = EngineProbe.UpdateProbeCalls;
            updateOriginal(false, null);
            Console.WriteLine("  [step] C: trampoline returned; calls delta = " +
                              (EngineProbe.UpdateProbeCalls - updateBefore) + " (expect 1)");
            if (EngineProbe.UpdateProbeCalls - updateBefore != 1) failures++;
            GC.KeepAlive(flatHook);

            Console.WriteLine("[engine] D) hook -> original nesting (the shape used in production)");
            var nestedMethod = typeof(EngineProbe).GetMethod(nameof(EngineProbe.UpdateProbe2));
            DumpPrologue("EngineProbe.UpdateProbe2", nestedMethod);
            RuntimeHelpers.PrepareMethod(nestedMethod.MethodHandle);
            var nestedTarget = nestedMethod.MethodHandle.GetFunctionPointer();
            var nestedHook = EngineProbe.MakeNestedHook();
            var nestedDetour = installMethod.Invoke(null,
                new object[] { nestedTarget, Marshal.GetFunctionPointerForDelegate(nestedHook) });
            var nestedTrampoline = (IntPtr)trampolineProp.GetValue(nestedDetour);
            EngineProbe.SetNestedHookOriginal(
                (UpdateProbeDelegate)Marshal.GetDelegateForFunctionPointer(nestedTrampoline,
                    typeof(UpdateProbeDelegate)));
            Console.WriteLine("  [step] D: invoking hooked method (hook calls the original) ...");
            var nestedBefore = EngineProbe.UpdateProbeCalls;
            nestedMethod.Invoke(null, new object[] { false, null });
            Console.WriteLine("  [step] D: returned; calls delta = " +
                              (EngineProbe.UpdateProbeCalls - nestedBefore) +
                              ", hook invocations = " + EngineProbe.UpdateProbeHookInvocations);
            if (EngineProbe.UpdateProbeCalls - nestedBefore != 1) failures++;
            if (EngineProbe.UpdateProbeHookInvocations != 1) failures++;
            GC.KeepAlive(nestedHook);

            Console.WriteLine("[engine] E) raw pointer mirror + ObjectPointer round-trip (string + object)");
            var objectPointerType = assembly.GetType("PclDlSwap.Payload.Hook.ObjectPointer", throwOnError: true);
            var toObjectMethod = objectPointerType.GetMethods()
                .First(m => m.Name == "ToObject" && !m.IsGenericMethod &&
                            m.GetParameters().Length == 1);
            var toPointerMethod = objectPointerType.GetMethods()
                .First(m => m.Name == "ToPointer");

            var roundTripText = new string("round-trip".ToCharArray());
            var roundTripPtr = (IntPtr)toPointerMethod.Invoke(null, new object[] { roundTripText });
            var roundTripBack = toObjectMethod.Invoke(null, new object[] { roundTripPtr });
            Console.WriteLine("  [step] ObjectPointer round-trip = " + roundTripBack + " (expect " + roundTripText + ")");
            if (!Equals(roundTripBack, roundTripText)) failures++;
            GC.KeepAlive(roundTripText);

            var rawMethod = typeof(EngineProbe).GetMethod(nameof(EngineProbe.RawProbe));
            DumpPrologue("EngineProbe.RawProbe", rawMethod);
            RuntimeHelpers.PrepareMethod(rawMethod.MethodHandle);
            var rawTarget = rawMethod.MethodHandle.GetFunctionPointer();
            string capturedText = null;
            object capturedPayload = null;
            RawProbeHook rawHook = (textPtr, payloadPtr) =>
            {
                capturedText = (string)toObjectMethod.Invoke(null, new object[] { textPtr });
                capturedPayload = toObjectMethod.Invoke(null, new object[] { payloadPtr });
            };
            var rawDetour = installMethod.Invoke(null,
                new object[] { rawTarget, Marshal.GetFunctionPointerForDelegate(rawHook) });
            var rawTrampoline = (IntPtr)trampolineProp.GetValue(rawDetour);
            var rawOriginal = Marshal.GetDelegateForFunctionPointer<RawProbeHook>(rawTrampoline);

            EngineProbe.RawProbeCalls = 0;
            var payloadToken = new object();
            Console.WriteLine("  [step] E1: calling hooked method with managed references ...");
            rawMethod.Invoke(null, new object[] { "hello", payloadToken });
            Console.WriteLine("  captured text = " + capturedText + " (expect hello); payload same reference = " +
                              ReferenceEquals(capturedPayload, payloadToken) + "; original calls = " +
                              EngineProbe.RawProbeCalls + " (expect 0)");
            if (capturedText != "hello") failures++;
            if (!ReferenceEquals(capturedPayload, payloadToken)) failures++;
            if (EngineProbe.RawProbeCalls != 0) failures++;

            EngineProbe.RawProbeCalls = 0;
            EngineProbe.RawProbeText = null;
            var trampolineText = new string("via-ptr".ToCharArray());
            var trampolinePayloadPtr = (IntPtr)toPointerMethod.Invoke(null, new object[] { payloadToken });
            Console.WriteLine("  [step] E2: calling trampoline with pointers ...");
            rawOriginal((IntPtr)toPointerMethod.Invoke(null, new object[] { trampolineText }), trampolinePayloadPtr);
            Console.WriteLine("  RawProbeText = " + EngineProbe.RawProbeText +
                              " (expect raw:via-ptr:Object); calls = " + EngineProbe.RawProbeCalls);
            if (EngineProbe.RawProbeText != "raw:via-ptr:Object") failures++;
            if (EngineProbe.RawProbeCalls != 1) failures++;
            GC.KeepAlive(trampolineText);
            GC.KeepAlive(payloadToken);
            GC.KeepAlive(rawHook);

            Console.WriteLine(failures == 0
                ? "[engine] RESULT: PASS"
                : "[engine] RESULT: FAIL (" + failures + " check(s))");
            return failures == 0 ? 0 : 1;
        }

        private static void ResetLogs()
        {
            var root = Path.Combine(Path.GetTempPath(), "PCL2Swap");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // a previous launcher instance may still hold the log open
            }

            try
            {
                var native = Path.Combine(Path.GetTempPath(), "pcl2swap-native.log");
                if (File.Exists(native)) File.Delete(native);
            }
            catch
            {
                // same tolerance as above
            }
        }

        private static void DumpPrologue(string label, MethodBase method)
        {
            if (method == null) return;
            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                var ptr = method.MethodHandle.GetFunctionPointer();
                var bytes = new byte[48];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                Console.WriteLine("  " + label + " -> 0x" + ptr.ToInt64().ToString("X"));
                Console.WriteLine("    " + BitConverter.ToString(bytes).Replace("-", " "));
            }
            catch (Exception ex)
            {
                Console.WriteLine("  " + label + " dump failed: " + ex.Message);
            }
        }

        private static string WaitForPayloadLog(DateTime compiledAfter, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            string newest = null;
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
                newest = FindLatestPayloadLog();
                if (newest != null)
                {
                    var text = SafeRead(newest);
                    if (text.Contains("注入式装配完成。") || text.Contains("注入式装配失败"))
                        return newest;
                }
            }
            return newest;
        }

        private static string FindLatestPayloadLog()
        {
            var root = Path.Combine(Path.GetTempPath(), "PCL2Swap");
            if (!Directory.Exists(root)) return null;
            try
            {
                return Directory.GetFiles(root, "pcl2swap.log", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .Select(info => info.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> DumpHighlights(string text)
        {
            if (string.IsNullOrEmpty(text)) return new[] { "(no payload log)" };
            return text.Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.Contains("已安装") || line.Contains("bits=x64") ||
                               line.Contains("失败") || line.Contains("error") ||
                               line.Contains("装配") || line.Contains("拦截") ||
                               line.Contains("OTA"))
                .Take(40)
                .ToArray();
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch { return string.Empty; }
        }

        private static string SafeReadUnicode(string path)
        {
            // the shim writes its log as UTF-16LE (WriteFile with wide characters)
            try { return File.ReadAllText(path, Encoding.Unicode); }
            catch { return string.Empty; }
        }
    }
}

namespace PCL
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Stand-in for PCL.ModSecret. Every member is real IL so the x64 detour engine has a genuine
    /// JIT-compiled prologue to steal.
    /// </summary>
    public static class ModSecret
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateStart(bool silent, IEnumerable<string> urlsOverride)
        {
            var pad = "pcl2swap.harness.update." + silent;
            var sink = urlsOverride == null ? pad : pad + urlsOverride.Count();
            PclDlSwap.ExLoaderHarness.Probes.UpdateStartCalls += sink.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>Stand-in for PCL.ModNet.</summary>
    public static class ModNet
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NetDownloadByLoader(IEnumerable<string> urls, string localPath,
            object loader, object checker, bool simulateBrowserHeaders)
        {
            var pad = "pcl2swap.harness.loader." + (urls == null ? 0 : urls.Count());
            PclDlSwap.ExLoaderHarness.Probes.DownloadByLoaderCalls += pad.Length > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NetDownloadByLoader(string url, string localPath,
            object loader, object checker, bool simulateBrowserHeaders)
        {
            var pad = "pcl2swap.harness.loader.single." + (url ?? string.Empty).Length;
            PclDlSwap.ExLoaderHarness.Probes.DownloadByLoaderCalls += pad.Length > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NetDownloadByClient(string url, string localPath, bool simulateBrowserHeaders)
        {
            var pad = "pcl2swap.harness.client." + (url ?? string.Empty).Length;
            PclDlSwap.ExLoaderHarness.Probes.DownloadByClientCalls += pad.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>Stand-in for PCL.LoaderTask&lt;TIn, TOut&gt; (the download list loader's shape).</summary>
    public class LoaderTask<TIn, TOut>
    {
    }

    /// <summary>Stand-in for PCL.MyPageRight, carrying the real PageLoaderInit&lt;,&gt; generic shape.</summary>
    public class MyPageRight
    {
        public static int LoaderInitCalls;
        public static object LastLoaderUi;
        public static object LastPanLoader;
        public static object LastPanContent;
        public static object LastRealLoader;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void PageLoaderInit<TIn, TOut>(object loaderUi, object panLoader, object panContent, object panAlways,
            LoaderTask<TIn, TOut> realLoader, Action<LoaderTask<TIn, TOut>> finished, Func<TIn> input, bool autoRun)
        {
            LoaderInitCalls++;
            LastLoaderUi = loaderUi;
            LastPanLoader = panLoader;
            LastPanContent = panContent;
            LastRealLoader = realLoader;
            var pad = "harness.pageloaderinit." + autoRun;
            if (pad.Length > 0 && finished != null) finished(realLoader);
        }
    }

    /// <summary>Stand-in for PCL.ModDownload (the built-in download module that must stay reachable).</summary>
    public static class ModDownload
    {
        public static object DlClientListLoader = new LoaderTask<string, object>();
        public static int DlClientListMainCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DlClientListMain(object context)
        {
            var pad = "harness.dlclientlist." + (context == null ? 0 : 1);
            DlClientListMainCalls += pad.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>Stand-in for PCL.PageDownloadInstall.</summary>
    public class PageDownloadInstall : MyPageRight
    {
        // The real members are VB.NET properties; deliver the same shape so the member reader is tested.
        public object LoadMinecraft { get; } = new object();
        public object PanLoad { get; } = new object();
        public object PanAllBack { get; } = new object();
        public static int ControlInitCalls;
        public static int OnFinishCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ControlInit()
        {
            var pad = "harness.controlinit." + GetHashCode();
            ControlInitCalls += pad.Length > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LoaderInit()
        {
            // Carry the 2.99 gate marker so the install-time stub probe accepts this build.
            var marker = "关闭下载";
            var pad = "pcl2swap.harness.loaderinit." + marker.Length + GetHashCode();
            PclDlSwap.ExLoaderHarness.Probes.LoaderInitCalls += pad.Length > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LoadMinecraft_OnFinish()
        {
            var pad = "harness.onfinish";
            OnFinishCalls += pad.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>WPF-free visibility enum for the sidebar stub.</summary>
    public enum StubVisibility
    {
        Visible,
        Collapsed,
        Hidden
    }

    /// <summary>Stand-in for PCL.MyListItem with the members the branding row sets.</summary>
    public class MyListItem
    {
        public string Title { get; set; }
        public string Info { get; set; }
        public string Logo { get; set; }
    }

    /// <summary>Stand-in for PCL.FormMain carrying the PageSubType enum.</summary>
    public class FormMain
    {
        public enum PageSubType
        {
            Default,
            Mod,
            Pack
        }
    }

    /// <summary>Minimal children collection mirroring UIElementCollection's IndexOf/Insert surface.</summary>
    public class FakeChildren
    {
        private readonly List<object> _items = new List<object>();
        public int Count => _items.Count;
        public object this[int index] => _items[index];
        public int IndexOf(object item) => _items.IndexOf(item);
        public void Insert(int index, object item) => _items.Insert(index, item);
    }

    /// <summary>Stand-in for the StackPanel that hosts the About page rows.</summary>
    public class FakePanel
    {
        public FakeChildren Children { get; } = new FakeChildren();
    }

    /// <summary>Row stub for the sidebar entries.</summary>
    public class StubItem
    {
        public StubVisibility Visibility { get; set; } = StubVisibility.Collapsed;
        public bool Checked { get; set; } = true;
    }

    /// <summary>Stand-in for PCL.PageDownloadLeft (members are VB.NET properties on the real class).</summary>
    public class PageDownloadLeft
    {
        public StubItem ItemInstall { get; } = new StubItem { Visibility = StubVisibility.Collapsed };
        public StubItem ItemMod { get; } = new StubItem { Checked = true };
        public FormMain.PageSubType PageID = FormMain.PageSubType.Mod;
    }

    /// <summary>Stand-in for PCL.PageOtherAbout with the properties the branding insert reads.</summary>
    public class PageOtherAbout
    {
        public FakePanel PanMain { get; } = new FakePanel();
        public MyListItem ItemAboutPcl { get; } = new MyListItem();

        public PageOtherAbout()
        {
            PanMain.Children.Insert(0, ItemAboutPcl);
        }
    }

    /// <summary>Stand-in for PCL.PageOtherLeft (the feedback entry).</summary>
    public static class PageOtherLeft
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TryFeedback()
        {
            var pad = "pcl2swap.harness.feedback";
            PclDlSwap.ExLoaderHarness.Probes.FeedbackCalls += pad.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>
    /// Stand-in for PCL.ModMain carrying the real 11-parameter MyMsgBox shape so the announcement
    /// guard and the PCL-native dialog bridge can be exercised end to end.
    /// </summary>
    public static class ModMain
    {
        // Lazy page references the pollers watch (the real launcher news these up on first visit).
        public static PageDownloadLeft FrmDownloadLeft;
        public static PageOtherAbout FrmOtherAbout;

        public static int MsgBoxCalls;
        public static int NextResult;
        public static string LastCaption;
        public static string LastButton2;
        public static bool LastForceWait;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int MyMsgBox(string caption, string title = "提示", string button1 = "确定",
            string button2 = "", string button3 = "", bool isWarn = false, bool highLight = true,
            bool forceWait = false, Action button1Action = null, Action button2Action = null,
            Action button3Action = null)
        {
            LastCaption = caption;
            LastButton2 = button2;
            LastForceWait = forceWait;
            var pad = "harness.msgbox." + (caption ?? string.Empty).Length;
            MsgBoxCalls += pad.Length > 0 ? 1 : 0;
            return NextResult;
        }
    }
}

