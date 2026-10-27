using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    /// <summary>Native checker used by the ABI test: takes a callable address, returns a bitmask of clobbered registers.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate long RegisterCheckFn(IntPtr target);

    /// <summary>The hook used while the ABI checker calls a detoured method.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int RegHook();

    /// <summary>Allocation-heavy hook used by the managed-caller experiment.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GcHook();

    /// <summary>Parameterised variant of <see cref="GcHook"/> (the one the experiment uses).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GcHook2(string tag);

    /// <summary>Throwaway targets for the detour-engine self test.</summary>
    public class CopyProbeInstance
    {
        public int Value = 5;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Add(int delta)
        {
            var pad = "copytest.instance." + delta;
            Value += pad.Length > 0 ? delta : 0;
            return Value;
        }
    }

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

        public static int RegTargetCalls;
        public static int RegHookCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int RegTarget()
        {
            var pad = "engine.regtarget";
            RegTargetCalls += pad.Length > 0 ? 1 : 0;
            return RegTargetCalls;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int RegHookImpl()
        {
            var pad = "engine.reghook";
            RegHookCalls += pad.Length > 0 ? 1 : 0;
            return 4242;
        }

        public static int GcTargetCalls;
        public static int GcHookCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GcTarget()
        {
            GcTargetCalls++;
            return GcTargetCalls;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GcHookImpl()
        {
            GcHookCalls++;
            for (int i = 0; i < 4; i++)
            {
                var waste = new byte[64 * 1024];
                GC.KeepAlive(waste);
            }
            if ((GcHookCalls & 15) == 0)
                GC.Collect(2, GCCollectionMode.Forced, true);
            return 777;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ManagedCallGcTarget()
        {
            return GcTarget();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GcTarget2(string tag)
        {
            var pad = "engine.gctarget2." + (tag ?? string.Empty);
            GcTargetCalls += pad.Length > 0 ? 1 : 0;
            return GcTargetCalls;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GcHookImpl2(string tag)
        {
            GcHookCalls++;
            for (int i = 0; i < 4; i++)
            {
                var waste = new byte[64 * 1024];
                GC.KeepAlive(waste);
            }
            if ((GcHookCalls & 15) == 0)
                GC.Collect(2, GCCollectionMode.Forced, true);
            return 777;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ManagedCallGcTarget2()
        {
            return GcTarget2("x");
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
            IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length >= 2 && args[0] == "--copytest") return RunCopyTest(args[1]);
            if (args.Length >= 3 && args[0] == "--abi2") return RunManagedCallerTest(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--hooktest") return RunHookTest(args[1]);
            if (args.Length >= 2 && args[0] == "--uitest") return RunUiThreadTest(args[1]);
            if (args.Length >= 3 && args[0] == "--manifest") return RunManifestTest(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--scantest") return RunScanPolicyTest(args[1]);
            if (args.Length >= 2 && args[0] == "--loaderhooktest") return RunLoaderHookTest(args[1]);
            if (args.Length >= 2 && args[0] == "--addontest") return RunAddonPlatformTest(args[1]);
            if (args.Length >= 2 && args[0] == "--surfacetest") return RunSurfaceTest(args[1]);
            if (args.Length >= 2 && args[0] == "--pcltest") return RunPclAccessTest(args[1]);
            if (args.Length >= 2 && args[0] == "--pagetest") return RunPageRegistryTest(args[1]);

            var reversedOta = args.Contains("--reversed-ota");
            var positional = args.Where(a => !a.StartsWith("--")).ToArray();

            if (positional.Length < 1)
            {
                Console.Error.WriteLine("usage: PclDlSwap.ExLoaderHarness <path-to-Pcl2Swap.Shim.x64.dll> [--reversed-ota]");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --copytest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --abi2 <payload.dll> <managed|stub>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --hooktest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --uitest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --manifest <payload.dll> <host.exe>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --scantest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --loaderhooktest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --addontest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --surfacetest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --pcltest <payload.dll>");
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --pagetest <payload.dll>");
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

            // Simulate the field case "download page is already open when the payload arrives":
            // seed FrmDownloadInstall before injection so the redrive watcher has to repair it.
            var preseeded = new PCL.PageDownloadInstall();
            PCL.ModMain.FrmDownloadInstall = preseeded;
            Console.WriteLine("[harness] preseeded FrmDownloadInstall (pre-injection)");

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

            // -- LoaderInit: the redrive watcher must re-run it on the pre-existing page, and the
            //    hook must feed the real loader with the live page controls. Counters are NOT reset
            //    here: the watcher may already have completed during the earlier steps.
            Console.WriteLine("[step] LoaderInit re-drive (via redrive watcher) ...");
            var loaderDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < loaderDeadline && PCL.MyPageRight.LoaderInitCalls == 0)
                Thread.Sleep(200);
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
                    PCL.ModMain.FrmDownloadInstall != null &&
                    about.PanMain.Children.Count == 2)
                    break;
                Thread.Sleep(100);
            }
            checks.Add(("sidebar poller restores the collapsed entry",
                sidebar.ItemInstall.Visibility == PCL.StubVisibility.Visible &&
                sidebar.PageID == PCL.FormMain.PageSubType.Default &&
                sidebar.ItemMod.Checked == false &&
                PCL.ModMain.FrmDownloadInstall != null,
                "visibility=" + sidebar.ItemInstall.Visibility + " pageId=" + sidebar.PageID +
                " modChecked=" + sidebar.ItemMod.Checked +
                " installPage=" + (PCL.ModMain.FrmDownloadInstall != null)));

            // The field crash (22:23:35 / 22:25:28): ItemInstall_Click runs
            // FrmDownloadInstall.ExitSelectPage() as soon as the entry is checked. The old repair
            // wrote the PageID field directly, skipped PageGet and left FrmDownloadInstall null, so
            // that click died with a NullReferenceException. The restored entry must be click-safe.
            var clickCallsBefore = PCL.PageDownloadInstall.ExitSelectPageCalls;
            var clickSafe = true;
            try
            {
                if (sidebar.ItemInstall.Checked) PCL.ModMain.FrmDownloadInstall.ExitSelectPage();
            }
            catch (NullReferenceException)
            {
                clickSafe = false;
            }
            checks.Add(("clicking the restored entry cannot hit the field NullReferenceException",
                clickSafe &&
                PCL.ModMain.FrmDownloadInstall != null &&
                PCL.PageDownloadInstall.ExitSelectPageCalls == clickCallsBefore + 1,
                "exitSelectPageCalls=" + PCL.PageDownloadInstall.ExitSelectPageCalls));
            checks.Add(("About poller inserts the brand row",
                about.PanMain.Children.Count == 2 &&
                about.PanMain.Children[1] is PCL.MyListItem &&
                ((PCL.MyListItem)about.PanMain.Children[1]).Title == "PCL2 Swap By BlockConnect",
                "children=" + about.PanMain.Children.Count));

            // keep the instances alive past the checks (the JIT must not see them as dead stores)
            GC.KeepAlive(sidebar);
            GC.KeepAlive(about);
            GC.KeepAlive(preseeded);

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
        /// End-to-end v2 verification: DetourInstaller installs a managed-body hook, a JIT caller then calls
        /// the hooked method 2000 times under GC pressure, and the original is reached through the IL copy.
        /// </summary>
        private static int RunHookTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var asm = Assembly.LoadFrom(full);
            var detourType = asm.GetType("PclDlSwap.Payload.Hook.DetourInstaller", throwOnError: true);
            var installMethod = detourType.GetMethod("Install",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var invokeOriginal = detourType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .First(m => m.Name == "InvokeOriginal" && !m.IsGenericMethod);

            var target = typeof(EngineProbe).GetMethod(nameof(EngineProbe.GcTarget2));
            var hook = typeof(EngineProbe).GetMethod(nameof(EngineProbe.GcHookImpl2));
            var detour = installMethod.Invoke(null, new object[] { target, hook });
            Console.WriteLine("[hooktest] detour installed");
            Console.Out.Flush();

            const int iterations = 2000;
            EngineProbe.GcTargetCalls = 0;
            EngineProbe.GcHookCalls = 0;
            for (int i = 0; i < iterations; i++)
            {
                var result = EngineProbe.ManagedCallGcTarget2();
                if ((i % 500) == 0)
                {
                    Console.WriteLine("[hooktest] iter " + i + " result=" + result +
                                      " hookCalls=" + EngineProbe.GcHookCalls);
                    Console.Out.Flush();
                }
            }

            var hookedOk = EngineProbe.GcHookCalls == iterations && EngineProbe.GcTargetCalls == 0;

            EngineProbe.GcTargetCalls = 0;
            var originalResult = invokeOriginal.Invoke(detour, new object[] { null, new object[] { "orig" } });
            Console.WriteLine("[hooktest] original IL copy returned " + originalResult +
                              " targetCalls=" + EngineProbe.GcTargetCalls);
            Console.Out.Flush();
            var originalOk = EngineProbe.GcTargetCalls == 1;

            Console.WriteLine("[hooktest] hooked=" + hookedOk + " original=" + originalOk);
            Console.WriteLine(hookedOk && originalOk ? "[hooktest] RESULT: PASS" : "[hooktest] RESULT: FAIL");
            return hookedOk && originalOk ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the manifest pipeline: the payload must resolve a real PCL binary to
        /// its manifest target by MVID. The Alpha 4 field logs reported "宿主不在偏移清单中" for both
        /// 2.99.0 and 2.99.0.1 even though the embedded manifest carries their MVIDs, so this test
        /// pins the parse + resolve path against real binaries instead of the harness stand-in.
        /// </summary>
        private static int RunManifestTest(string payloadPath, string hostPath)
        {
            var payloadFull = Path.GetFullPath(payloadPath);
            if (!File.Exists(payloadFull)) { Console.Error.WriteLine("payload not found: " + payloadFull); return 2; }
            var hostFull = Path.GetFullPath(hostPath);
            if (!File.Exists(hostFull)) { Console.Error.WriteLine("host not found: " + hostFull); return 2; }

            var failures = 0;
            var payload = Assembly.LoadFrom(payloadFull);
            var manifestType = payload.GetType("PclDlSwap.Payload.Host.OffsetManifest", throwOnError: true);
            var resolve = manifestType.GetMethod("ResolveFor", BindingFlags.Public | BindingFlags.Static);

            var all = ((IEnumerable)manifestType.GetProperty("All", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null, null))
                .Cast<object>().ToList();
            Console.WriteLine("[manifest] parsed targets: " + all.Count);
            foreach (var target in all)
            {
                var t = target.GetType();
                Console.WriteLine("  - " + t.GetProperty("Id").GetValue(target) +
                                  " @" + t.GetProperty("Mvid").GetValue(target));
            }
            if (all.Count == 0) failures++;
            Console.Out.Flush();

            var host = Assembly.LoadFrom(hostFull);
            var hostMvid = host.ManifestModule.ModuleVersionId.ToString("D").ToLowerInvariant();
            Console.WriteLine("[manifest] host " + Path.GetFileName(hostPath) + " mvid=" + hostMvid);

            var match = resolve.Invoke(null, new object[] { host });
            if (match == null)
            {
                Console.Error.WriteLine("[manifest] ResolveFor returned null for " + hostPath);
                failures++;
            }
            else
            {
                var mt = match.GetType();
                var id = mt.GetProperty("Id").GetValue(match);
                var mvid = Convert.ToString(mt.GetProperty("Mvid").GetValue(match));
                Console.WriteLine("[manifest] resolved: " + id + " @" + mvid);
                if (!string.Equals(mvid, hostMvid, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("[manifest] resolved MVID does not match the host");
                    failures++;
                }
            }

            Console.WriteLine(failures == 0 ? "[manifest] RESULT: PASS" : "[manifest] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the settings-page registry. Covers both detours:
        ///   - PageCheck keeps routing PCL's own ids to the original body,
        ///   - PageCheck takes over our ids (100/101) instead of letting the host throw,
        ///   - PageGet (FormMain.PageChangeActual calls it with the leftover PageID when the user
        ///     re-enters 设置) returns the cached swap page instead of "未知的设置子页面种类",
        ///   - real ids still fall through to the host body.
        /// The stub declares the same FormMain.PageSubType parameter shape as PCL, so the
        /// enum/int hook ABI is exercised by a genuinely JIT-compiled method.
        /// </summary>
        private static int RunPageRegistryTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[page] ok   " : "[page] FAIL ") + label);
                if (!ok) failures++;
            }

            var payload = Assembly.LoadFrom(full);
            var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
            hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

            var left = new PCL.PageSetupLeft();

            // PCL's own entry: the hook must hand the call to the original body (which switches).
            left.PageID = PCL.FormMain.PageSubType.Mod;
            left.PageCheck(new PCL.MyListItem { Tag = "0" }, EventArgs.Empty);
            Expect("PageCheck falls through for PCL's own ids",
                PCL.PageSetupLeft.PageChangeRunCalls == 1 && left.PageID == PCL.FormMain.PageSubType.Default);

            // Our sidebar ids: the host would throw "未知的设置子页面种类"; the swap must take over.
            var before = PCL.PageSetupLeft.PageChangeRunCalls;
            left.PageCheck(new PCL.MyListItem { Tag = "100" }, EventArgs.Empty);
            Expect("PageCheck intercepts id 100 and drives the host switch",
                PCL.PageSetupLeft.PageChangeRunCalls == before + 1);
            Expect("the swap id is written back to PageID", Convert.ToInt32(left.PageID) == 100);
            var page100 = left.PageGet((PCL.FormMain.PageSubType)(-1));
            Expect("PageGet(-1) resolves the swap page from PageID", page100 is PCL.MyPageRight);

            left.PageCheck(new PCL.MyListItem { Tag = "101" }, EventArgs.Empty);
            Expect("the second swap page is registered the same way", Convert.ToInt32(left.PageID) == 101);
            var page101 = left.PageGet((PCL.FormMain.PageSubType)101);
            Expect("PageGet(101) returns the cached page instead of throwing", page101 is PCL.MyPageRight);
            Expect("each swap id keeps its own page instance", !ReferenceEquals(page100, page101));
            Expect("PageGet(-1) follows the current PageID (101)",
                ReferenceEquals(left.PageGet((PCL.FormMain.PageSubType)(-1)), page101));

            // Real ids keep the host behaviour.
            var hostPage = left.PageGet(PCL.FormMain.PageSubType.Default);
            Expect("PageGet(Default) still reaches the host body",
                hostPage is PCL.MyPageRight && !ReferenceEquals(hostPage, page101));

            Console.WriteLine(failures == 0 ? "[page] RESULT: PASS" : "[page] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the typed PCL access families: Secret.UpdateStart and
        /// Net.DownloadByClient must reach the real host methods (the harness stubs count calls).
        /// </summary>
        private static int RunPclAccessTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[pcl] ok   " : "[pcl] FAIL ") + label);
                if (!ok) failures++;
            }

            var payload = Assembly.LoadFrom(full);
            var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
            hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

            var api = hostType.GetProperty("Api", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var pcl = api?.GetType().GetProperty("Pcl")?.GetValue(api);
            Expect("typed PCL access exposed on ISwapApi", pcl != null);

            var secret = pcl?.GetType().GetProperty("Secret")?.GetValue(pcl);
            Expect("Secret family exposed", secret != null);
            var before = Probes.UpdateStartCalls;
            secret?.GetType().GetMethod("UpdateStart")?.Invoke(secret, new object[] { false, null });
            var after = Probes.UpdateStartCalls;
            Console.WriteLine("[pcl] Secret.UpdateStart calls: " + before + " -> " + after);
            Expect("Secret.UpdateStart reached the host method", after == before + 1);

            var net = pcl?.GetType().GetProperty("Net")?.GetValue(pcl);
            Expect("Net family exposed", net != null);
            var clientBefore = Probes.DownloadByClientCalls;
            net?.GetType().GetMethod("DownloadByClient")?.Invoke(net,
                new object[] { "https://example.com/a.bin", @"C:\Temp\a.bin", false });
            var clientAfter = Probes.DownloadByClientCalls;
            Console.WriteLine("[pcl] Net.DownloadByClient calls: " + clientBefore + " -> " + clientAfter);
            Expect("Net.DownloadByClient reached the host method", clientAfter == clientBefore + 1);

            var animation = pcl?.GetType().GetProperty("Animation")?.GetValue(pcl);
            Expect("Animation family exposed", animation != null);

            Console.WriteLine(failures == 0 ? "[pcl] RESULT: PASS" : "[pcl] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the surface index: the payload must expose the embedded PclSurface
        /// dump through ISwapApi.Surface and resolve known members with their metadata tokens.
        /// </summary>
        private static int RunSurfaceTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[surface] ok   " : "[surface] FAIL ") + label);
                if (!ok) failures++;
            }

            var payload = Assembly.LoadFrom(full);
            var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
            hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

            var api = hostType.GetProperty("Api", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var surface = api?.GetType().GetProperty("Surface")?.GetValue(api);
            Expect("surface exposed on ISwapApi", surface != null);

            var findMethod = surface?.GetType().GetMethod("FindMethod");
            var updateStart = findMethod?.Invoke(surface, new object[] { "PCL.ModSecret", "UpdateStart", 2 });
            Expect("PCL.ModSecret.UpdateStart(bool, IEnumerable<string>) found", updateStart != null);
            var token = Convert.ToString(updateStart?.GetType().GetProperty("Token")?.GetValue(updateStart));
            Console.WriteLine("[surface] UpdateStart token=" + token);
            Expect("token is 0x0600041F", token == "0x0600041F");

            var typeCount = Convert.ToInt32(surface?.GetType().GetProperty("TypeCount")?.GetValue(surface) ?? 0);
            Console.WriteLine("[surface] type count=" + typeCount);
            Expect("index carries at least 200 types", typeCount > 200);

            var search = surface?.GetType().GetMethod("Search")
                ?.Invoke(surface, new object[] { "DlClientList", 20 }) as System.Collections.IEnumerable;
            var hits = search?.Cast<object>().Count() ?? 0;
            Console.WriteLine("[surface] search(DlClientList) hits=" + hits);
            Expect("search finds the download loader family", hits > 0);

            Console.WriteLine(failures == 0 ? "[surface] RESULT: PASS" : "[surface] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// End-to-end test for the Swap API platform: packs the sample addon into a real
        /// .swapaddon archive (manifest + net48 dll), starts SwapHost against the harness
        /// stand-in host and asserts that the three built-ins plus the packed addon load,
        /// validate and enable - i.e. the addon package format works end to end.
        /// </summary>
        private static int RunAddonPlatformTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[addon] ok   " : "[addon] FAIL ") + label);
                if (!ok) failures++;
            }

            var addonRoot = Path.Combine(Path.GetTempPath(), "PCL2Swap", "addons");
            Directory.CreateDirectory(addonRoot);
            var archive = Path.Combine(addonRoot, "test.sample.swapaddon");
            var unpacked = Path.Combine(addonRoot, "test.sample");
            var payload = Assembly.LoadFrom(full);

            try
            {
                if (File.Exists(archive)) File.Delete(archive);
                using (var zip = new ZipArchive(File.Create(archive), ZipArchiveMode.Create))
                {
                    var manifest = zip.CreateEntry("manifest.json");
                    using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write("{\"schema\":1,\"id\":\"test.sample\",\"name\":\"Sample Addon\"," +
                                     "\"version\":\"1.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                                     "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"," +
                                     "\"description\":\"harness sample\"}");
                    }
                    var dllEntry = zip.CreateEntry("PCL2Swap.SampleAddon.dll");
                    using (var target = dllEntry.Open())
                    using (var source = File.OpenRead(typeof(PCL2Swap.SampleAddon.SampleAddon).Assembly.Location))
                        source.CopyTo(target);
                }
                Console.WriteLine("[addon] packed " + archive);

                var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
                var start = hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                start.Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

                var manager = hostType.GetProperty("Addons", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                var records = (manager?.GetType().GetProperty("Records")?.GetValue(manager) as System.Collections.IEnumerable)
                    ?.Cast<object>().ToList() ?? new List<object>();
                Console.WriteLine("[addon] records: " + records.Count);
                foreach (var record in records)
                {
                    var recordType = record.GetType();
                    Console.WriteLine("  - " + recordType.GetProperty("Id").GetValue(record) +
                                      " status=" + recordType.GetProperty("Status").GetValue(record));
                }

                Expect("host started with three built-ins plus the packed addon", records.Count == 4);
                var sample = records.FirstOrDefault(record => Convert.ToString(
                    record.GetType().GetProperty("Id").GetValue(record)) == "test.sample");
                Expect("sample addon discovered", sample != null);
                Expect("sample addon enabled",
                    sample != null && Convert.ToString(sample.GetType().GetProperty("Status").GetValue(sample)) == "enabled");

                // The loader unpacks the dll and loads it from its own directory, so the counters
                // must be read from that instance, not from the copy this process references.
                var instance = sample?.GetType().GetProperty("Instance")?.GetValue(sample);
                var instanceType = instance?.GetType();
                int StaticInt(string name) => instanceType == null
                    ? -1
                    : Convert.ToInt32(instanceType.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? -1);
                string StaticString(string name) => instanceType == null
                    ? null
                    : Convert.ToString(instanceType.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null));

                Expect("sample addon OnLoad ran", StaticInt("LoadCalls") == 1);
                Expect("sample addon OnEnable ran", StaticInt("EnableCalls") == 1);
                Expect("sample addon saw the API version", StaticString("LastApiVersion") == "1.0.0");
            }
            finally
            {
                // Leave the real addon directory exactly as found.
                try { if (File.Exists(archive)) File.Delete(archive); } catch { }
                try { if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "[addon] RESULT: PASS" : "[addon] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the loader-shape download hook: after the IL rewrite, DownloadHook must
        /// rebuild PCL's own NetFile/LoaderDownload pipeline (multi-source, progress sync, checker)
        /// through reflection - the old version replaced it with a single-threaded WebClient and
        /// dropped the checker. The stub above mirrors the real ctor/method signatures measured from
        /// PCL 2.99.0's IL.
        /// </summary>
        private static int RunLoaderHookTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var payload = Assembly.LoadFrom(full);
            var hookType = payload.GetType("PclDlSwap.Payload.Host.HookEntryPoints", throwOnError: true);
            var configType = payload.GetType("PclDlSwap.Payload.Sandbox.SwapConfig", throwOnError: true);
            var config = Activator.CreateInstance(configType);
            var configProperty = hookType.GetProperty("Config", BindingFlags.Public | BindingFlags.Static);
            configProperty?.GetSetMethod(nonPublic: true)?.Invoke(null, new object[] { config });

            PCL.ModNet.LoaderDownload.WaitForExitCalls = 0;
            PCL.ModNet.LoaderDownload.CancelCalls = 0;
            PCL.ModNet.LoaderDownload.LastInput = null;
            PCL.ModNet.LoaderDownload.LastLoaderToSync = null;
            PCL.ModNet.LoaderDownload.LastForceRestart = true;
            PCL.ModNet.LoaderDownload.LastProvider = null;

            var downloadHook = hookType.GetMethod("DownloadHook", BindingFlags.Public | BindingFlags.Static);
            var loaderSync = new object();
            var urls = new[] { "https://pcl2-server.example/file.json" };
            var localPath = Path.Combine(Path.GetTempPath(), "pcl2swap-loaderhooktest.json");
            if (File.Exists(localPath)) File.Delete(localPath);

            downloadHook.Invoke(null, new object[] { urls, localPath, loaderSync, null, false });

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[loaderhook] ok   " : "[loaderhook] FAIL ") + label);
                if (!ok) failures++;
            }

            Expect("WaitForExit called once", PCL.ModNet.LoaderDownload.WaitForExitCalls == 1);
            Expect("Cancel called once", PCL.ModNet.LoaderDownload.CancelCalls == 1);
            Expect("loader passed as LoaderToSyncProgress",
                ReferenceEquals(PCL.ModNet.LoaderDownload.LastLoaderToSync, loaderSync));
            Expect("IsForceRestart false", PCL.ModNet.LoaderDownload.LastForceRestart == false);
            Expect("Input null", PCL.ModNet.LoaderDownload.LastInput == null);

            Console.WriteLine(failures == 0
                ? "[loaderhook] RESULT: PASS"
                : "[loaderhook] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the scan policy: only files that can execute or carry code are worth a
        /// scan, and Owner's policy excludes everything landing inside the Minecraft directory (game
        /// files, mods, resource packs, shaders) while keeping launcher files fetched from PCL's
        /// servers. Both rules together are what removed the field report's performance loss.
        /// </summary>
        private static int RunScanPolicyTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var payload = Assembly.LoadFrom(full);
            var scannerType = payload.GetType("PclDlSwap.Payload.Sandbox.ScannerSet", throwOnError: true);
            var shouldScan = scannerType.GetMethod("ShouldScan", BindingFlags.Public | BindingFlags.Static);
            var coreType = payload.GetType("PclDlSwap.Payload.Host.CoreInstaller", throwOnError: true);
            var isGamePath = coreType.GetMethod("IsGameContentPath", BindingFlags.NonPublic | BindingFlags.Static);
            if (shouldScan == null || isGamePath == null)
            {
                Console.Error.WriteLine("[scan] payload is missing ShouldScan/IsGameContentPath");
                return 1;
            }

            var failures = 0;
            void Expect(string label, Func<object> call, object expected)
            {
                var actual = call();
                var ok = Equals(actual, expected);
                Console.WriteLine((ok ? "[scan] ok   " : "[scan] FAIL ") + label + " => " + actual +
                                  " (expect " + expected + ")");
                if (!ok) failures++;
            }

            Expect("ShouldScan(PCL update .exe)", () => shouldScan.Invoke(null, new object[] { @"C:\Temp\PCL\update.exe" }), true);
            Expect("ShouldScan(version index .json)", () => shouldScan.Invoke(null, new object[] { @"C:\Temp\PCL\index.json" }), false);
            Expect("ShouldScan(mod .jar)", () => shouldScan.Invoke(null, new object[] { @"C:\MC\mods\mod.jar" }), true);
            Expect("IsGameContentPath(.minecraft\\mods)", () => isGamePath.Invoke(null, new object[] { @"C:\Users\x\.minecraft\mods\a.jar" }), true);
            Expect("IsGameContentPath(shaderpacks)", () => isGamePath.Invoke(null, new object[] { @"D:\MC\shaderpacks\a.zip" }), true);
            Expect("IsGameContentPath(resourcepacks)", () => isGamePath.Invoke(null, new object[] { @"D:\MC\resourcepacks\a.zip" }), true);
            Expect("IsGameContentPath(versions)", () => isGamePath.Invoke(null, new object[] { @"D:\MC\versions\1.21\client.jar" }), true);
            Expect("IsGameContentPath(PCL update temp)", () => isGamePath.Invoke(null, new object[] { @"C:\Users\x\AppData\Local\Temp\PCL\update.exe" }), false);
            Expect("IsGameContentPath(mymods trap)", () => isGamePath.Invoke(null, new object[] { @"C:\Tools\mymods\a.exe" }), false);

            Console.WriteLine(failures == 0 ? "[scan] RESULT: PASS" : "[scan] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Regression test for the WPF marshalling fix: PclUi.OnUiThread used to look for a
        /// single-parameter Invoke(Delegate) overload that WPF does not have, silently ran the body
        /// on the polling thread and produced "调用线程无法访问此对象" on the About page and the
        /// 2.99.0.1 sidebar. This spins a real WPF Dispatcher on an STA thread and asserts that a
        /// cross-thread call lands there (and that an on-thread call runs inline).
        /// </summary>
        private static int RunUiThreadTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var payload = Assembly.LoadFrom(full);
            var pclUi = payload.GetType("PclDlSwap.Payload.Host.PclUi", throwOnError: true);
            var onUi = pclUi.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "OnUiThread" && m.IsGenericMethodDefinition);
            var closed = onUi.MakeGenericMethod(typeof(string));

            System.Windows.Threading.Dispatcher dispatcher = null;
            var uiThreadId = 0;
            var ready = new ManualResetEventSlim(false);
            var ui = new Thread(() =>
            {
                try
                {
                    var application = new System.Windows.Application();
                    dispatcher = application.Dispatcher;
                    uiThreadId = Thread.CurrentThread.ManagedThreadId;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[uitest] dispatcher setup failed: " + ex.Message);
                }
                finally
                {
                    ready.Set();
                }
                System.Windows.Threading.Dispatcher.Run();
            });
            ui.SetApartmentState(ApartmentState.STA);
            ui.IsBackground = true;
            ui.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(15)) || dispatcher == null)
            {
                Console.Error.WriteLine("[uitest] WPF dispatcher did not start");
                return 1;
            }

            var failures = 0;
            Console.WriteLine("[uitest] ui thread=" + uiThreadId + ", caller thread=" + Thread.CurrentThread.ManagedThreadId);

            // Diagnostics for the payload's own dispatcher lookup path (it runs in the payload's
            // LoadFrom context, so harness-side Type.GetType results would not prove anything).
            var tryGetDispatcher = pclUi.GetMethod("TryGetDispatcher", BindingFlags.NonPublic | BindingFlags.Static);
            var callerDispatcher = tryGetDispatcher.Invoke(null, null);
            Console.WriteLine("[uitest] caller: Application.Current=" +
                              (System.Windows.Application.Current == null ? "null" : "set") +
                              ", payload lookup=" + (callerDispatcher == null ? "null" : "found"));
            var uiThreadDispatcherFound = false;
            var uiProbeDone = new ManualResetEventSlim(false);
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { uiThreadDispatcherFound = tryGetDispatcher.Invoke(null, null) != null; }
                finally { uiProbeDone.Set(); }
            }));
            uiProbeDone.Wait(TimeSpan.FromSeconds(10));
            Console.WriteLine("[uitest] payload lookup on ui thread: " + (uiThreadDispatcherFound ? "found" : "null"));

            var observedThreadId = 0;
            var body = new Func<string>(() =>
            {
                observedThreadId = Thread.CurrentThread.ManagedThreadId;
                return "body:" + observedThreadId;
            });

            var result = (string)closed.Invoke(null, new object[] { body, "fallback" });
            Console.WriteLine("[uitest] cross-thread result=" + result + " observed=" + observedThreadId);
            if (observedThreadId != uiThreadId)
            {
                Console.Error.WriteLine("[uitest] body did not run on the dispatcher thread");
                failures++;
            }
            if (result != "body:" + uiThreadId)
            {
                Console.Error.WriteLine("[uitest] cross-thread call did not return the body's value");
                failures++;
            }

            // Already on the UI thread: the body must run inline instead of dead-locking on Invoke.
            var inlineResult = string.Empty;
            var inlineDone = new ManualResetEventSlim(false);
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { inlineResult = (string)closed.Invoke(null, new object[] { body, "fallback" }); }
                catch (Exception ex) { Console.Error.WriteLine("[uitest] inline call failed: " + ex.Message); }
                finally { inlineDone.Set(); }
            }));
            if (!inlineDone.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine("[uitest] inline call timed out");
                failures++;
            }
            else
            {
                Console.WriteLine("[uitest] inline result=" + inlineResult);
                if (inlineResult != "body:" + uiThreadId)
                {
                    Console.Error.WriteLine("[uitest] on-thread call did not run inline");
                    failures++;
                }
            }

            dispatcher.BeginInvoke(new Action(() => dispatcher.InvokeShutdown()));
            ui.Join(TimeSpan.FromSeconds(10));

            Console.WriteLine(failures == 0 ? "[uitest] RESULT: PASS" : "[uitest] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Exercises the payload's IL-copy factory: the emitted copies must be ordinary managed methods with
        /// the original behaviour (branching, calls, static-field side effects visible to the host).
        /// </summary>
        private static int RunCopyTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var asm = Assembly.LoadFrom(full);
            var factoryType = asm.GetType("PclDlSwap.Payload.Host.OriginalFactory", throwOnError: true);
            var create = factoryType.GetMethod("CreateCopy", BindingFlags.Public | BindingFlags.Static);
            var failures = 0;

            var calcCopy = (MethodInfo)create.Invoke(null,
                new object[] { typeof(EngineProbe).GetMethod(nameof(EngineProbe.Calc)) });
            var calcResult = (int)calcCopy.Invoke(null, new object[] { 3, 4 });
            Console.WriteLine("[copy] Calc copy(3,4) = " + calcResult + " (expect 7)");
            if (calcResult != 7) failures++;

            var managedCopy = (MethodInfo)create.Invoke(null,
                new object[] { typeof(EngineProbe).GetMethod(nameof(EngineProbe.Managed)) });
            var m1 = (int)managedCopy.Invoke(null, new object[] { null });
            var m2 = (int)managedCopy.Invoke(null, new object[] { new[] { "a", "b" } });
            Console.WriteLine("[copy] Managed copy(null)=" + m1 + " (expect -1); copy(list)=" + m2 + " (expect 2)");
            if (m1 != -1 || m2 != 2) failures++;

            EngineProbe.RawProbeCalls = 0;
            EngineProbe.RawProbeText = null;
            var rawCopy = (MethodInfo)create.Invoke(null,
                new object[] { typeof(EngineProbe).GetMethod(nameof(EngineProbe.RawProbe)) });
            rawCopy.Invoke(null, new object[] { "copytest", null });
            Console.WriteLine("[copy] RawProbe copy -> Text=" + EngineProbe.RawProbeText +
                              " calls=" + EngineProbe.RawProbeCalls);
            if (EngineProbe.RawProbeText != "raw:copytest:null" || EngineProbe.RawProbeCalls != 1) failures++;

            var instance = new CopyProbeInstance();
            var addCopy = (MethodInfo)create.Invoke(null,
                new object[] { typeof(CopyProbeInstance).GetMethod(nameof(CopyProbeInstance.Add)) });
            object instanceResult;
            if (addCopy.IsStatic)
                instanceResult = addCopy.Invoke(null, new object[] { instance, 3 });
            else
                instanceResult = addCopy.Invoke(instance, new object[] { 3 });
            Console.WriteLine("[copy] instance copy static=" + addCopy.IsStatic + " Add(3) -> ret=" +
                              instanceResult + " value=" + instance.Value + " (expect 8/8)");
            if (!Equals(instanceResult, 8) || instance.Value != 8) failures++;

            Console.WriteLine(failures == 0 ? "[copy] RESULT: PASS" : "[copy] RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Reproduces the real failure shape: a JIT-compiled (managed) caller invokes a detoured method whose hook
        /// performs allocations and GC. Mode "managed" points the detour at a managed method body (the intended
        /// fix); mode "stub" points it at a Marshal.GetFunctionPointerForDelegate thunk (what the launcher
        /// currently uses: a thunk that assumes it is entered from unmanaged code).
        /// </summary>
        private static int RunManagedCallerTest(string payloadPath, string mode)
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
            Console.WriteLine("[abi2] payload types resolved");
            Console.Out.Flush();

            var capstonePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".nuget\packages\gee.external.capstone\2.3.0\runtimes\win-x64\native\capstone.dll");
            if (File.Exists(capstonePath)) LoadLibraryW(capstonePath);
            Console.WriteLine("[abi2] capstone preload attempted (exists=" + File.Exists(capstonePath) + ")");
            Console.Out.Flush();

            var rejectMode = string.Equals(mode, "reject", StringComparison.OrdinalIgnoreCase);
            var targetName = rejectMode ? nameof(EngineProbe.GcTarget) : nameof(EngineProbe.GcTarget2);
            var hookName = rejectMode ? nameof(EngineProbe.GcHookImpl) : nameof(EngineProbe.GcHookImpl2);

            var targetMethod = typeof(EngineProbe).GetMethod(targetName);
            RuntimeHelpers.PrepareMethod(targetMethod.MethodHandle);
            var targetPtr = targetMethod.MethodHandle.GetFunctionPointer();

            Delegate hookDelegate = null;
            IntPtr hookPtr;
            if (rejectMode || string.Equals(mode, "stub", StringComparison.OrdinalIgnoreCase))
            {
                hookDelegate = rejectMode
                    ? (Delegate)new GcHook(EngineProbe.GcHookImpl)
                    : new GcHook2(EngineProbe.GcHookImpl2);
                hookPtr = Marshal.GetFunctionPointerForDelegate(hookDelegate);
            }
            else if (string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase))
            {
                var hookMethod = typeof(EngineProbe).GetMethod(hookName);
                RuntimeHelpers.PrepareMethod(hookMethod.MethodHandle);
                hookPtr = hookMethod.MethodHandle.GetFunctionPointer();
            }
            else
            {
                Console.Error.WriteLine("mode must be stub|managed|reject");
                return 2;
            }

            Console.WriteLine("[abi2] targetPtr=0x" + targetPtr.ToInt64().ToString("X") +
                              " hookPtr=0x" + hookPtr.ToInt64().ToString("X") + " mode=" + mode);
            Console.Out.Flush();

            var probe = new byte[64];
            IntPtr readBytes2;
            var rpmOk = ReadProcessMemory(GetCurrentProcess(), targetPtr, probe, (IntPtr)probe.Length, out readBytes2);
            Console.WriteLine("[abi2] target rpm64 ok=" + rpmOk + " read=" + readBytes2.ToInt64() +
                              " bytes=" + BitConverter.ToString(probe).Replace("-", " "));
            Console.Out.Flush();

            object detour;
            try
            {
                detour = installMethod.Invoke(null, new object[] { targetPtr, hookPtr });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[abi2] install threw: " + ex.GetType().Name + " | inner: " +
                                  (ex.InnerException == null ? "(none)" : ex.InnerException.Message));
                Console.Out.Flush();
                if (rejectMode)
                {
                    Console.WriteLine("[abi2] RESULT: rejection path completed without a native crash");
                    return 0;
                }
                return 1;
            }
            Console.WriteLine("[abi2] mode=" + mode + " detour installed; hookPtr=0x" + hookPtr.ToInt64().ToString("X"));
            Console.Out.Flush();
            if (rejectMode)
            {
                Console.WriteLine("[abi2] RESULT: reject mode installed unexpectedly (engine should have refused)");
                return 1;
            }

            const int iterations = 2000;
            for (int i = 0; i < iterations; i++)
            {
                if (i < 5)
                {
                    Console.WriteLine("[abi2] before managed call " + i);
                    Console.Out.Flush();
                }
                var result = EngineProbe.ManagedCallGcTarget2();
                if ((i % 100) == 0 || i == iterations - 1)
                    Console.WriteLine("[abi2] iter " + i + " result=" + result +
                                      " targetCalls=" + EngineProbe.GcTargetCalls +
                                      " hookCalls=" + EngineProbe.GcHookCalls);
            }

            Console.WriteLine("[abi2] RESULT: survived " + iterations + " managed calls (hookCalls=" +
                              EngineProbe.GcHookCalls + ", targetCalls=" + EngineProbe.GcTargetCalls + ")");
            GC.KeepAlive(hookDelegate);
            return 0;
        }

        /// <summary>
        /// Direct exercise of the payload's x64 detour engine without the shim: proves the trampoline
        /// for a blittable signature and for a managed-reference signature (the shape every real hook
        /// in this project uses).
        /// </summary>
        /// </summary>
        private static byte[] BuildRegisterChecker()
        {
            const ulong s1 = 0x1111111111111111UL;
            const ulong s2 = 0x2222222222222222UL;
            const ulong s3 = 0x3333333333333333UL;
            const ulong s4 = 0x4444444444444444UL;
            const ulong s5 = 0x5555555555555555UL;
            const ulong s6 = 0x6666666666666666UL;
            const ulong s7 = 0x7777777777777777UL;

            var code = new List<byte>();
            code.AddRange(new byte[] { 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57 });
            code.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 });
            code.AddRange(MovImm64(0xBB, s1));
            code.AddRange(MovImm64(0xBE, s2));
            code.AddRange(MovImm64(0xBF, s3));
            code.AddRange(MovImm64(0xBC, s4, true));
            code.AddRange(MovImm64(0xBD, s5, true));
            code.AddRange(MovImm64(0xBE, s6, true));
            code.AddRange(MovImm64(0xBF, s7, true));
            code.AddRange(new byte[] { 0xFF, 0xD1 });
            code.AddRange(MovImm64(0xBA, s1)); code.AddRange(new byte[] { 0x48, 0x31, 0xD3, 0x48, 0x89, 0xD8 });
            code.AddRange(MovImm64(0xBA, s2)); code.AddRange(new byte[] { 0x48, 0x31, 0xD6, 0x48, 0x09, 0xF0 });
            code.AddRange(MovImm64(0xBA, s3)); code.AddRange(new byte[] { 0x48, 0x31, 0xD7, 0x48, 0x09, 0xF8 });
            code.AddRange(MovImm64(0xBA, s4)); code.AddRange(new byte[] { 0x49, 0x31, 0xD4, 0x4C, 0x09, 0xE0 });
            code.AddRange(MovImm64(0xBA, s5)); code.AddRange(new byte[] { 0x49, 0x31, 0xD5, 0x4C, 0x09, 0xE8 }); // xor r13,rdx
            code.AddRange(MovImm64(0xBA, s6)); code.AddRange(new byte[] { 0x49, 0x31, 0xD6, 0x4C, 0x09, 0xF0 }); // xor r14,rdx
            code.AddRange(MovImm64(0xBA, s7)); code.AddRange(new byte[] { 0x49, 0x31, 0xD7, 0x4C, 0x09, 0xF8 }); // xor r15,rdx
            code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x20 });
            code.AddRange(new byte[] { 0x41, 0x5F, 0x41, 0x5E, 0x41, 0x5D, 0x41, 0x5C, 0x5F, 0x5E, 0x5B });
            code.Add(0xC3);
            return code.ToArray();
        }

        private static byte[] MovImm64(byte opcode, ulong value, bool rexB = false)
        {
            var bytes = new List<byte> { (byte)(0x48 | (rexB ? 0x01 : 0x00)), opcode };
            bytes.AddRange(BitConverter.GetBytes(value));
            return bytes.ToArray();
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

        /// <summary>Stand-in for PCL.ModNet.NetFile (the loader shape rebuilds it via reflection).</summary>
        public sealed class NetFile
        {
            public readonly IEnumerable<string> Urls;
            public readonly string LocalPath;
            public readonly object Checker;
            public readonly bool SimulateBrowserHeaders;

            public NetFile(IEnumerable<string> urls, string localPath, object checker, bool simulateBrowserHeaders)
            {
                Urls = urls;
                LocalPath = localPath;
                Checker = checker;
                SimulateBrowserHeaders = simulateBrowserHeaders;
            }
        }

        /// <summary>Stand-in for PCL.ModNet.LoaderDownload (carries the WaitForExit/Cancel surface).</summary>
        public sealed class LoaderDownload
        {
            public static int WaitForExitCalls;
            public static int CancelCalls;
            public static object LastInput;
            public static object LastLoaderToSync;
            public static bool LastForceRestart = true;
            public static object LastProvider;

            public readonly string Name;
            public readonly List<NetFile> Tasks;

            public LoaderDownload(string name, IEnumerable<NetFile> fileTasks)
            {
                Name = name;
                Tasks = new List<NetFile>(fileTasks ?? Enumerable.Empty<NetFile>());
            }

            public void WaitForExit(object input, object loaderToSyncProgress, bool isForceRestart,
                CancellationToken cancellationToken, object progressProvider)
            {
                WaitForExitCalls++;
                LastInput = input;
                LastLoaderToSync = loaderToSyncProgress;
                LastForceRestart = isForceRestart;
                LastProvider = progressProvider;
            }

            public void Cancel()
            {
                CancelCalls++;
            }
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

        // The swap registry moves a page between the host's panels; keep the three members it
        // touches so the harness can exercise GetOrCreatePage for real.
        public object Child;
        public object PanContent;
        public object PanAlways;

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

        public static int ExitSelectPageCalls;

        /// <summary>The handler that crashed in the field (ItemInstall_Click calls this).</summary>
        public void ExitSelectPage()
        {
            var pad = "harness.exitselect.";
            ExitSelectPageCalls += pad.Length > 0 ? 1 : 0;
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
        /// <summary>PCL stores the settings sub-page id here; the swap registry reads it back.</summary>
        public object Tag { get; set; }
    }

    /// <summary>
    /// Stand-in for PCL.PageSetupLeft. The swap registry detours both PageCheck and PageGet, so the
    /// harness can reproduce the field bug where the leftover PageID 100/101 reached
    /// FormMain.PageChangeActual -> PageGet and blew up with "未知的设置子页面种类：101".
    /// PageChange/PageGet mirror PCL's own semantics: known ids switch, unknown ids throw.
    /// </summary>
    public class PageSetupLeft
    {
        public static int PageChangeRunCalls;
        public static object LastChangedPage;

        public FormMain.PageSubType PageID = FormMain.PageSubType.Default;
        public bool IsPageSwitched;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void PageCheck(MyListItem sender, EventArgs e)
        {
            if (sender.Tag != null)
                PageChange((FormMain.PageSubType)Convert.ToInt32(sender.Tag));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void PageChange(FormMain.PageSubType id)
        {
            if (PageID == id) return;
            switch (id)
            {
                case FormMain.PageSubType.Default:
                    PageChangeRun(new MyPageRight());
                    break;
                default:
                    throw new Exception("未知的设置子页面种类：" + Convert.ToInt32(id));
            }
            PageID = id;
        }

        // The registry replays the host's navigation through this private static method.
        private static void PageChangeRun(object page)
        {
            PageChangeRunCalls++;
            LastChangedPage = page;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public object PageGet(FormMain.PageSubType id = (FormMain.PageSubType)(-1))
        {
            if (id == (FormMain.PageSubType)(-1)) id = PageID;
            if (id == FormMain.PageSubType.Default) return new MyPageRight();
            throw new Exception("未知的设置子页面种类：" + Convert.ToInt32(id));
        }
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
        public StubItem ItemInstall { get; } = new StubItem { Visibility = StubVisibility.Collapsed, Checked = false };
        public StubItem ItemMod { get; } = new StubItem { Checked = true };
        public FormMain.PageSubType PageID = FormMain.PageSubType.Mod;
        public int PageChangeCalls;

        /// <summary>Mirrors PCL's PageChange: early-returns on the current id, otherwise resolves through PageGet.</summary>
        public void PageChange(FormMain.PageSubType id)
        {
            PageChangeCalls++;
            if (PageID == id) return;
            PageGet(id);
            PageID = id;
        }

        /// <summary>Mirrors PCL's PageGet: creates the sub-page object on first use.</summary>
        public object PageGet(FormMain.PageSubType id = (FormMain.PageSubType)(-1))
        {
            if (id == (FormMain.PageSubType)(-1)) id = PageID;
            if (id == FormMain.PageSubType.Default)
            {
                if (ModMain.FrmDownloadInstall == null) ModMain.FrmDownloadInstall = new PageDownloadInstall();
                return ModMain.FrmDownloadInstall;
            }
            return null;
        }
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
        public static PageDownloadInstall FrmDownloadInstall;

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

