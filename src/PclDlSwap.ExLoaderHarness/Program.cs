using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    /// <summary>Static handler the module-scoped hook test attaches to PCL.HookTarget.Ping.</summary>
    public static class HookHandlers
    {
        public static int SampleCalls;

        public static int Sample(int value)
        {
            SampleCalls++;
            return value + 100;
        }

        public static void Before(int value) => PCL.HookTarget.Order.Add("before:" + value);

        public static void After(int value) => PCL.HookTarget.Order.Add("after:" + value);
    }

    /// <summary>In-memory IData used by the AddonSettings round-trip assertion.</summary>
    public sealed class MemoryData : PCL2Swap.Api.IData
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        public bool Saved;

        public string Get(string key, string fallback = null) =>
            key != null && _values.TryGetValue(key, out var value) ? value : fallback;

        public void Set(string key, string value)
        {
            if (!string.IsNullOrEmpty(key)) _values[key] = value;
        }

        public void Save() => Saved = true;
    }

    /// <summary>Instance-delegate handler with a reference-typed parameter.</summary>
    public sealed class InstanceHookHandler
    {
        private readonly string _tag;
        public int Calls;

        public InstanceHookHandler(string tag) => _tag = tag;

        public string Echo(string value)
        {
            Calls++;
            return _tag + ":" + value;
        }
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
            if (args.Length >= 2 && args[0] == "--safemodetest") return RunSafeModeTest(args[1]);

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
                Console.Error.WriteLine("   or: PclDlSwap.ExLoaderHarness --safemodetest <payload.dll>");
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
        /// Exercises the recovery journal as a real startup sequence: three unclean starts must
        /// enter safe mode, every addon must be skipped for that process, and clearing the flag must
        /// make the next start normal again.
        /// </summary>
        private static int RunSafeModeTest(string payloadPath)
        {
            var full = Path.GetFullPath(payloadPath);
            if (!File.Exists(full)) { Console.Error.WriteLine("payload not found: " + full); return 2; }

            var failures = 0;
            void Expect(string label, bool ok)
            {
                Console.WriteLine((ok ? "[safe] ok   " : "[safe] FAIL ") + label);
                if (!ok) failures++;
            }

            var dataRoot = Path.Combine(Path.GetTempPath(),
                "Pcl2SwapSafeModeTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("PCL2SWAP_DATA", dataRoot);
            Directory.CreateDirectory(PCL2Swap.Api.SwapPaths.AddonsDirectory);

            var packageSource = Path.GetFullPath(Path.Combine(
                "examples", "demo-addon", "demo.sample.swapaddon"));
            if (!File.Exists(packageSource))
            {
                Console.Error.WriteLine("[safe] package not found: " + packageSource);
                return 2;
            }
            File.Copy(packageSource,
                Path.Combine(PCL2Swap.Api.SwapPaths.AddonsDirectory, "demo.sample.swapaddon"),
                true);

            var payload = Assembly.LoadFrom(full);
            var recoveryType = payload.GetType("PclDlSwap.Payload.Swap.StartupRecovery",
                throwOnError: true);
            var begin = recoveryType.GetMethod("Begin", BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.Static);
            Expect("StartupRecovery.Begin exposed", begin != null);

            var moduleIds = new[]
            {
                "swap.download-mc", "swap.ota-protector", "swap.cancel-button", "demo.sample"
            };
            object recovery = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                recovery = begin?.Invoke(null, new object[]
                {
                    PCL2Swap.Api.SwapShape.Injected, "2.99.0.1", moduleIds
                });
                Expect("startup " + attempt + " recorded",
                    recovery != null &&
                    Convert.ToInt32(recovery.GetType().GetProperty("Attempt")?.GetValue(recovery)) == attempt);
                if (attempt < 3)
                {
                    recovery?.GetType().GetMethod("MarkModuleCompleted")
                        ?.Invoke(recovery, new object[] { "swap.cancel-button" });
                    recovery?.GetType().GetMethod("MarkModuleStarting")
                        ?.Invoke(recovery, new object[] { "demo.sample", "onEnable" });
                }
            }

            var safeMode = recovery?.GetType().GetProperty("SafeMode")?.GetValue(recovery);
            Expect("two unclean starts trigger safe mode", Convert.ToBoolean(safeMode ?? false));
            Expect("safe mode exposes a machine-readable trigger code",
                Convert.ToString(recovery?.GetType().GetProperty("SafeModeReasonCode")
                    ?.GetValue(recovery)) == "unstable-start");
            Expect("safe mode preserves the triggering unclean count",
                Convert.ToInt32(recovery?.GetType().GetProperty("SafeModeUncleanStarts")
                    ?.GetValue(recovery) ?? 0) >= 2);
            Expect("safe mode carries the module that was starting before the unclean exit",
                Convert.ToString(recovery?.GetType().GetProperty("SuspectModule")?.GetValue(recovery)) ==
                "demo.sample");
            Expect("safe mode carries the suspect phase reached before the unclean exit",
                Convert.ToString(recovery?.GetType().GetProperty("SuspectPhase")?.GetValue(recovery)) ==
                "onEnable");
            Expect("safe mode carries the last successfully enabled module",
                Convert.ToString(recovery?.GetType().GetProperty("LastCompletedModule")?.GetValue(recovery)) ==
                "swap.cancel-button");
            Expect("recovery journal exists",
                File.Exists(Convert.ToString(recovery?.GetType().GetProperty("JournalPath")
                    ?.GetValue(recovery))));

            var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
            hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

            var api = hostType.GetProperty("Api", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                      as PCL2Swap.Api.ISwapApi;
            Expect("Host.SafeMode reports safe mode", api?.Host?.SafeMode == true);
            Expect("Host.SafeModeReason is populated",
                !string.IsNullOrWhiteSpace(api?.Host?.SafeModeReason));
            Expect("Host.RecoveryDirectory reports the data root",
                (api?.Host?.RecoveryDirectory ?? string.Empty)
                .StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase));

            var manager = hostType.GetProperty("Addons", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);
            var records = (manager?.GetType().GetProperty("Records")?.GetValue(manager)
                           as System.Collections.IEnumerable)
                ?.Cast<object>().ToList() ?? new List<object>();
            Expect("all built-ins and the package remain visible", records.Count == 4);
            Expect("every addon is skipped in safe mode",
                records.Count == 4 && records.All(record =>
                    Convert.ToString(record.GetType().GetProperty("Status").GetValue(record)) ==
                    "skipped-safe-mode"));

            var isEnabled = manager?.GetType().GetMethod("IsEnabled");
            Expect("safe mode blocks runtime enablement for built-ins and third-party modules",
                !Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "swap.download-mc" }) ?? true) &&
                !Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "demo.sample" }) ?? true));

            var setEnabled = manager?.GetType().GetMethod("SetEnabled");
            Expect("safe mode rejects a live enable request",
                !Convert.ToBoolean(setEnabled?.Invoke(manager,
                    new object[] { "demo.sample", true }) ?? true));

            var activeRecovery = manager?.GetType().GetProperty("Recovery",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager);
            Expect("safe mode preserves the original machine-readable reason",
                Convert.ToString(activeRecovery?.GetType().GetProperty("SafeModeReasonCode")
                    ?.GetValue(activeRecovery)) == "unstable-start");
            Expect("the active recovery journal retains the suspect module",
                Convert.ToString(activeRecovery?.GetType().GetProperty("SuspectModule")
                    ?.GetValue(activeRecovery)) == "demo.sample");

            var disableSuspect = manager?.GetType().GetMethod("DisableSuspectAndLeaveSafeMode");
            Expect("safe mode exposes one-step suspect disable and safe-mode exit",
                Convert.ToBoolean(disableSuspect?.Invoke(manager, null) ?? false));
            Expect("clear is pending restart, not a live module load",
                Convert.ToBoolean(activeRecovery.GetType().GetProperty("SafeModeExitPending")
                    ?.GetValue(activeRecovery) ?? false));
            Expect("the current process remains isolated after clearing the persistent flag",
                Convert.ToBoolean(activeRecovery.GetType().GetProperty("SafeMode")
                    ?.GetValue(activeRecovery) ?? false) &&
                !Convert.ToBoolean(isEnabled?.Invoke(manager,
                    new object[] { "swap.download-mc" }) ?? true));
            Expect("recovery keeps the triggering unclean count visible after clearing",
                Convert.ToInt32(activeRecovery.GetType().GetProperty("SafeModeUncleanStarts")
                    ?.GetValue(activeRecovery) ?? 0) >= 2);
            var demoAfterDisable = records.FirstOrDefault(record =>
                Convert.ToString(record.GetType().GetProperty("Id").GetValue(record)) == "demo.sample");
            Expect("the suspect module is persisted as disabled",
                demoAfterDisable != null &&
                Convert.ToBoolean(demoAfterDisable.GetType().GetProperty("Enabled").GetValue(demoAfterDisable)) == false &&
                Convert.ToString(demoAfterDisable.GetType().GetProperty("Status").GetValue(demoAfterDisable)) ==
                "disabled");
            var modulesJson = File.Exists(PCL2Swap.Api.SwapPaths.ModulesFile)
                ? File.ReadAllText(PCL2Swap.Api.SwapPaths.ModulesFile)
                : string.Empty;
            var demoStateMatch = System.Text.RegularExpressions.Regex.Match(
                modulesJson, "\"demo\\.sample\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            Expect("modules.json records the disabled suspect",
                demoStateMatch.Success &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    demoStateMatch.Groups["body"].Value,
                    "\"enabled\"\\s*:\\s*false"));

            var next = begin?.Invoke(null, new object[]
            {
                PCL2Swap.Api.SwapShape.Injected, "2.99.0.1", moduleIds
            });
            Expect("the next launch no longer enters safe mode",
                !Convert.ToBoolean(next?.GetType().GetProperty("SafeMode")?.GetValue(next) ?? true));
            Expect("the next launch has no stale suspect",
                string.IsNullOrEmpty(Convert.ToString(next?.GetType().GetProperty("SuspectModule")
                    ?.GetValue(next))));

            Console.WriteLine(failures == 0 ? "[safe] RESULT: PASS" : "[safe] RESULT: FAIL (" + failures + ")");
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

            // Keep the language switch and generated addon state out of the owner's profile.
            Environment.SetEnvironmentVariable("PCL2SWAP_DATA",
                Path.Combine(Path.GetTempPath(), "Pcl2SwapPclTest-" + Guid.NewGuid().ToString("N")));

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

            var api = hostType.GetProperty("Api", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as PCL2Swap.Api.ISwapApi;
            var pcl = api?.GetType().GetProperty("Pcl")?.GetValue(api);
            Expect("typed PCL access exposed on ISwapApi", pcl != null);

            var i18n = api?.GetType().GetProperty("I18n")?.GetValue(api);
            Expect("English layer exposed on ISwapApi", i18n != null);
            if (i18n != null)
            {
                var i18nType = i18n.GetType();
                Expect("English layer loaded the expanded dictionary",
                    Convert.ToInt32(i18nType.GetProperty("Count")?.GetValue(i18n) ?? 0) >= 450);
                var original = i18nType.GetProperty("Language")?.GetValue(i18n);
                var languageEvents = 0;
                EventHandler languageHandler = (sender, args) => languageEvents++;
                ((PCL2Swap.Api.II18n)i18n).LanguageChanged += languageHandler;
                try
                {
                    i18nType.GetMethod("SetLanguage")?.Invoke(i18n,
                        new object[] { PCL2Swap.Api.SwapLanguage.English });
                    Expect("English layer translates the multiplayer module label",
                        Convert.ToString(i18nType.GetMethod("T", new[] { typeof(string), typeof(string) })
                            ?.Invoke(i18n, new object[] { "联机模块", "Multiplayer module" })) ==
                        "Multiplayer module");
                    var registered = ((PCL2Swap.Api.II18n)i18n).RegisterMany(
                        new Dictionary<string, string>
                        {
                            ["harness 已处理 {0} 项"] = "Harness processed {0} items"
                        });
                    Expect("I18n.RegisterMany updates the live dictionary", registered == 1);
                    Expect("I18n.Format translates before formatting",
                        ((PCL2Swap.Api.II18n)i18n).Format("harness 已处理 {0} 项", 7) ==
                        "Harness processed 7 items");
                    Expect("I18n.TryTranslate is non-mutating",
                        ((PCL2Swap.Api.II18n)i18n).TryTranslate("背景图片已清除！",
                            out var clearedTranslation) &&
                        clearedTranslation == "Background image cleared.");
                }
                finally
                {
                    i18nType.GetMethod("SetLanguage")?.Invoke(i18n,
                        new object[] { original ?? PCL2Swap.Api.SwapLanguage.Auto });
                    ((PCL2Swap.Api.II18n)i18n).LanguageChanged -= languageHandler;
                }
                Expect("English layer raises LanguageChanged on a real switch", languageEvents >= 2);
            }

            var hostInfo = api?.GetType().GetProperty("Host")?.GetValue(api);
            var pclVersion = Convert.ToString(hostInfo?.GetType().GetProperty("PclVersion")?.GetValue(hostInfo));
            Expect("Host.PclVersion reports the bound PCL build", !string.IsNullOrEmpty(pclVersion));
            Expect("Host.ApiVersion reports the additive 1.11 contract",
                Convert.ToString(hostInfo?.GetType().GetProperty("ApiVersion")?.GetValue(hostInfo)) == "1.11.0");

            var recovery = api?.GetType().GetProperty("Recovery")?.GetValue(api);
            var recoveryDiagnostics = Convert.ToString(recovery?.GetType().GetMethod("Diagnostics")
                ?.Invoke(recovery, null));
            Expect("Recovery backend exposes the journal path",
                !string.IsNullOrWhiteSpace(Convert.ToString(recovery?.GetType().GetProperty("JournalPath")
                    ?.GetValue(recovery))));
            Expect("Recovery backend exposes lifecycle diagnostics",
                recoveryDiagnostics != null &&
                recoveryDiagnostics.Contains("lifecycle phase:") &&
                recoveryDiagnostics.Contains("suspect module:") &&
                recoveryDiagnostics.Contains("suspect phase:") &&
                recoveryDiagnostics.Contains("safe mode reason code:"));

            var moduleCatalog = api?.Modules;
            Expect("Module catalog exposes installed built-ins",
                moduleCatalog != null &&
                moduleCatalog.Modules.Count >= 3 &&
                moduleCatalog.Find("swap.cancel-button") != null);

            var diagnostics = api?.Diagnostics;
            var diagnosticEvents = 0;
            EventHandler diagnosticHandler = (sender, args) => diagnosticEvents++;
            if (diagnostics != null) diagnostics.Changed += diagnosticHandler;
            api?.LogWarning("diagnostics-harness-probe");
            Expect("Diagnostics exposes the bounded structured timeline",
                diagnostics != null &&
                diagnostics.Recent(20).Any(item => item.Message.Contains("diagnostics-harness-probe")));
            Expect("Diagnostics emits Changed and exports readable text",
                diagnosticEvents > 0 &&
                diagnostics.Export(20).Contains("diagnostics-harness-probe"));
            if (diagnostics != null) diagnostics.Changed -= diagnosticHandler;

            var files = api?.Files;
            var fileRoundTrip = false;
            var traversalRejected = false;
            if (files != null)
            {
                files.WriteText("harness/probe.txt", "api18-file-probe", false);
                fileRoundTrip = files.Exists("harness/probe.txt") &&
                                files.ReadText("harness/probe.txt") == "api18-file-probe" &&
                                files.List("harness").Contains("harness/probe.txt");
                try { files.GetPath("../escape.txt"); }
                catch (UnauthorizedAccessException) { traversalRejected = true; }
            }
            Expect("IAddonFiles round-trips text inside its private root", fileRoundTrip);
            Expect("IAddonFiles rejects traversal outside its root", traversalRejected);
            Expect("host exposes package assets and lifetime-scoped localization",
                api?.Assets != null && api.Localization != null &&
                api.Localization.AvailableLocales != null);

            var messageDeliveries = -1;
            var messagePayload = string.Empty;
            if (api?.Messaging != null)
            {
                using (api.Messaging.Subscribe("harness.topic", message =>
                       {
                           messagePayload = Convert.ToString(message.Payload);
                       }))
                {
                    messageDeliveries = api.Messaging.Publish("harness.topic", "api18-message");
                }
            }
            Expect("IAddonMessaging publishes synchronously",
                messageDeliveries == 1 && messagePayload == "api18-message");

            var secret = pcl?.GetType().GetProperty("Secret")?.GetValue(pcl);
            Expect("Secret family exposed", secret != null);
            Expect("Appearance and Window families exposed",
                pcl?.GetType().GetProperty("Appearance")?.GetValue(pcl) != null &&
                pcl.GetType().GetProperty("Window")?.GetValue(pcl) != null);
            var appearance = pcl?.GetType().GetProperty("Appearance")?.GetValue(pcl);
            if (appearance != null)
            {
                var themeBefore = PCL.ModSecret.ThemeRefreshMainCalls;
                appearance.GetType().GetProperty("Hue")?.SetValue(appearance, 222.0, null);
                appearance.GetType().GetMethod("Refresh")?.Invoke(appearance, null);
                Expect("Appearance reads/writes host theme fields and calls ThemeRefreshMain",
                    Convert.ToInt32(appearance.GetType().GetProperty("ThemeId")?.GetValue(appearance)) == 4 &&
                    PCL.ModSecret.ColorHue == 222 &&
                    PCL.ModSecret.ThemeRefreshMainCalls == themeBefore + 1);
            }
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

            var requestBefore = PCL.ModNet.RequestCalls;
            var requestResult = Convert.ToString(net?.GetType().GetMethod("Request")?.Invoke(net,
                new object[]
                {
                    "https://example.com/api", "POST", null, "body", "text/plain",
                    "application/json", new Dictionary<string, string> { { "X-Test", "1" } },
                    true, true
                }));
            Expect("Net.Request reaches the host retry helper",
                PCL.ModNet.RequestCalls == requestBefore + 1 &&
                PCL.ModNet.LastRequest.Contains("POST https://example.com/api") &&
                requestResult == "response:https://example.com/api");

            var navigation = pcl?.GetType().GetProperty("Navigation")?.GetValue(pcl);
            Expect("Navigation family exposed", navigation != null);
            if (navigation != null)
            {
                var navigationType = navigation.GetType();
                Expect("Navigation reads the initial root page",
                    Convert.ToString(navigationType.GetProperty("CurrentRootPage")?.GetValue(navigation)) == "Launch");
                navigationType.GetMethod("Go")?.Invoke(navigation,
                    new object[] { "Setup", "SetupUI" });
                Expect("Navigation.Go drives FormMain.PageChange",
                    PCL.ModMain.FrmMain.PageChangeCalls == 1 &&
                    Convert.ToString(navigationType.GetProperty("CurrentRootPage")?.GetValue(navigation)) == "Setup" &&
                    Convert.ToString(navigationType.GetProperty("CurrentSubPage")?.GetValue(navigation)) == "SetupUI");
                navigationType.GetMethod("Back")?.Invoke(navigation, null);
                Expect("Navigation.Back drives FormMain.PageBack",
                    PCL.ModMain.FrmMain.PageBackCalls == 1);
            }

            var threading = pcl?.GetType().GetProperty("Threading")?.GetValue(pcl);
            Expect("Threading family exposed", threading != null);
            if (threading != null)
            {
                var threadingType = threading.GetType();
                var genericInvoke = threadingType.GetMethods()
                    .FirstOrDefault(method => method.Name == "Invoke" &&
                                              method.IsGenericMethodDefinition &&
                                              method.GetParameters().Length == 1);
                var value = genericInvoke?.MakeGenericMethod(typeof(int))
                    .Invoke(threading, new object[] { (Func<int>)(() => 17) });
                Expect("Threading.Invoke(value) runs the callback",
                    Convert.ToInt32(value) == 17);
                var scheduled = threadingType.GetMethod("Schedule")
                    ?.Invoke(threading, new object[] { TimeSpan.Zero, (Action)(() => { }) });
                Expect("Threading.Schedule returns a disposable handle", scheduled is IDisposable);
                (scheduled as IDisposable)?.Dispose();

                var repeatCount = 0;
                var repeat = threadingType.GetMethod("Repeat")
                    ?.Invoke(threading,
                        new object[] { TimeSpan.FromMilliseconds(20), (Action)(() => repeatCount++) })
                    as IDisposable;
                Thread.Sleep(120);
                repeat?.Dispose();
                var repeatAfterDispose = repeatCount;
                Thread.Sleep(60);
                Expect("Threading.Repeat ticks and disposal stops it",
                    repeatCount >= 2 && repeatCount == repeatAfterDispose);
            }

            var environment = pcl?.GetType().GetProperty("Environment")?.GetValue(pcl);
            Expect("Environment family exposed", environment != null);
            if (environment != null)
            {
                var environmentType = environment.GetType();
                Expect("Environment exposes the host path and data directory",
                    !string.IsNullOrEmpty(Convert.ToString(environmentType.GetProperty("HostPath")
                        ?.GetValue(environment))) &&
                    !string.IsNullOrEmpty(Convert.ToString(environmentType.GetProperty("DataDirectory")
                        ?.GetValue(environment))));
            }

            var profiles = pcl?.GetType().GetProperty("Profiles")?.GetValue(pcl);
            Expect("Profiles family exposed", profiles != null);
            if (profiles != null)
            {
                var profilesType = profiles.GetType();
                Expect("Profiles.LoginName/LoginStatus reach ModLaunch",
                    Convert.ToString(profilesType.GetMethod("LoginName")?.Invoke(profiles, null)) == "Dissociate" &&
                    Convert.ToString(profilesType.GetMethod("LoginStatus")?.Invoke(profiles, null)) == string.Empty);
                Expect("Profiles.LoginData/SelectedSkin return host objects",
                    profilesType.GetMethod("LoginData")?.Invoke(profiles, null) != null &&
                    profilesType.GetMethod("SelectedSkin")?.Invoke(profiles, null) != null);
            }

            var animation = pcl?.GetType().GetProperty("Animation")?.GetValue(pcl);
            Expect("Animation family exposed", animation != null);

            // ---- typed families: Instance / Download / Launch ----
            var instance = pcl?.GetType().GetProperty("Instance")?.GetValue(pcl);
            Expect("Instance family exposed", instance != null);
            if (instance != null)
            {
                var instanceType = instance.GetType();
                instanceType.GetProperty("FolderSelected")?.SetValue(instance, @"C:\instances2");
                Expect("Instance.FolderSelected writes through to the host",
                    PCL.ModMinecraft.McFolderSelected == @"C:\instances2");
                Expect("Instance.GetFriendlyName reaches the host method",
                    Convert.ToString(instanceType.GetMethod("GetFriendlyName")
                        ?.Invoke(instance, new object[] { "X" })) == "友好名称:X");

                var list = instanceType.GetMethod("List")?.Invoke(instance, new object[] { null })
                           as IReadOnlyList<object>;
                Expect("Instance.List flattens the host's cached list", list != null && list.Count == 1);
                var first = list != null && list.Count > 0 ? list[0] : null;
                Expect("Instance helpers read McInstance members",
                    Convert.ToString(instanceType.GetMethod("GetName")?.Invoke(instance, new[] { first })) == "Demo" &&
                    Convert.ToString(instanceType.GetMethod("GetVersionDisplayName")
                        ?.Invoke(instance, new[] { first })) == "Demo (1.21.4)");
                Expect("Instance.GetNativesFolder reaches ModLaunch",
                    Convert.ToString(instanceType.GetMethod("GetNativesFolder")
                        ?.Invoke(instance, new[] { first })) == @"C:\temp\natives");
            }

            var download = pcl?.GetType().GetProperty("Download")?.GetValue(pcl);
            Expect("Download family exposed", download != null);
            if (download != null)
            {
                var downloadType = download.GetType();
                Expect("Download.PreferMojang reads the host flag",
                    Convert.ToBoolean(downloadType.GetProperty("PreferMojang")?.GetValue(download) ?? false));
                Expect("Download.SourceModGet reaches the host",
                    Convert.ToString(downloadType.GetMethod("SourceModGet")
                        ?.Invoke(download, new object[] { "sodium" })) == "mod-sodium");
                Expect("Download.ClientListGet returns the host payload",
                    downloadType.GetMethod("ClientListGet")?.Invoke(download, new object[] { "mojang" }) != null);
                var request = downloadType.GetMethod("ModRequest")
                    ?.Invoke(download, new object[] { "https://example.com/api", "POST", null, null });
                Expect("Download.ModRequest posts through the host helper",
                    PCL.ModDownload.ModRequestCalls == 1 &&
                    Convert.ToString(request) == "POST https://example.com/api");
            }

            var launch = pcl?.GetType().GetProperty("Launch")?.GetValue(pcl);
            Expect("Launch family exposed", launch != null);
            if (launch != null)
            {
                var launchType = launch.GetType();
                Expect("Launch.LoginName reaches the host",
                    Convert.ToString(launchType.GetMethod("LoginName")?.Invoke(launch, null)) == "Dissociate");
                Expect("Launch.Start drives the host launcher pipeline",
                    Convert.ToBoolean(launchType.GetMethod("Start")
                        ?.Invoke(launch, new object[] { null }) ?? false) &&
                    PCL.ModLaunch.StartCalls == 1);
            }

            var loader = pcl?.GetType().GetProperty("Loader")?.GetValue(pcl);
            Expect("Loader family exposed", loader != null);
            if (loader != null)
            {
                var loaderType = loader.GetType();
                Expect("Loader.TaskbarProgress reads the host value",
                    Math.Abs(Convert.ToDouble(loaderType.GetProperty("TaskbarProgress")
                        ?.GetValue(loader) ?? 0.0) - 0.42) < 0.0001);

                loaderType.GetMethod("RefreshTaskbar")?.Invoke(loader, null);
                Expect("Loader.RefreshTaskbar reaches the host", PCL.ModLoader.TaskbarRefreshCalls == 1);

                loaderType.GetMethod("TaskbarAdd")?.Invoke(loader, new object[] { "combo" });
                Expect("Loader.TaskbarAdd publishes to the host",
                    Convert.ToString(PCL.ModLoader.LastTaskbarCombo) == "combo");

                var stub = new PCL.ModLoader.LoaderBase();
                loaderType.GetMethod("Start")?.Invoke(loader, new object[] { stub, "input", true });
                Expect("Loader.Start drives a loader instance",
                    stub.StartCalls == 1 && stub.LastForceRestart &&
                    Convert.ToBoolean(loaderType.GetMethod("IsRunning")
                        ?.Invoke(loader, new object[] { stub }) ?? false));

                stub.Progress = 0.5;
                Expect("Loader.GetProgress reads the loader",
                    Math.Abs(Convert.ToDouble(loaderType.GetMethod("GetProgress")
                        ?.Invoke(loader, new object[] { stub }) ?? 0.0) - 0.5) < 0.0001);

                loaderType.GetMethod("Cancel")?.Invoke(loader, new object[] { stub });
                loaderType.GetMethod("Failed")?.Invoke(loader, new object[] { stub, new Exception("boom") });
                Expect("Loader.Cancel/Failed reach the loader",
                    stub.CancelCalls == 1 && stub.FailedCalls == 1);

                // ---- watch / combo / weights: the progress + task surface addons build on ----
                var watchMethod = loaderType.GetMethod("Watch");
                var watched = new PCL.ModLoader.LoaderBase();
                var states = new List<string>();
                var progressSeen = new List<double>();
                var watch = watchMethod?.Invoke(loader, new object[]
                {
                    watched,
                    new Action<double>(value => progressSeen.Add(value)),
                    new Action<string>(state => states.Add(state))
                }) as IDisposable;
                Expect("Loader.Watch seeds the current state",
                    watch != null && states.Contains("Waiting"));

                watched.Progress = 0.25;
                watched.Start(null, false);
                Expect("Loader.Watch sees progress ticks and state transitions",
                    progressSeen.Contains(0.25) && states.Contains("Loading"));

                watch?.Dispose();
                var statesAfterDispose = states.Count;
                watched.Progress = 0.75;
                Expect("a disposed loader watch stops receiving", states.Count == statesAfterDispose);

                var auto = new PCL.ModLoader.LoaderBase();
                var autoStates = new List<string>();
                var autoProgress = new List<double>();
                var autoWatch = watchMethod?.Invoke(loader, new object[]
                {
                    auto,
                    new Action<double>(value => autoProgress.Add(value)),
                    new Action<string>(state => autoStates.Add(state))
                }) as IDisposable;
                auto.Cancel();
                var autoAfterCancel = autoProgress.Count;
                auto.Progress = 0.9;
                Expect("a terminal state releases the watch automatically",
                    autoStates.Contains("Canceled") &&
                    autoAfterCancel == 1 && autoProgress.Count == 1);
                autoWatch?.Dispose();

                var previewCalls = 0;
                var previewWatch = loaderType.GetMethod("WatchPreviewFinish")?.Invoke(loader,
                    new object[] { auto, new Action<object>(_ => previewCalls++) }) as IDisposable;
                auto.RaisePreviewFinish();
                Expect("Loader.WatchPreviewFinish bridges the host event", previewCalls == 1);
                previewWatch?.Dispose();

                Expect("Loader.GetName reads the host field",
                    Convert.ToString(loaderType.GetMethod("GetName")?.Invoke(loader, new object[] { watched })) ==
                    "harness loader");
                loaderType.GetMethod("SetProgressWeight")?.Invoke(loader, new object[] { watched, 2.5 });
                Expect("Loader.ProgressWeight round-trips",
                    Math.Abs(Convert.ToDouble(loaderType.GetMethod("GetProgressWeight")
                        ?.Invoke(loader, new object[] { watched }) ?? 0.0) - 2.5) < 0.0001);

                var second = new PCL.ModLoader.LoaderBase();
                var combo = loaderType.GetMethod("CreateCombo")?.Invoke(loader,
                    new object[] { "harness combo", new List<object> { watched, second }, null });
                Expect("Loader.CreateCombo builds the host combo type",
                    combo != null &&
                    combo.GetType().Name.StartsWith("LoaderCombo", StringComparison.Ordinal));

                var tasks = loaderType.GetMethod("ListTasks")
                    ?.Invoke(loader, new object[] { combo, false }) as System.Collections.IList;
                Expect("Loader.ListTasks flattens the combo", tasks != null && tasks.Count == 2);

                PCL.ModLoader.LoaderTaskbar.Clear();
                PCL.ModLoader.LoaderTaskbar.Add(new PCL.ModLoader.LoaderBase { Name = "taskbar task" });
                var all = loaderType.GetMethod("ListAll")
                    ?.Invoke(loader, new object[] { false }) as System.Collections.IList;
                Expect("Loader.ListAll reads the host's taskbar registry",
                    all != null && all.Count == 1);
                var running = loaderType.GetMethod("ListAll")
                    ?.Invoke(loader, new object[] { true }) as System.Collections.IList;
                Expect("Loader.ListAll(onlyRunning) filters by state", running != null && running.Count == 0);

                var failure = new Exception("loader fell over");
                second.Failed(failure);
                Expect("Loader.GetError surfaces the failure",
                    ReferenceEquals(loaderType.GetMethod("GetError")
                        ?.Invoke(loader, new object[] { second }), failure));
            }

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

            // Packs a module: manifestJson == null leaves manifest.json out (broken package),
            // a null id skips the entry dll.
            void PackAddon(string path, string id, string name, string version, string manifestJson)
            {
                using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
                {
                    if (manifestJson != null)
                    {
                        var manifest = zip.CreateEntry("manifest.json");
                        using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                            writer.Write(manifestJson);
                    }
                    if (id != null)
                    {
                        var dllEntry = zip.CreateEntry("PCL2Swap.SampleAddon.dll");
                        using (var target = dllEntry.Open())
                        using (var source = File.OpenRead(typeof(PCL2Swap.SampleAddon.SampleAddon).Assembly.Location))
                            source.CopyTo(target);
                    }
                }
            }

            // An assembly-load failure can carry a type whose name cannot be resolved any more, and
            // then even Exception.ToString() throws; print defensively.
            string SafeException(Exception ex)
            {
                try
                {
                    var text = ex.GetType().FullName + ": " + ex.Message;
                    if (ex.InnerException != null)
                        text += " <- " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message;
                    return text;
                }
                catch
                {
                    return "(unprintable exception)";
                }
            }

            string HashFile(string path)
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty)
                        .ToLowerInvariant();
            }

            string KeyIdFor(RSA key)
            {
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToXmlString(false)));
                    var text = new StringBuilder(16);
                    for (var index = 0; index < 8; index++) text.Append(hash[index].ToString("x2"));
                    return text.ToString();
                }
            }

            void WriteZipEntry(ZipArchive archive, string name, byte[] bytes)
            {
                var entry = archive.CreateEntry(name);
                using (var stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
            }

            /// <summary>Packs one signed module: manifest (+ payload hashes) + RSA-SHA256 signature.</summary>
            void PackSignedAddon(string path, string manifestJson, RSA key, string dllPath = null)
            {
                var dll = dllPath ?? typeof(PCL2Swap.SampleAddon.SampleAddon).Assembly.Location;
                var dllName = Path.GetFileName(dll);
                var body = manifestJson.TrimEnd();
                if (body.EndsWith("}", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 1).TrimEnd();
                var withFiles = body + ",\n  \"files\": [{\"path\": \"" + dllName + "\", \"sha256\": \"" +
                                HashFile(dll) + "\"}]\n}";
                var manifestBytes = new UTF8Encoding(false).GetBytes(withFiles);

                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "manifest.json", manifestBytes);
                    if (key != null)
                    {
                        var signature = key.SignData(manifestBytes, HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                        var signatureJson = "{\"algorithm\":\"RSA-SHA256\",\"keyId\":\"" + KeyIdFor(key) +
                                             "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
                        WriteZipEntry(zip, "signature.json", new UTF8Encoding(false).GetBytes(signatureJson));
                    }
                    zip.CreateEntryFromFile(dll, dllName);
                }
            }

            /// <summary>Rewrites a package's dll entry so its bytes no longer match the signed hash.</summary>
            void TamperDllEntry(string path)
            {
                string dllName;
                byte[] manifestBytes;
                using (var zip = ZipFile.OpenRead(path))
                {
                    dllName = zip.Entries.First(entry => entry.FullName.EndsWith(".dll",
                        StringComparison.OrdinalIgnoreCase)).FullName;
                    using (var stream = zip.GetEntry("manifest.json").Open())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        manifestBytes = buffer.ToArray();
                    }
                }

                var tampered = new byte[manifestBytes.Length + 16];
                Array.Copy(manifestBytes, tampered, manifestBytes.Length);

                using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    zip.GetEntry(dllName)?.Delete();
                    WriteZipEntry(zip, dllName, tampered);   // wrong payload, hash now mismatches
                }
            }

            // Keep the test out of the real profile: the module state and addon store now live in
            // SwapPaths.DataRoot, which PCL2SWAP_DATA overrides.
            var dataRoot = Path.Combine(Path.GetTempPath(), "PCL2SwapTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("PCL2SWAP_DATA", dataRoot);
            var addonRoot = PCL2Swap.Api.SwapPaths.AddonsDirectory;
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
                                     "\"description\":\"harness sample\"," +
                                     "\"scopes\":[" +
                                     "{\"id\":\"sample.hooks\",\"name\":\"演示钩子\",\"nameEn\":\"Demo hook\"," +
                                     "\"description\":\"由示例钩子使用\",\"descriptionEn\":\"Used by the demo hook\"," +
                                     "\"defaultEnabled\":true}," +
                                     "{\"id\":\"sample.optional\",\"name\":\"可选功能\",\"nameEn\":\"Optional feature\"," +
                                     "\"description\":\"默认关闭的可选功能\",\"descriptionEn\":\"Optional feature disabled by default\"," +
                                     "\"defaultEnabled\":false}" +
                                     "]}");
                    }
                    var dllEntry = zip.CreateEntry("PCL2Swap.SampleAddon.dll");
                    using (var target = dllEntry.Open())
                    using (var source = File.OpenRead(typeof(PCL2Swap.SampleAddon.SampleAddon).Assembly.Location))
                        source.CopyTo(target);
                    WriteZipEntry(zip, "assets/branding/readme.txt",
                        new UTF8Encoding(false).GetBytes("sample asset"));
                    WriteZipEntry(zip, "i18n/en-US.json",
                        new UTF8Encoding(false).GetBytes(
                            "{\"示例设置\":\"Sample Settings Pack\",\"示例模块（Sample Addon）\":\"Sample Addon Pack\"}"));
                    WriteZipEntry(zip, "i18n/zh-CN.json",
                        new UTF8Encoding(false).GetBytes(
                            "{\"示例设置\":\"示例设置\",\"示例模块（Sample Addon）\":\"示例模块（Sample Addon）\"}"));
                }
                Console.WriteLine("[addon] packed " + archive);

                var hostType = payload.GetType("PclDlSwap.Payload.Swap.SwapHost", throwOnError: true);
                var start = hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                start.Invoke(null, new object[] { PCL2Swap.Api.SwapShape.Injected, typeof(PCL.ModMain).Assembly });

                var api = hostType.GetProperty("Api", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as PCL2Swap.Api.ISwapApi;
                var manager = hostType.GetProperty("Addons", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                var records = (manager?.GetType().GetProperty("Records")?.GetValue(manager) as System.Collections.IEnumerable)
                    ?.Cast<object>().ToList() ?? new List<object>();
                Console.WriteLine("[addon] records: " + records.Count);
                foreach (var record in records)
                {
                    var recordType = record.GetType();
                    var failure = recordType.GetProperty("Failure").GetValue(record) as Exception;
                    Console.WriteLine("  - " + recordType.GetProperty("Id").GetValue(record) +
                                      " status=" + recordType.GetProperty("Status").GetValue(record) +
                                      (failure == null ? string.Empty : " failure=" + failure));
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
                Expect("sample addon saw the API version", StaticString("LastApiVersion") == "1.11.0");
                Expect("sample addon sees only the default-enabled feature scope",
                    StaticString("LastEnabledScopes") == "sample.hooks");

                // ---- module state: persisted, and it gates the built-in features ----
                var isEnabled = manager?.GetType().GetMethod("IsEnabled");
                var setEnabled = manager?.GetType().GetMethod("SetEnabled");
                Expect("module state exposes IsEnabled/SetEnabled", isEnabled != null && setEnabled != null);
                var catalogEvents = 0;
                EventHandler catalogHandler = (sender, args) => catalogEvents++;
                var catalog = api?.Modules;
                if (catalog != null) catalog.Changed += catalogHandler;
                Expect("module catalog exposes built-ins and the package",
                    catalog != null && catalog.Modules.Count == 4 &&
                    catalog.Find("test.sample") != null);

                var modulesFile = PCL2Swap.Api.SwapPaths.ModulesFile;
                Expect("OTA Protector starts enabled",
                    Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "swap.ota-protector" })));

                var disabled = Convert.ToBoolean(setEnabled?.Invoke(manager,
                    new object[] { "swap.ota-protector", false }));
                var stateAfterDisable = File.Exists(modulesFile) ? File.ReadAllText(modulesFile) : string.Empty;
                Expect("disabling a module flips the runtime gate", disabled &&
                    !Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "swap.ota-protector" })));
                Expect("module state persists to modules.json",
                    stateAfterDisable.Contains("swap.ota-protector") && stateAfterDisable.Contains("false"));

                var reEnabled = Convert.ToBoolean(setEnabled?.Invoke(manager,
                    new object[] { "swap.ota-protector", true }));
                Expect("re-enabling restores the gate", reEnabled &&
                    Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "swap.ota-protector" })));
                Expect("module catalog raises Changed for governance edits",
                    catalogEvents >= 2);
                Expect("addon store lives under the durable root",
                    Directory.Exists(Path.Combine(dataRoot, "addons")));

                // ---- event bus: the sample addon subscribed through IHooks and must receive ----
                var hooks = api?.GetType().GetProperty("Hooks")?.GetValue(api);
                var raiseDialog = hooks?.GetType().GetMethod("RaiseDialogOpening",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var raiseUpdate = hooks?.GetType().GetMethod("RaiseUpdateRequested",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Expect("hook bus exposes the raise surface", raiseDialog != null && raiseUpdate != null);

                raiseDialog?.Invoke(hooks, new object[]
                {
                    new PCL2Swap.Api.DialogEventArgs
                    {
                        Caption = "harness", Title = "事件测试", ForceWait = true, CancelAdded = true
                    }
                });
                raiseUpdate?.Invoke(hooks, new object[]
                {
                    new PCL2Swap.Api.UpdateEventArgs
                    {
                        Silent = true, Urls = new[] { "https://example.com/update" }
                    }
                });

                Expect("sample addon received DialogOpening", StaticInt("DialogEvents") == 1);
                Expect("sample addon received UpdateRequested", StaticInt("UpdateEvents") == 1);
                Expect("sample addon saw the dialog title", StaticString("LastDialogTitle") == "事件测试");

                // ---- feature scopes: defaults, persistence, catalog events and live hook gating ----
                var createScopedApi = manager?.GetType().GetMethod("CreateScopedApi",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var scopedApi = createScopedApi?.Invoke(manager, new[] { sample }) as PCL2Swap.Api.ISwapApi;
                Expect("scoped API exposes IModuleScopes", scopedApi?.Scopes != null);
                Expect("scoped API exposes IAddonConfig", scopedApi?.Config != null);
                Expect("scoped API exposes IAddonServices", scopedApi?.Services != null);
                Expect("scoped API exposes package assets and module-localized text",
                    scopedApi?.Assets != null &&
                    scopedApi.Assets.Assets.Count == 1 &&
                    scopedApi.Assets.Exists("branding/readme.txt") &&
                    scopedApi.Localization != null &&
                    scopedApi.Localization.AvailableLocales.Contains("en-US"));
                var i18nApi = api?.I18n;
                var languageBeforeAssetProbe = i18nApi?.Language ?? PCL2Swap.Api.SwapLanguage.Auto;
                var localizedAssetText = string.Empty;
                try
                {
                    i18nApi?.SetLanguage(PCL2Swap.Api.SwapLanguage.English);
                    localizedAssetText = scopedApi?.Localization.T("示例设置", "fallback") ?? string.Empty;
                }
                finally
                {
                    i18nApi?.SetLanguage(languageBeforeAssetProbe);
                }
                Expect("module localization pack resolves across a live language switch",
                    localizedAssetText == "Sample Settings Pack");
                var escapedAsset = false;
                try { scopedApi?.Assets.GetPath("../outside.txt"); }
                catch (UnauthorizedAccessException) { escapedAsset = true; }
                catch (ArgumentException) { escapedAsset = true; }
                Expect("package asset paths cannot escape the addon directory", escapedAsset);
                Expect("scope catalog localizes names and descriptions",
                    scopedApi?.Scopes.Scopes.Count == 2 &&
                    scopedApi.Scopes.Scopes[0].Name == "演示钩子" &&
                    scopedApi.Scopes.Scopes[0].NameEnglish == "Demo hook" &&
                    scopedApi.Scopes.Scopes[0].DescriptionEnglish == "Used by the demo hook");
                Expect("scope defaults are applied before the user changes anything",
                    scopedApi?.Scopes.IsEnabled("sample.hooks") == true &&
                    scopedApi.Scopes.IsEnabled("sample.optional") == false &&
                    scopedApi.Scopes.Enabled.SequenceEqual(new[] { "sample.hooks" }));

                var sampleConfigPath = PCL2Swap.Api.SwapPaths.AddonConfigFile("test.sample");
                File.WriteAllText(sampleConfigPath, "{\"loaded\":\"legacy\",\"legacyKey\":\"yes\"}");
                var configApi = createScopedApi?.Invoke(manager, new[] { sample }) as PCL2Swap.Api.ISwapApi;
                var configEvents = 0;
                EventHandler configHandler = (sender, args) => configEvents++;
                if (configApi?.Config != null) configApi.Config.Changed += configHandler;
                Expect("legacy flat config loads as schema 1",
                    configApi?.Config.SchemaVersion == 1 &&
                    configApi.Config.Get("legacyKey") == "yes");
                var migrated = configApi?.Config.Migrate(2, transaction =>
                {
                    transaction.Set("demo.refreshIndex", "0");
                    transaction.Remove("loaded");
                    transaction.Remove("legacyKey");
                }) == true;
                var configDisk = File.Exists(sampleConfigPath) ? File.ReadAllText(sampleConfigPath) : string.Empty;
                Expect("config migration stages, commits and rewrites the schema envelope",
                    migrated && configApi.Config.SchemaVersion == 2 &&
                    configApi.Config.Get("demo.refreshIndex") == "0" &&
                    configApi.Config.Get("loaded") == null &&
                    configApi.Config.Get("legacyKey") == null &&
                    configDisk.Contains("\"_schema\":2") &&
                    configDisk.Contains("\"_values\"") &&
                    File.Exists(sampleConfigPath + ".bak"));
                Expect("config migration raises one change event", configEvents == 1);
                var rollbackError = false;
                try
                {
                    configApi.Config.Migrate(3, transaction =>
                    {
                        transaction.Set("rollbackProbe", "must-not-commit");
                        throw new InvalidOperationException("migration failure");
                    });
                }
                catch (InvalidOperationException)
                {
                    rollbackError = true;
                }
                Expect("failed config migration leaves schema and values unchanged",
                    rollbackError && configApi.Config.SchemaVersion == 2 &&
                    configApi.Config.Get("rollbackProbe") == null);
                if (configApi?.Config != null) configApi.Config.Changed -= configHandler;

                var serviceEvents = 0;
                EventHandler<PCL2Swap.Api.SwapServiceChangedEventArgs> serviceHandler =
                    (sender, args) => serviceEvents++;
                if (scopedApi?.Services != null) scopedApi.Services.Changed += serviceHandler;
                var configInfo = catalog?.Find("test.sample");
                Expect("module catalog reflects the migrated config",
                    configInfo?.ConfigSchemaVersion == 2 && configInfo.ConfigKeyCount >= 1);
                Expect("module catalog exposes packaged asset and locale inventories",
                    configInfo?.AssetCount == 1 &&
                    configInfo.Locales.Contains("en-US") &&
                    configInfo.Locales.Contains("zh-CN"));
                var serviceToken = scopedApi?.Services.Register(
                    "services/test.sample/probe", new object(), "1.0", "harness service");
                Expect("module services register, query by contract and report provenance",
                    serviceToken != null &&
                    scopedApi.Services.TryGet<object>("services/test.sample/probe", out var registeredService) &&
                    registeredService != null &&
                    scopedApi.Services.Services.Any(info =>
                        info.ServiceId == "services/test.sample/probe" &&
                        info.Provider == "test.sample"));
                Expect("service registration raises the registry event", serviceEvents == 1);
                var disposableService = scopedApi?.Services.Register(
                    "services/test.sample/disposable", new object(), "1.0", "disposable service");
                disposableService?.Dispose();
                Expect("disposing a service token unregisters exactly that registration",
                    scopedApi?.Services.TryGet<object>("services/test.sample/disposable", out _) == false);

                var scopeEvents = 0;
                EventHandler scopeHandler = (sender, args) => scopeEvents++;
                if (scopedApi != null) scopedApi.Scopes.Changed += scopeHandler;
                var setScope = manager?.GetType().GetMethod("SetScopeEnabled");
                Expect("module manager exposes SetScopeEnabled", setScope != null);
                var catalogEventsBeforeScope = catalogEvents;

                // ---- module-scoped hooks: the gate follows both the scope and module toggles ----
                var attachScoped = api?.GetType().GetMethods(
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "AttachAddonHook" &&
                                              method.GetParameters().Length == 7 &&
                                              method.GetParameters()[1].ParameterType == typeof(string));
                Expect("host exposes the scoped attach", attachScoped != null);

                var token = attachScoped?.Invoke(api, new object[]
                {
                    "test.sample", "PCL.HookTarget", "Ping", "sample.ping",
                    "sample.hooks",
                    PCL2Swap.Api.HookMode.Replace,
                    (Func<int, int>)HookHandlers.Sample
                }) as IDisposable;
                Expect("scoped hook installed", token != null);

                var handlerBefore = HookHandlers.SampleCalls;
                var hooked = PCL.HookTarget.Ping(5);
                Expect("enabled module's handler runs",
                    HookHandlers.SampleCalls == handlerBefore + 1 && hooked == 105);

                var scopeDisabled = Convert.ToBoolean(setScope?.Invoke(manager,
                    new object[] { "test.sample", "sample.hooks", false }) ?? false);
                var scopeForwarded = PCL.HookTarget.Ping(5);
                var modulesAfterScopeChange = File.Exists(modulesFile) ? File.ReadAllText(modulesFile) : string.Empty;
                Expect("disabling a feature scope immediately forwards its hook",
                    scopeDisabled && HookHandlers.SampleCalls == handlerBefore + 1 && scopeForwarded == 10);
                Expect("feature-scope edits persist and notify both change surfaces",
                    modulesAfterScopeChange.Contains("\"scopes\":[]") &&
                    scopeEvents == 1 && catalogEvents == catalogEventsBeforeScope + 1);
                Expect("scope API reports the live disabled state",
                    scopedApi?.Scopes.IsEnabled("sample.hooks") == false &&
                    !scopedApi.Scopes.Enabled.Contains("sample.hooks"));

                var scopeReenabled = Convert.ToBoolean(setScope?.Invoke(manager,
                    new object[] { "test.sample", "sample.hooks", true }) ?? false);
                var scopeAgain = PCL.HookTarget.Ping(5);
                Expect("re-enabling a feature scope restores its hook without reattaching",
                    scopeReenabled && HookHandlers.SampleCalls == handlerBefore + 2 && scopeAgain == 105 &&
                    scopeEvents == 2 && catalogEvents == catalogEventsBeforeScope + 2);

                setEnabled?.Invoke(manager, new object[] { "test.sample", false });
                var forwarded = PCL.HookTarget.Ping(5);
                Expect("disabled module forwards to the original",
                    HookHandlers.SampleCalls == handlerBefore + 2 && forwarded == 10);
                Expect("disabling a module removes every service it published",
                    api.Services.TryGet<object>("services/test.sample/probe", out _) == false &&
                    serviceEvents >= 2);
                var disabledServiceAccess = false;
                try { _ = scopedApi?.Services.Services; }
                catch (InvalidOperationException) { disabledServiceAccess = true; }
                Expect("a disabled module cannot query or publish services", disabledServiceAccess);

                setEnabled?.Invoke(manager, new object[] { "test.sample", true });
                var again = PCL.HookTarget.Ping(5);
                Expect("re-enabled module hooks again",
                    HookHandlers.SampleCalls == handlerBefore + 3 && again == 105);
                Expect("re-enabling a module republishes its own service",
                    api.Services.TryGet<object>("services/test.sample/echo", out _) == true &&
                    serviceEvents >= 3);
                serviceToken?.Dispose();
                if (scopedApi != null) scopedApi.Services.Changed -= serviceHandler;

                token?.Dispose();
                var afterDispose = PCL.HookTarget.Ping(5);
                Expect("disposed token forwards to the original",
                    HookHandlers.SampleCalls == handlerBefore + 3 && afterDispose == 10);
                if (scopedApi != null) scopedApi.Scopes.Changed -= scopeHandler;
                Expect("unknown scope edits are rejected",
                    Convert.ToBoolean(setScope?.Invoke(manager,
                        new object[] { "test.sample", "sample.missing", true }) ?? true) == false);
                var publicScopeHits = 0;
                var publicScopeHook = scopedApi?.Hooks.Attach(
                    "PCL.HookTarget", "Ping", "sample.public-scope", "sample.hooks",
                    (Action<int>)(_ => publicScopeHits++), PCL2Swap.Api.HookMode.Prefix);
                PCL.HookTarget.Ping(1);
                setScope?.Invoke(manager, new object[] { "test.sample", "sample.hooks", false });
                PCL.HookTarget.Ping(1);
                setScope?.Invoke(manager, new object[] { "test.sample", "sample.hooks", true });
                PCL.HookTarget.Ping(1);
                Expect("the public scoped-attach overload follows the same live scope gate",
                    publicScopeHook != null && publicScopeHits == 2);
                publicScopeHook?.Dispose();
                try
                {
                    scopedApi?.Hooks.Attach(
                        "PCL.HookTarget", "Ping", "sample.unknown-scope", "sample.missing",
                        (Action<int>)(_ => { }), PCL2Swap.Api.HookMode.Prefix);
                    Expect("attaching to an undeclared feature scope is rejected", false);
                }
                catch (ArgumentException ex)
                {
                    Expect("attaching to an undeclared feature scope is rejected",
                        ex.Message.Contains("未声明") && ex.Message.Contains("sample.missing"));
                }

                // ---- hook modes: prefix observes before, postfix after, both keep the host result ----
                PCL.HookTarget.Order.Clear();
                var prefixToken = attachScoped?.Invoke(api, new object[]
                {
                    "test.sample", "PCL.HookTarget", "Ping", "sample.prefix",
                    null,
                    PCL2Swap.Api.HookMode.Prefix, (Action<int>)HookHandlers.Before
                }) as IDisposable;
                Expect("prefix hook installed", prefixToken != null);
                var prefixResult = PCL.HookTarget.Ping(7);
                Expect("prefix keeps the host result and observes first",
                    prefixResult == 14 && PCL.HookTarget.Order.SequenceEqual(new[] { "before:7", "original:7" }));
                prefixToken?.Dispose();

                var postfixToken = attachScoped?.Invoke(api, new object[]
                {
                    "test.sample", "PCL.HookTarget", "Ping", "sample.postfix",
                    null,
                    PCL2Swap.Api.HookMode.Postfix, (Action<int>)HookHandlers.After
                }) as IDisposable;
                Expect("postfix hook installed", postfixToken != null);
                PCL.HookTarget.Order.Clear();
                var postfixResult = PCL.HookTarget.Ping(7);
                Expect("postfix keeps the host result and observes after",
                    postfixResult == 14 && PCL.HookTarget.Order.SequenceEqual(new[] { "original:7", "after:7" }));
                postfixToken?.Dispose();

                // ---- instance delegate + reference-typed parameter ----
                var instanceHandler = new InstanceHookHandler("inst");
                var echoToken = attachScoped?.Invoke(api, new object[]
                {
                    "test.sample", "PCL.HookTarget", "Echo", "sample.echo",
                    null,
                    PCL2Swap.Api.HookMode.Replace, (Func<string, string>)instanceHandler.Echo
                }) as IDisposable;
                Expect("instance delegate attaches", echoToken != null);
                var echoed = PCL.HookTarget.Echo("abc");
                Expect("instance handler with a reference parameter runs",
                    instanceHandler.Calls == 1 && echoed == "inst:abc");
                echoToken?.Dispose();
                Expect("disposed echo hook restores the host body", PCL.HookTarget.Echo("abc") == "harness:abc");

                // ---- signature-aware hooks: select one overload without guessing ----
                scopedApi?.Files.WriteText("nested/probe.txt", "scoped-file-probe", false);
                Expect("scoped IAddonFiles stays below the module root",
                    scopedApi?.Files.ReadText("nested/probe.txt") == "scoped-file-probe" &&
                    scopedApi.Files.Root.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase));

                var scopedMessages = 0;
                string scopedSource = null;
                using (scopedApi?.Messaging.Subscribe("sample.scoped", message =>
                       {
                           scopedMessages++;
                           scopedSource = message.Source;
                       }))
                {
                    scopedApi?.Messaging.Publish("sample.scoped", 18);
                }
                var sampleId = Convert.ToString(sample?.GetType().GetProperty("Id")?.GetValue(sample));
                Expect("scoped IAddonMessaging stamps the module id as source",
                    scopedMessages == 1 && scopedSource == sampleId);

                var overloads = scopedApi?.Hooks.FindTargets("PCL.HookTarget", "Overloaded");
                Expect("signature discovery sees both overloads",
                    overloads != null && overloads.Count == 2);
                var stringOverload = overloads?.FirstOrDefault(target =>
                    target.ParameterTypes.Count == 1 &&
                    target.ParameterTypes[0] == typeof(string).FullName);
                Expect("signature discovery exposes full parameter types", stringOverload != null);
                var intOverload = overloads?.FirstOrDefault(target =>
                    target.ParameterTypes.Count == 1 &&
                    target.ParameterTypes[0] == typeof(int).FullName);

                var stringHookHits = 0;
                var signatureToken = stringOverload == null
                    ? null
                    : scopedApi.Hooks.Attach(stringOverload, "sample.signature",
                        (Action<string>)(_ => stringHookHits++), PCL2Swap.Api.HookMode.Prefix);
                var intCallsBefore = PCL.HookTarget.OverloadedIntCalls;
                PCL.HookTarget.Overloaded("signature");
                PCL.HookTarget.Overloaded(4);
                Expect("signature hook only runs for the selected overload",
                    stringHookHits == 1 && PCL.HookTarget.OverloadedIntCalls == intCallsBefore + 1);
                Expect("signature target reports its installed state",
                    scopedApi?.Hooks.IsHooked(stringOverload) == true);
                signatureToken?.Dispose();
                Expect("disposing a signature hook clears its installed state",
                    scopedApi?.Hooks.IsHooked(stringOverload) == false);

                PCL2Swap.Api.SwapHookContext capturedContext = null;
                var originalResult = 0;
                var contextToken = intOverload == null
                    ? null
                    : scopedApi.Hooks.Attach(intOverload, "sample.original-context",
                        (Func<int, int>)(value =>
                        {
                            capturedContext = PCL2Swap.Api.SwapHookContext.Current;
                            originalResult = Convert.ToInt32(capturedContext?.InvokeOriginal());
                            return originalResult + 7;
                        }), PCL2Swap.Api.HookMode.Replace);
                var contextResult = PCL.HookTarget.Overloaded(4);
                Expect("Replace handlers can invoke the original method through HookContext",
                    contextResult == 19 && originalResult == 12);
                Expect("HookContext exposes target metadata and restores after the handler",
                    capturedContext != null &&
                    capturedContext.Mode == PCL2Swap.Api.HookMode.Replace &&
                    capturedContext.Target.Contains("Overloaded") &&
                    capturedContext.Arguments.Length == 1 &&
                    Convert.ToInt32(capturedContext.Arguments[0]) == 4 &&
                    PCL2Swap.Api.SwapHookContext.Current == null);
                contextToken?.Dispose();

                try
                {
                    scopedApi?.Hooks.Attach(new PCL2Swap.Api.SwapMethodTarget
                    {
                        DeclaringType = "PCL.HookTarget",
                        MethodName = "Overloaded",
                        ParameterTypes = new List<string> { "System.Guid" }
                    }, "sample.signature.missing", (Action<Guid>)(_ => { }),
                        PCL2Swap.Api.HookMode.Prefix);
                    Expect("unknown signature is rejected", false);
                }
                catch (Exception ex)
                    when (ex is InvalidOperationException ||
                          ex.InnerException is InvalidOperationException)
                {
                    var inner = ex as InvalidOperationException ?? ex.InnerException;
                    Expect("unknown signature is rejected with the available overloads",
                        inner.Message.Contains("钩子签名不存在") &&
                        inner.Message.Contains("Overloaded"));
                }

                // A handler whose shape cannot be called with the target's ABI must fail at attach
                // time (field lesson: the mismatch only surfaced later, as a bare
                // TargetParameterCountException on an unrelated launcher code path).
                try
                {
                    attachScoped?.Invoke(api, new object[]
                    {
                        "test.sample", "PCL.HookTarget", "Ping", "sample.bad",
                        null,
                        PCL2Swap.Api.HookMode.Replace, (Action)(() => { })
                    });
                    Expect("mismatched handler shape is rejected", false);
                }
                catch (System.Reflection.TargetInvocationException ex)
                    when (ex.InnerException is ArgumentException)
                {
                    Expect("mismatched handler shape is rejected with a readable error",
                        ex.InnerException.Message.Contains("参数个数不匹配"));
                }

                // ---- crash isolation: a module handler that throws must not break the host call ----
                var boomHits = 0;
                var boomToken = attachScoped?.Invoke(api, new object[]
                {
                    "test.sample", "PCL.HookTarget", "Ping", "sample.boom",
                    null,
                    PCL2Swap.Api.HookMode.Replace, (Func<int, int>)(value =>
                    {
                        boomHits++;
                        throw new InvalidOperationException("module bug");
                    })
                }) as IDisposable;
                Expect("a throwing Replace handler falls back to the host method",
                    PCL.HookTarget.Ping(3) == 6);
                Expect("the failed handler is deactivated instead of retried",
                    PCL.HookTarget.Ping(4) == 8 && boomHits == 1);

                var scopedHookType = payload.GetType("PclDlSwap.Payload.Swap.ScopedHook", throwOnError: true);
                var errorsFor = scopedHookType.GetMethod("ErrorsFor",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var moduleErrors = Convert.ToInt64(errorsFor?.Invoke(null, new object[] { "test.sample" }) ?? 0L);
                Expect("the failure is counted for the module list", moduleErrors == 1);
                boomToken?.Dispose();

                // ---- SDK ui builders: native controls when the host has them, plain WPF otherwise ----
                var ui = api?.GetType().GetProperty("Ui")?.GetValue(api);
                var uiBuildOk = false;
                var uiThread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        // WPF demands an STA thread: in the launcher the page factory already runs on
                        // the UI thread, so the test builds the elements the same way.
                        var card = ui?.GetType().GetMethod("Card")?.Invoke(ui, new object[]
                        {
                            "harness card", new System.Windows.FrameworkElement[0]
                        }) as System.Windows.FrameworkElement;
                        var uiButton = ui?.GetType().GetMethod("Button")?.Invoke(ui, new object[]
                        {
                            "harness button", (Action)(() => { }), null
                        }) as System.Windows.FrameworkElement;
                        var infoRow = ui?.GetType().GetMethod("InfoRow")?.Invoke(ui, new object[]
                        {
                            "标签", "值"
                        }) as System.Windows.FrameworkElement;
                        var checkBox = ui?.GetType().GetMethod("CheckBox")?.Invoke(ui, new object[]
                        {
                            "选项", true, (Action<bool>)(_ => { })
                        }) as System.Windows.FrameworkElement;
                        var combo = ui?.GetType().GetMethod("Combo")?.Invoke(ui, new object[]
                        {
                            "选择", new[] { "A", "B" }, 0, (Action<int>)(_ => { })
                        }) as System.Windows.FrameworkElement;
                        var banner = ui?.GetType().GetMethod("Banner")?.Invoke(ui, new object[]
                        {
                            "harness state", "semantic banner", PCL2Swap.Api.SwapHintType.Warning
                        }) as System.Windows.FrameworkElement;
                        var badge = ui?.GetType().GetMethod("Badge")?.Invoke(ui, new object[]
                        {
                            "READY", PCL2Swap.Api.SwapHintType.Success
                        }) as System.Windows.FrameworkElement;
                        var expander = ui?.GetType().GetMethod("Expander")?.Invoke(ui, new object[]
                        {
                            "advanced", true,
                            (Func<System.Windows.FrameworkElement>)(() =>
                                new System.Windows.Controls.TextBlock { Text = "content" })
                        }) as System.Windows.FrameworkElement;
                        var tabs = ui?.GetType().GetMethod("Tabs")?.Invoke(ui, new object[]
                        {
                            new[] { "one", "two" }, 1,
                            (Func<int, System.Windows.FrameworkElement>)(index =>
                                new System.Windows.Controls.TextBlock { Text = "tab:" + index })
                        }) as System.Windows.FrameworkElement;
                        var timeline = ui?.GetType().GetMethod("Timeline")?.Invoke(ui, new object[]
                        {
                            new[]
                            {
                                new PCL2Swap.Api.SwapDiagnosticEvent
                                {
                                    TimestampUtc = DateTime.UtcNow,
                                    Level = "info",
                                    Severity = PCL2Swap.Api.SwapHintType.Info,
                                    Message = "timeline event"
                                }
                            }, 180
                        }) as System.Windows.FrameworkElement;
                        var table = ui?.GetType().GetMethod("Table")?.Invoke(ui, new object[]
                        {
                            new[] { "Key", "Value" },
                            new IReadOnlyList<string>[]
                            {
                                new[] { "api", "1.8" },
                                new[] { "status", "ready" }
                            },
                            new[] { 1.0, 2.0 }, 180
                        }) as System.Windows.FrameworkElement;
                        var emptyState = ui?.GetType().GetMethod("EmptyState")?.Invoke(ui, new object[]
                        {
                            "Nothing here", "Add an item to continue.", null
                        }) as System.Windows.FrameworkElement;
                        var codeBlock = ui?.GetType().GetMethod("CodeBlock")?.Invoke(ui, new object[]
                        {
                            "{\"ok\":true}", 120
                        }) as System.Windows.FrameworkElement;
                        var scroller = ui?.GetType().GetMethod("Scroll")?.Invoke(ui, new object[]
                        {
                            new System.Windows.Controls.StackPanel()
                        }) as System.Windows.FrameworkElement;
                        uiBuildOk = card != null && uiButton != null && infoRow != null &&
                                    checkBox != null && combo != null && banner != null &&
                                    badge != null && expander != null && tabs != null && timeline != null &&
                                    table != null && emptyState != null && codeBlock != null &&
                                    scroller != null;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        Console.WriteLine("[addon] ui builder threw " + inner.GetType().Name + ": " + inner.Message);
                    }
                });
                uiThread.SetApartmentState(System.Threading.ApartmentState.STA);
                uiThread.Start();
                uiThread.Join();
                Expect("ui builders return renderable elements",
                    uiBuildOk);

                var store = new MemoryData();
                PCL2Swap.Api.AddonSettings.SetBool(store, "flag", true);
                PCL2Swap.Api.AddonSettings.SetInt(store, "count", 7);
                Expect("AddonSettings round-trips values",
                    PCL2Swap.Api.AddonSettings.GetBool(store, "flag") &&
                    PCL2Swap.Api.AddonSettings.GetInt(store, "count") == 7 &&
                    PCL2Swap.Api.AddonSettings.GetBool(store, "missing", true) &&
                    store.Saved);

                // ---- module manager: hot install / uninstall / shape scope ------------------------
                // Runs last: the sample assembly is used for the installed copies too, and its
                // OnDisable would otherwise tear down the subscriptions the bus tests rely on.
                var install = manager?.GetType().GetMethod("Install");
                var uninstall = manager?.GetType().GetMethod("Uninstall");
                var cleanup = manager?.GetType().GetMethod("CleanupPendingRemovals");
                Expect("module manager exposes Install / Uninstall / CleanupPendingRemovals",
                    install != null && uninstall != null && cleanup != null);

                string IdOf(object record) =>
                    Convert.ToString(record?.GetType().GetProperty("Id")?.GetValue(record));
                string StatusOf(object record) =>
                    Convert.ToString(record?.GetType().GetProperty("Status")?.GetValue(record));
                List<string> RecordIds() =>
                    (manager?.GetType().GetProperty("Records")?.GetValue(manager) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(IdOf).ToList() ?? new List<string>();

                // Trusted key material has to exist before the first package is packed: the install
                // preview, the audit trail and the repack warning all read the trust chain.
                var trustedDir = Path.Combine(dataRoot, "trusted-keys");
                Directory.CreateDirectory(trustedDir);
                var trustedKey = RSA.Create();
                trustedKey.KeySize = 2048;
                File.WriteAllText(Path.Combine(trustedDir, "harness.xml"), trustedKey.ToXmlString(false),
                    new UTF8Encoding(false));

                var external = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapInstall-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                // Unsigned, but with a payload inventory: the "legacy" shape for the trust tests while
                // still giving the installed record a digest the repack warning can compare.
                PackSignedAddon(external,
                    "{\"schema\":1,\"id\":\"test.installed\",\"name\":\"Installed Sample\"," +
                    "\"version\":\"2.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                    "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\",\"author\":\"harness\"," +
                    "\"description\":\"installed by the manager test\"," +
                    "\"permissions\":[\"settings\",\"hooks\",\"files\",\"messaging\",\"services\",\"theme\",\"window\"]," +
                    "\"scopes\":[" +
                    "{\"id\":\"installed.core\",\"name\":\"核心\",\"nameEn\":\"Core\"," +
                    "\"description\":\"核心功能\",\"descriptionEn\":\"Core features\",\"defaultEnabled\":true}," +
                    "{\"id\":\"installed.optional\",\"name\":\"可选\",\"nameEn\":\"Optional\"," +
                    "\"description\":\"可选功能\",\"descriptionEn\":\"Optional features\",\"defaultEnabled\":false}]}",
                    null);

                // ---- install preview: inspect without touching the store, then describe it ----
                var inspectMethod = manager?.GetType().GetMethod("Inspect");
                var describeMethod = manager?.GetType().GetMethod("DescribeForReview");
                Expect("the manager can inspect a package before installing it",
                    inspectMethod != null && describeMethod != null);

                object inspected = null;
                try { inspected = inspectMethod?.Invoke(manager, new object[] { external }); }
                catch (Exception ex) { Console.WriteLine("[addon] inspect threw: " + SafeException(ex)); }
                Expect("inspect reads the manifest without touching the addon store",
                    Convert.ToString(inspected?.GetType().GetProperty("Id")?.GetValue(inspected)) == "test.installed");

                var reviewMethod = manager?.GetType().GetMethod("Review");
                var legacyReview = reviewMethod?.Invoke(manager, new object[] { external });
                var previewText = Convert.ToString(describeMethod?.Invoke(manager,
                    new object[]
                    {
                        inspected, external,
                        legacyReview?.GetType().GetProperty("Trust")?.GetValue(legacyReview)
                    })) ?? string.Empty;
                Expect("the install preview names the module, its API range and its hash",
                    previewText.Contains("test.installed") && previewText.Contains("API") &&
                    previewText.Contains("SHA-256"));
                Expect("the install preview repeats the declared permissions",
                    previewText.Contains("权限声明") && previewText.Contains("settings") &&
                    previewText.Contains("hooks") && previewText.Contains("files") &&
                    previewText.Contains("messaging"));
                Expect("the install preview shows feature scopes and their defaults",
                    previewText.Contains("功能作用域") && previewText.Contains("installed.core") &&
                    previewText.Contains("installed.optional") && previewText.Contains("默认开") &&
                    previewText.Contains("默认关"));

                // ---- trust chain: signature + payload hashes + version-level warnings -------------
                object TrustOfReview(object review) =>
                    review?.GetType().GetProperty("Trust")?.GetValue(review);
                string TrustKindOf(object review)
                {
                    var trust = TrustOfReview(review);
                    return Convert.ToString(trust?.GetType().GetProperty("Kind")?.GetValue(trust));
                }

                Expect("a legacy package reports Unsigned", TrustKindOf(legacyReview) == "Unsigned");
                Expect("the preview prints the trust state", previewText.Contains("信任状态"));

                var signedOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapSigned-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackSignedAddon(signedOut,
                    "{\"schema\":1,\"id\":\"test.signed\",\"name\":\"Signed Addon\",\"version\":\"1.0.0\"," +
                    "\"apiVersion\":\">=1.0 <2.0\",\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"}", trustedKey);

                var signedReview = reviewMethod?.Invoke(manager, new object[] { signedOut });
                Expect("a package signed by a trusted key reports Trusted",
                    TrustKindOf(signedReview) == "Trusted");

                var signedRecord = install?.Invoke(manager, new object[] { signedOut, true });
                var signedRecordType = signedRecord?.GetType();
                Expect("the install records the signature state",
                    Convert.ToString(signedRecordType?.GetProperty("TrustKind")?.GetValue(signedRecord)) == "Trusted");
                Expect("the install records a payload digest",
                    (Convert.ToString(signedRecordType?.GetProperty("PayloadDigest")?.GetValue(signedRecord)) ??
                     string.Empty).Length == 64);

                var unknownKeyOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapOtherKey-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                using (var otherKey = RSA.Create())
                {
                    otherKey.KeySize = 2048;
                    PackSignedAddon(unknownKeyOut,
                        "{\"schema\":1,\"id\":\"test.other-key\",\"name\":\"Other Key\",\"version\":\"1.0.0\"," +
                        "\"apiVersion\":\">=1.0 <2.0\",\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"}", otherKey);
                }
                Expect("a package signed by an unknown key is reported as UnknownKey",
                    TrustKindOf(reviewMethod?.Invoke(manager, new object[] { unknownKeyOut })) == "UnknownKey");
                Expect("an unknown key is advisory, not blocking",
                    install?.Invoke(manager, new object[] { unknownKeyOut, true }) != null);

                var tamperedOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapTampered-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                File.Copy(signedOut, tamperedOut, true);
                TamperDllEntry(tamperedOut);
                Expect("a modified payload reports TamperedFiles",
                    TrustKindOf(reviewMethod?.Invoke(manager, new object[] { tamperedOut })) == "TamperedFiles");

                var refusedTamper = false;
                try { install?.Invoke(manager, new object[] { tamperedOut, true }); }
                catch (TargetInvocationException) { refusedTamper = true; }
                Expect("a tampered package is refused at install", refusedTamper);

                // Zip-slip: an entry escaping the module folder must never be written.
                var slipOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapSlip-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                using (var zip = ZipFile.Open(slipOut, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "manifest.json", Encoding.UTF8.GetBytes(
                        "{\"schema\":1,\"id\":\"test.slip\",\"name\":\"Slip\",\"version\":\"1.0.0\"," +
                        "\"apiVersion\":\">=1.0 <2.0\",\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"}"));
                    WriteZipEntry(zip, "../evil.txt", Encoding.UTF8.GetBytes("escaped"));
                    WriteZipEntry(zip, "../1.0.0-evil/prefix-evil.txt",
                        Encoding.UTF8.GetBytes("escaped by a sibling-prefix collision"));
                    zip.CreateEntryFromFile(
                        typeof(PCL2Swap.SampleAddon.SampleAddon).Assembly.Location, "SampleAddon.dll");
                }
                install?.Invoke(manager, new object[] { slipOut, true });
                var escapedPaths = new[]
                {
                    Path.Combine(addonRoot, "test.slip", "evil.txt"),
                    Path.Combine(addonRoot, "test.slip", "1.0.0-evil", "prefix-evil.txt"),
                    Path.Combine(addonRoot, "evil.txt"),
                    Path.Combine(Path.GetDirectoryName(addonRoot) ?? addonRoot, "evil.txt")
                };
                Expect("zip-slip entries never escape the module folder",
                    !escapedPaths.Any(File.Exists));

                object installed = null;
                try
                {
                    installed = install?.Invoke(manager, new object[] { external, true });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[addon] install threw: " + SafeException(ex));
                }
                Expect("install registers and starts the new module",
                    IdOf(installed) == "test.installed" && StatusOf(installed) == "enabled");
                Expect("install archives the package into the addon store",
                    File.Exists(Path.Combine(addonRoot, "test.installed-2.0.0.swapaddon")));
                Expect("install keeps the user's original package untouched", File.Exists(external));
                Expect("installed module shows up in the list", RecordIds().Contains("test.installed"));

                // ---- user governance: requested permissions can be revoked and restored ----
                var installedInfo = catalog?.Find("test.installed");
                Expect("a new module starts with all requested permissions granted",
                    installedInfo != null &&
                    installedInfo.RequestedPermissions.Contains("settings") &&
                    installedInfo.RequestedPermissions.Contains("hooks") &&
                    installedInfo.RequestedPermissions.Contains("files") &&
                    installedInfo.RequestedPermissions.Contains("messaging") &&
                    installedInfo.RequestedPermissions.Contains("theme") &&
                    installedInfo.RequestedPermissions.Contains("window") &&
                    installedInfo.GrantedPermissions.Contains("settings") &&
                    installedInfo.GrantedPermissions.Contains("hooks") &&
                    installedInfo.GrantedPermissions.Contains("files") &&
                    installedInfo.GrantedPermissions.Contains("messaging") &&
                    installedInfo.GrantedPermissions.Contains("theme") &&
                    installedInfo.GrantedPermissions.Contains("window") &&
                    installedInfo.DeniedPermissions.Count == 0);
                Expect("installed module exposes localized scope metadata and defaults",
                    installedInfo?.Scopes.Count == 2 &&
                    installedInfo.Scopes[0].NameEnglish == "Core" &&
                    installedInfo.Scopes[0].DescriptionEnglish == "Core features" &&
                    installedInfo.Scopes[0].Enabled &&
                    !installedInfo.Scopes[1].Enabled);
                Expect("installed module publishes its service and catalog metadata",
                    installedInfo?.Services.Count == 1 &&
                    installedInfo.Services[0].Provider == "test.installed" &&
                    installedInfo.Services[0].ServiceId == "services/test.installed/echo");
                var governedScopedApi = createScopedApi?.Invoke(manager,
                    new[] { installed }) as PCL2Swap.Api.ISwapApi;
                Expect("services are discoverable across module boundaries",
                    governedScopedApi?.Services.TryGet<object>("services/test.sample/echo", out _) == true);
                var duplicateServiceRejected = false;
                try
                {
                    governedScopedApi?.Services.Register(
                        "services/test.sample/echo", new object(), "1.0", "duplicate");
                }
                catch (InvalidOperationException)
                {
                    duplicateServiceRejected = true;
                }
                Expect("duplicate service ids are rejected", duplicateServiceRejected);
                var governedHookHits = 0;
                var governedHook = governedScopedApi?.Hooks.Attach(
                    new PCL2Swap.Api.SwapMethodTarget
                    {
                        DeclaringType = "PCL.HookTarget",
                        MethodName = "Ping",
                        ParameterTypes = new List<string> { "System.Int32" }
                    }, "governed.permission-hook",
                    (Func<int, int>)(value =>
                    {
                        governedHookHits++;
                        return value + 1000;
                    }), PCL2Swap.Api.HookMode.Replace);
                Expect("a granted hook permission runs the installed handler",
                    governedHookHits == 0 && PCL.HookTarget.Ping(2) == 1002);
                var installedScopeEvents = 0;
                EventHandler installedScopeHandler = (sender, args) => installedScopeEvents++;
                if (governedScopedApi != null) governedScopedApi.Scopes.Changed += installedScopeHandler;
                var sampleCrossScopeEvents = 0;
                EventHandler sampleCrossScopeHandler = (sender, args) => sampleCrossScopeEvents++;
                if (scopedApi != null) scopedApi.Scopes.Changed += sampleCrossScopeHandler;
                var installedScopeChanged = Convert.ToBoolean(setScope?.Invoke(manager,
                    new object[] { "test.installed", "installed.optional", true }) ?? false);
                Expect("installed module scopes are user-editable and raise a scoped event",
                    installedScopeChanged && installedScopeEvents == 1 &&
                    governedScopedApi?.Scopes.IsEnabled("installed.optional") == true);
                setScope?.Invoke(manager, new object[] { "test.installed", "installed.core", false });
                setScope?.Invoke(manager, new object[] { "test.installed", "installed.core", true });
                Expect("scope events are isolated to the module that changed",
                    installedScopeEvents == 3 && sampleCrossScopeEvents == 0);
                if (governedScopedApi != null) governedScopedApi.Scopes.Changed -= installedScopeHandler;
                if (scopedApi != null) scopedApi.Scopes.Changed -= sampleCrossScopeHandler;
                var governanceEventsBefore = catalogEvents;
                var setPermission = manager?.GetType().GetMethod("SetPermission");
                var revoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "settings", false }) ?? false);
                var revokedInfo = catalog?.Find("test.installed");
                Expect("revoking a requested permission updates the catalog",
                    revoked && revokedInfo != null &&
                    !revokedInfo.GrantedPermissions.Contains("settings") &&
                    revokedInfo.DeniedPermissions.Contains("settings") &&
                    catalogEvents == governanceEventsBefore + 1);
                try
                {
                    var deniedSettings = governedScopedApi?.Pcl.Settings;
                    Expect("a revoked permission is enforced at the scoped API boundary", false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Expect("a revoked permission is enforced at the scoped API boundary",
                        ex.Message.Contains("未获得权限"));
                }
                var filesRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "files", false }) ?? false);
                var filesDenied = false;
                try { governedScopedApi?.Files.ReadText("missing.txt"); }
                catch (UnauthorizedAccessException) { filesDenied = true; }
                Expect("revoking files immediately gates IAddonFiles",
                    filesRevoked && filesDenied);
                var messagingRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "messaging", false }) ?? false);
                var messagingDenied = false;
                try { governedScopedApi?.Messaging.Publish("denied.topic", "x"); }
                catch (UnauthorizedAccessException) { messagingDenied = true; }
                Expect("revoking messaging immediately gates IAddonMessaging",
                    messagingRevoked && messagingDenied);
                var servicesRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "services", false }) ?? false);
                var servicesDenied = false;
                try { governedScopedApi?.Services.Get("services/test.installed/echo", typeof(object)); }
                catch (UnauthorizedAccessException) { servicesDenied = true; }
                Expect("revoking services immediately gates IAddonServices",
                    servicesRevoked && servicesDenied);
                var hooksRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "hooks", false }) ?? false);
                var hooksBeforeRevocation = governedHookHits;
                var ungatedHookResult = PCL.HookTarget.Ping(2);
                Expect("revoking hook permission immediately disables installed handlers",
                    hooksRevoked && governedHookHits == hooksBeforeRevocation &&
                    ungatedHookResult == 4);
                var themeRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "theme", false }) ?? false);
                var windowRevoked = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "window", false }) ?? false);
                var themeDenied = false;
                var windowDenied = false;
                try { _ = governedScopedApi?.Pcl.Appearance; }
                catch (UnauthorizedAccessException) { themeDenied = true; }
                try { _ = governedScopedApi?.Pcl.Window; }
                catch (UnauthorizedAccessException) { windowDenied = true; }
                Expect("revoking theme/window permissions gates both host-control families",
                    themeRevoked && windowRevoked && themeDenied && windowDenied);
                var governedModules = File.ReadAllText(modulesFile);
                Expect("permission grants persist independently of the enabled flag",
                    governedModules.Contains("\"test.installed\"") &&
                    governedModules.Contains("\"permissions\":[]"));
                var managerType = manager?.GetType();
                var managerCtor = managerType?.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(ctor => ctor.GetParameters().Length == 1);
                var reloadedManager = managerCtor?.Invoke(new object[] { api });
                managerType?.GetMethod("RegisterBuiltIns")?.Invoke(reloadedManager, null);
                managerType?.GetMethod("LoadFromDisk")?.Invoke(reloadedManager, null);
                managerType?.GetMethod("ApplySavedState",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(reloadedManager, null);
                var reloadedInstalled = (reloadedManager == null ? null :
                        managerType.GetMethod("ModuleSnapshot",
                                BindingFlags.NonPublic | BindingFlags.Instance,
                                null, Type.EmptyTypes, null)
                            ?.Invoke(reloadedManager, null)
                        as IEnumerable<PCL2Swap.Api.SwapModuleInfo>)
                    ?.FirstOrDefault(module => module.Id == "test.installed");
                Expect("permission revocation survives module-state reload",
                    reloadedInstalled != null &&
                    reloadedInstalled.DeniedPermissions.Contains("settings") &&
                    reloadedInstalled.DeniedPermissions.Contains("hooks") &&
                    reloadedInstalled.DeniedPermissions.Contains("theme") &&
                    reloadedInstalled.DeniedPermissions.Contains("window"));
                Expect("feature-scope selection survives module-state reload",
                    reloadedInstalled != null &&
                    reloadedInstalled.Scopes.First(scope => scope.Id == "installed.core").Enabled &&
                    reloadedInstalled.Scopes.First(scope => scope.Id == "installed.optional").Enabled);
                var regranted = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "settings", true }) ?? false);
                var hooksRegranted = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "hooks", true }) ?? false);
                var servicesRegranted = Convert.ToBoolean(setPermission?.Invoke(manager,
                    new object[] { "test.installed", "services", true }) ?? false);
                Expect("a revoked permission can be granted again",
                    regranted && hooksRegranted && servicesRegranted && catalog?.Find("test.installed")
                        ?.GrantedPermissions.Contains("settings") == true &&
                    catalog?.Find("test.installed")?.GrantedPermissions.Contains("hooks") == true);
                Expect("regranting immediately restores scoped access",
                    governedScopedApi?.Pcl.Settings != null);
                Expect("regranting hook permission immediately restores installed handlers",
                    PCL.HookTarget.Ping(2) == 1002);
                Expect("regranting services immediately restores service access",
                    governedScopedApi?.Services.Get("services/test.installed/echo", typeof(object)) != null);
                governedHook?.Dispose();

                var permissionUpdatePath = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapPermissionUpdate-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackSignedAddon(permissionUpdatePath,
                    "{\"schema\":1,\"id\":\"test.permission-update\",\"name\":\"Permission Update\"," +
                    "\"version\":\"1.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                    "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\",\"permissions\":[\"settings\"]}",
                    trustedKey);
                var permissionFirst = install?.Invoke(manager,
                    new object[] { permissionUpdatePath, true });
                setPermission?.Invoke(manager,
                    new object[] { "test.permission-update", "settings", false });
                var permissionUpdated = install?.Invoke(manager,
                    new object[] { permissionUpdatePath, true });
                Expect("hot module update preserves user permission revocations",
                    permissionFirst != null && permissionUpdated != null &&
                    catalog?.Find("test.permission-update")
                        ?.DeniedPermissions.Contains("settings") == true);
                uninstall?.Invoke(manager, new object[] { "test.permission-update" });

                var scopeUpdatePath = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapScopeUpdate-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackSignedAddon(scopeUpdatePath,
                    "{\"schema\":1,\"id\":\"test.scope-update\",\"name\":\"Scope Update\"," +
                    "\"version\":\"1.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                    "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\",\"scopes\":[" +
                    "{\"id\":\"scope-update.core\",\"name\":\"核心\",\"nameEn\":\"Core\",\"defaultEnabled\":true}," +
                    "{\"id\":\"scope-update.optional\",\"name\":\"可选\",\"nameEn\":\"Optional\",\"defaultEnabled\":false}]}",
                    trustedKey);
                var scopeFirst = install?.Invoke(manager, new object[] { scopeUpdatePath, true });
                setScope?.Invoke(manager,
                    new object[] { "test.scope-update", "scope-update.optional", true });
                var scopeUpdated = install?.Invoke(manager, new object[] { scopeUpdatePath, true });
                Expect("hot module update preserves user feature-scope choices",
                    scopeFirst != null && scopeUpdated != null &&
                    catalog?.Find("test.scope-update")
                        ?.Scopes.First(scope => scope.Id == "scope-update.optional").Enabled == true);
                uninstall?.Invoke(manager, new object[] { "test.scope-update" });

                // Version-level warnings only make sense once the same id is installed.
                var downgradeOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapDowngrade-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackSignedAddon(downgradeOut,
                    "{\"schema\":1,\"id\":\"test.installed\",\"name\":\"Installed Sample\",\"version\":\"1.0.0\"," +
                    "\"apiVersion\":\">=1.0 <2.0\",\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"}", trustedKey);
                var downgradeReview = reviewMethod?.Invoke(manager, new object[] { downgradeOut });
                var downgradeText = Convert.ToString(describeMethod?.Invoke(manager, new object[]
                {
                    downgradeReview?.GetType().GetProperty("Manifest")?.GetValue(downgradeReview),
                    downgradeOut, TrustOfReview(downgradeReview)
                })) ?? string.Empty;
                Expect("the preview warns about a downgrade", downgradeText.Contains("降级安装"));

                var repackOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapRepack-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackSignedAddon(repackOut,
                    "{\"schema\":1,\"id\":\"test.installed\",\"name\":\"Installed Sample\",\"version\":\"2.0.0\"," +
                    "\"apiVersion\":\">=1.0 <2.0\",\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\"}", trustedKey,
                    typeof(PCL.ModMain).Assembly.Location);
                var repackReview = reviewMethod?.Invoke(manager, new object[] { repackOut });
                var repackText = Convert.ToString(describeMethod?.Invoke(manager, new object[]
                {
                    repackReview?.GetType().GetProperty("Manifest")?.GetValue(repackReview),
                    repackOut, TrustOfReview(repackReview)
                })) ?? string.Empty;
                Expect("the preview warns when the same version ships different content",
                    repackText.Contains("包内容与已安装的不一致"));

                // Per-module log: the module's own lines and the hooks the platform attaches for it.
                var moduleLogPath = Convert.ToString(manager?.GetType().GetMethod("GetModuleLogPath")
                    ?.Invoke(manager, new object[] { "test.installed" }));
                var moduleLog = !string.IsNullOrEmpty(moduleLogPath) && File.Exists(moduleLogPath)
                    ? File.ReadAllText(moduleLogPath)
                    : string.Empty;
                Expect("each module writes its own log file",
                    moduleLog.Length > 0 && moduleLog.Contains("[info]"));
                Expect("the module log records the hooks installed for it", moduleLog.Contains("挂钩"));
                var sampleLogPath = Convert.ToString(manager?.GetType().GetMethod("GetModuleLogPath")
                    ?.Invoke(manager, new object[] { "test.sample" }));
                var sampleLog = !string.IsNullOrEmpty(sampleLogPath) && File.Exists(sampleLogPath)
                    ? File.ReadAllText(sampleLogPath)
                    : string.Empty;
                Expect("the module log records the handler failure", sampleLog.Contains("抛错"));

                var broken = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapBroken-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackAddon(broken, null, null, null, null);
                var refused = false;
                try { install?.Invoke(manager, new object[] { broken, true }); }
                catch (TargetInvocationException) { refused = true; }
                Expect("install refuses a package without manifest", refused);

                var scopedOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapScoped-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackAddon(scopedOut, "test.patched-only", "Patched Only", "1.0.0",
                    "{\"schema\":1,\"id\":\"test.patched-only\",\"name\":\"Patched Only\"," +
                    "\"version\":\"1.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                    "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\",\"shapes\":[\"patched\"]}");
                var scopedRecord = install?.Invoke(manager, new object[] { scopedOut, true });
                Expect("a module scoped to another shape is skipped, not failed",
                    StatusOf(scopedRecord) == "skipped-shape");

                // ---- host version scope: the single-app equivalent of an LSPosed scope ----
                var hostVersion = typeof(PCL.ModMain).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
                var hostScopeMethod = manager?.GetType().GetMethod("HostVersionMatches",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Expect("host version scope matches the running host",
                    Convert.ToBoolean(hostScopeMethod?.Invoke(null,
                        new object[] { new List<string> { ">=" + hostVersion }, hostVersion }) ?? false));
                Expect("host version scope rejects a build outside the range",
                    !Convert.ToBoolean(hostScopeMethod?.Invoke(null,
                        new object[] { new List<string> { ">=99.0" }, hostVersion }) ?? true));
                Expect("the wildcard scope accepts any host",
                    Convert.ToBoolean(hostScopeMethod?.Invoke(null,
                        new object[] { new List<string> { "*" }, hostVersion }) ?? false));

                var hostOut = Path.Combine(Path.GetTempPath(),
                    "PCL2SwapHostScope-" + Guid.NewGuid().ToString("N") + ".swapaddon");
                PackAddon(hostOut, "test.other-host", "Other Host", "1.0.0",
                    "{\"schema\":1,\"id\":\"test.other-host\",\"name\":\"Other Host\"," +
                    "\"version\":\"1.0.0\",\"apiVersion\":\">=1.0 <2.0\"," +
                    "\"entry\":\"PCL2Swap.SampleAddon.SampleAddon\",\"hostVersions\":[\">=99.0\"]}");
                var otherHost = install?.Invoke(manager, new object[] { hostOut, true });
                Expect("a module scoped to another PCL build is skipped, not failed",
                    StatusOf(otherHost) == "skipped-host");

                // ---- module detail view: audit trail, log tail, selection, reload ----
                var installedRecordType = installed?.GetType();
                Expect("install records the audit trail (source, hash, time)",
                    Convert.ToString(installedRecordType?.GetProperty("SourcePath")?.GetValue(installed)) == external &&
                    (Convert.ToString(installedRecordType?.GetProperty("Sha256")?.GetValue(installed)) ?? string.Empty).Length == 64 &&
                    installedRecordType?.GetProperty("InstalledAtUtc")?.GetValue(installed) != null);

                var logTail = Convert.ToString(manager?.GetType().GetMethod("LogTail")
                    ?.Invoke(manager, new object[] { "test.installed", 5 })) ?? string.Empty;
                Expect("the detail view can read a module's log tail", logTail.Contains("[info]"));

                var configPath = Convert.ToString(manager?.GetType().GetMethod("GetModuleConfigPath")
                    ?.Invoke(manager, new object[] { "test.installed" }));
                Expect("the detail view knows where the module config lives",
                    !string.IsNullOrEmpty(configPath) &&
                    configPath.EndsWith("config.json", StringComparison.OrdinalIgnoreCase));

                manager?.GetType().GetProperty("SelectedAddonId")?.SetValue(manager, "test.installed");
                var selectedRecord = manager?.GetType().GetProperty("Selected")?.GetValue(manager);
                Expect("the manager remembers which module detail is open",
                    Convert.ToString(selectedRecord?.GetType().GetProperty("Id")?.GetValue(selectedRecord)) ==
                    "test.installed");
                manager?.GetType().GetProperty("SelectedAddonId")?.SetValue(manager, null);

                var hookChainType = payload.GetType("PclDlSwap.Payload.Swap.ScopedHook", throwOnError: true);
                var countFor = hookChainType.GetMethod("CountFor", BindingFlags.NonPublic | BindingFlags.Static);
                // The harness host lacks ModBase.ToUnixTimestampUtc, so the sample module cannot
                // attach its own hook here - register one for it to give the reload something to clean.
                attachScoped?.Invoke(api, new object[]
                {
                    "test.installed", "PCL.HookTarget", "Ping", "detail.ping",
                    "installed.core",
                    PCL2Swap.Api.HookMode.Replace, (Func<int, int>)(value => value * 3)
                });
                var hooksBeforeReload = Convert.ToInt32(countFor?.Invoke(null, new object[] { "test.installed" }) ?? -1);
                var reloadMethod = manager?.GetType().GetMethod("Reload");
                var reloaded = Convert.ToBoolean(reloadMethod?.Invoke(manager, new object[] { "test.installed" }) ?? false);
                var hooksAfterReload = Convert.ToInt32(countFor?.Invoke(null, new object[] { "test.installed" }) ?? -1);
                Expect("reload cycles the module and leaves it enabled",
                    reloaded && Convert.ToBoolean(isEnabled?.Invoke(manager, new object[] { "test.installed" })));
                Expect("reload drops the previous generation's hooks",
                    hooksBeforeReload > 0 && hooksAfterReload == 0);
                Expect("built-in modules refuse to reload",
                    !Convert.ToBoolean(reloadMethod?.Invoke(manager, new object[] { "swap.ota-protector" }) ?? true));

                var removedOk = Convert.ToBoolean(uninstall?.Invoke(manager, new object[] { "test.installed" }));
                Expect("uninstall drops the module from the list",
                    removedOk && !RecordIds().Contains("test.installed"));
                Expect("uninstall deletes the archived package",
                    !File.Exists(Path.Combine(addonRoot, "test.installed-2.0.0.swapaddon")));
                var unpackedDir = Path.Combine(addonRoot, "test.installed", "2.0.0");
                var pendingDir = Path.Combine(addonRoot, ".pending");
                var queued = Directory.Exists(pendingDir) &&
                             Directory.GetFiles(pendingDir, "*.txt").Length > 0;
                Expect("uninstall removes the module files or queues the locked ones",
                    !Directory.Exists(unpackedDir) || queued);
                Expect("uninstall removes the module's own log",
                    !File.Exists(moduleLogPath) || queued);
                Expect("uninstall drops the module's hook registrations",
                    Convert.ToInt32(countFor?.Invoke(null, new object[] { "test.installed" }) ?? -1) == 0);
                Expect("the user's original package survived the uninstall", File.Exists(external));
                Expect("built-in modules refuse to uninstall",
                    !Convert.ToBoolean(uninstall?.Invoke(manager, new object[] { "swap.ota-protector" })));

                foreach (var temp in new[]
                         {
                             external, broken, scopedOut, hostOut, signedOut, unknownKeyOut, tamperedOut,
                             downgradeOut, repackOut
                         })
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
            finally
            {
                // Leave the real addon directory exactly as found.
                try { if (File.Exists(archive)) File.Delete(archive); } catch { }
                try { if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true); } catch { }
                try { if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true); } catch { }
                Environment.SetEnvironmentVariable("PCL2SWAP_DATA", null);
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

            Environment.SetEnvironmentVariable("PCL2SWAP_DATA",
                Path.Combine(Path.GetTempPath(), "Pcl2SwapUiTest-" + Guid.NewGuid().ToString("N")));

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

            // API 1.3 front-end builders are WPF objects; exercise them on a real STA dispatcher
            // so a bad template/property path cannot hide behind compile-time-only checks.
            var frontendOk = false;
            var frontendError = (Exception)null;
            var frontendDetail = string.Empty;
            var frontendDone = new ManualResetEventSlim(false);
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var swapUi = payload.GetType("PclDlSwap.Payload.Swap.Views.SwapUi",
                        throwOnError: true);
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    var radio = swapUi.GetMethod("RadioGroup", flags)?.Invoke(null, new object[]
                    {
                        "choice", new[] { "one", "two" }, 0, (Action<int>)(_ => { })
                    });
                    var number = swapUi.GetMethod("NumberInput", flags)?.Invoke(null, new object[]
                    {
                        "number", 1.0, 0.0, 10.0, 1.0, (Action<double>)(_ => { })
                    });
                    var password = swapUi.GetMethod("PasswordInput", flags)?.Invoke(null, new object[]
                    {
                        "secret", string.Empty, (Action<string>)(_ => { })
                    });
                    var multiline = swapUi.GetMethod("MultilineTextInput", flags)?.Invoke(null, new object[]
                    {
                        "notes", string.Empty, 70, (Action<string>)(_ => { })
                    });
                    var progress = swapUi.GetMethod("Progress", flags)?.Invoke(null, new object[]
                    {
                        "progress", 0.25, false
                    });
                    var progressElement = progress?.GetType().GetProperty("Element")?.GetValue(progress);
                    var banner = swapUi.GetMethod("Banner", flags)?.Invoke(null, new object[]
                    {
                        "state", "semantic banner", PCL2Swap.Api.SwapHintType.Success
                    });
                    var badge = swapUi.GetMethod("Badge", flags)?.Invoke(null, new object[]
                    {
                        "READY", PCL2Swap.Api.SwapHintType.Success
                    });
                    var expander = swapUi.GetMethod("Expander", flags)?.Invoke(null, new object[]
                    {
                        "advanced", true,
                        (Func<System.Windows.FrameworkElement>)(() =>
                            new System.Windows.Controls.TextBlock { Text = "content" })
                    });
                    var tabs = swapUi.GetMethod("Tabs", flags)?.Invoke(null, new object[]
                    {
                        new[] { "one", "two" }, 1,
                        (Func<int, System.Windows.FrameworkElement>)(index =>
                            new System.Windows.Controls.TextBlock { Text = "tab:" + index })
                    });
                    var timeline = swapUi.GetMethod("Timeline", flags)?.Invoke(null, new object[]
                    {
                        new[]
                        {
                            new PCL2Swap.Api.SwapDiagnosticEvent
                            {
                                TimestampUtc = DateTime.UtcNow,
                                Level = "warn",
                                Severity = PCL2Swap.Api.SwapHintType.Warning,
                                ModuleId = "test.module",
                                Message = "timeline event"
                            }
                        }, 180
                    });
                    var table = swapUi.GetMethod("Table", flags)?.Invoke(null, new object[]
                    {
                        new[] { "Key", "Value" },
                        new IReadOnlyList<string>[]
                        {
                            new[] { "api", "1.8" },
                            new[] { "status", "ready" }
                        },
                        new[] { 1.0, 2.0 }, 180
                    });
                    var search = swapUi.GetMethod("SearchBox", flags)?.Invoke(null, new object[]
                    {
                        "Search", "Type a keyword", string.Empty, (Action<string>)(_ => { })
                    });
                    var segmented = swapUi.GetMethod("SegmentedControl", flags)?.Invoke(null, new object[]
                    {
                        "Density", new[] { "Compact", "Standard", "Comfortable" }, 1,
                        (Action<int>)(_ => { })
                    });
                    var color = swapUi.GetMethod("ColorInput", flags)?.Invoke(null, new object[]
                    {
                        "Accent", "#3A7BD5", (Action<string>)(_ => { })
                    });
                    var selectList = swapUi.GetMethod("SelectList", flags)?.Invoke(null, new object[]
                    {
                        "Preset", new[] { "Launch", "Download", "Settings" }, 0,
                        (Action<int>)(_ => { })
                    });
                    var emptyState = swapUi.GetMethod("EmptyState", flags)?.Invoke(null, new object[]
                    {
                        "Nothing here", "Add an item to continue.", null
                    });
                    var codeBlock = swapUi.GetMethod("CodeBlock", flags)?.Invoke(null, new object[]
                    {
                        "{\"ok\":true}", 120
                    });
                    var scroller = swapUi.GetMethod("Scroll", flags)?.Invoke(null, new object[]
                    {
                        new System.Windows.Controls.StackPanel()
                    });

                    var i18nType = payload.GetType("PclDlSwap.Payload.Swap.SwapI18n",
                        throwOnError: true);
                    var i18n = i18nType.GetProperty("Current",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    var oldLanguage = i18n?.GetType().GetProperty("Language")?.GetValue(i18n);
                    var translated = false;
                    try
                    {
                        i18nType.GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.Instance)
                            ?.Invoke(i18n, new object[] { PCL2Swap.Api.SwapLanguage.English });
                        var probe = new System.Windows.Controls.TextBlock();
                        var probeRun = new System.Windows.Documents.Run { Text = "联机模块" };
                        probe.Inlines.Add(probeRun);
                        var direct = Convert.ToString(i18nType.GetMethod("T",
                                BindingFlags.Public | BindingFlags.Instance,
                                null, new[] { typeof(string) }, null)
                            ?.Invoke(i18n, new object[] { "联机模块" }));
                        var translator = payload.GetType("PclDlSwap.Payload.Swap.HostTranslator",
                            throwOnError: true);
                        var internalFlags = BindingFlags.NonPublic | BindingFlags.Static;
                        var assign = (Action<string>)(value => probeRun.Text = value);
                        var applyHits = Convert.ToInt32(translator
                            .GetMethod("TranslateValue", internalFlags)
                            ?.Invoke(null, new object[] { probeRun, "text", probeRun.Text, assign, i18n }) ?? 0);
                        var afterApply = probeRun.Text;
                        // A disconnected StackPanel is not guaranteed to expose its visual
                        // children until it has entered a presentation source. The direct
                        // dictionary hit proves the expanded table; the host translator itself is
                        // exercised by the real-host self-test.
                        var restoreHits = Convert.ToInt32(translator
                            .GetMethod("RestoreValue", internalFlags)
                            ?.Invoke(null, new object[] { probeRun, "text", probeRun.Text, assign, i18n }) ?? 0);
                        var restored = afterApply == "Multiplayer module" &&
                                       restoreHits == 1 &&
                                       probeRun.Text == "联机模块";
                        translated = direct == "Multiplayer module" && restored;
                        frontendDetail = "direct=" + direct + ", apply=" + applyHits +
                                         ", restore=" + restoreHits + ", after=" + afterApply +
                                         ", final=" + probeRun.Text +
                                         ", inlines=" + probe.Inlines.Count;
                    }
                    finally
                    {
                        if (oldLanguage != null)
                            i18nType.GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.Instance)
                                ?.Invoke(i18n, new[] { oldLanguage });
                    }
                    frontendDetail = "radio=" + (radio != null) + ", number=" + (number != null) +
                                     ", password=" + (password != null) + ", multiline=" + (multiline != null) +
                                     ", progress=" + (progressElement != null) + ", banner=" + (banner != null) +
                                     ", badge=" + (badge != null) + ", expander=" + (expander != null) +
                                     ", tabs=" + (tabs != null) + ", timeline=" + (timeline != null) +
                                     ", table=" + (table != null) + ", empty=" + (emptyState != null) +
                                     ", code=" + (codeBlock != null) + ", scroll=" + (scroller != null) +
                                     ", search=" + (search != null) + ", segmented=" + (segmented != null) +
                                     ", color=" + (color != null) + ", select=" + (selectList != null) +
                                     ", translated=" + translated +
                                     "; " + frontendDetail;
                    frontendOk = radio != null && number != null && password != null &&
                                 multiline != null && progressElement != null && banner != null &&
                                 badge != null && expander != null && tabs != null &&
                                 timeline != null && table != null && emptyState != null &&
                                  codeBlock != null && scroller != null && search != null &&
                                  segmented != null && color != null && selectList != null &&
                                  translated;
                }
                catch (Exception ex)
                {
                    frontendError = ex;
                    frontendDetail = ex.Message;
                }
                finally
                {
                    frontendDone.Set();
                }
            }));
            frontendDone.Wait(TimeSpan.FromSeconds(10));
                Console.WriteLine("[uitest] API 1.11 frontends=" + (frontendOk ? "PASS" : "FAIL"));
            if (!frontendOk)
            {
                Console.WriteLine("[uitest] frontend detail: " + frontendDetail);
                if (frontendError != null)
                    Console.Error.WriteLine("[uitest] frontend error: " + frontendError.Message);
                failures++;
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
        public static int ThemeNow = 4;
        public static int ColorHue = 210;
        public static int ColorSat = 85;
        public static int ColorLightAdjust;
        public static int ThemeRefreshMainCalls;

        public static void ThemeRefreshMain()
        {
            ThemeRefreshMainCalls++;
        }

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
        public static int RequestCalls;
        public static string LastRequest;

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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string NetRequestByClientRetry(string url, System.Net.Http.HttpMethod method,
            string backupUrl, object content, string contentType, string accept, string[,] headers,
            bool requireJson, Encoding encoding, bool simulateBrowserHeaders)
        {
            RequestCalls++;
            LastRequest = (method?.Method ?? "GET") + " " + url + " json=" + requireJson;
            return "response:" + url;
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
        public static int ModRequestCalls;
        public static string LastModRequest;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DlClientListMain(object context)
        {
            var pad = "harness.dlclientlist." + (context == null ? 0 : 1);
            DlClientListMainCalls += pad.Length > 0 ? 1 : 0;
        }

        // ---- typed download family (ModDownload) ----
        public static bool DlSourcePreferMojang => true;
        public static bool DlVersionListPreferMojang => false;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object DlClientListGet(string source) => new[] { "1.21.4", "1.21.3" };

        public static IEnumerable<string> DlSourceAssetsGet(string version) => new[] { "assets-" + version };
        public static IEnumerable<string> DlSourceLibraryGet(string version) => new[] { "libs-" + version };
        public static IEnumerable<string> DlSourceLauncherOrMetaGet(string version) => new[] { "meta-" + version };
        public static string DlSourceModGet(string project) => "mod-" + project;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object DlModRequest(string url, System.Net.Http.HttpMethod method, string body,
            string contentType)
        {
            ModRequestCalls++;
            LastModRequest = (method == null ? "?" : method.Method) + " " + url;
            return LastModRequest;
        }
    }

    /// <summary>Stand-in for PCL.ModMinecraft and its nested McInstance.</summary>
    public static class ModMinecraft
    {
        public static int ListCalls;
        public static object SelectedSkinValue = new object();
        public static string McFolderSelected { get; set; } = @"C:\instances";
        public static McInstance McInstanceSelected { get; set; }

        public static object McSkinSelect() => SelectedSkinValue;

        public static string GetMcFoolName(string folder)
        {
            var pad = "harness.foolname." + (folder ?? string.Empty).Length;
            return pad.Length > 0 ? "友好名称:" + folder : null;
        }

        // Mirrors PCL's private static InitMcInstanceListWithCache(folder).
        private static Dictionary<object, List<McInstance>> InitMcInstanceListWithCache(string folder)
        {
            ListCalls++;
            return new Dictionary<object, List<McInstance>>
            {
                { "Card", new List<McInstance> { new McInstance("Demo", "1.21.4", true) } }
            };
        }

        public class McInstance
        {
            public McInstance() { }

            public McInstance(string name, string version, bool modable)
            {
                Name = name;
                Version = version;
                Modable = modable;
            }

            public string Name { get; set; }
            public string Version { get; set; }
            public bool Modable { get; set; }
            public string PathIndie => @"C:\instances\" + Name;
            public string GetJsonPath() => PathIndie + "\\" + Name + ".json";
            public string VersionDisplayName() => Name + " (" + Version + ")";
        }
    }

    /// <summary>Stand-in for PCL.ModLaunch (launch pipeline + login facts).</summary>
    public static class ModLaunch
    {
        public static int StartCalls;
        public static int LoginInputCalls;
        public static object LastOptions;
        public static string LoginNameValue = "Dissociate";
        public static object LoginInputValue = new object();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool McLaunchStart(object options)
        {
            StartCalls++;
            LastOptions = options;
            return true;
        }

        public static string GetNativesFolder(object instance) => @"C:\temp\natives";
        public static string McLoginName() => LoginNameValue;
        public static string McLoginAble() => string.Empty;
        public static object McLoginInput() { LoginInputCalls++; return LoginInputValue; }
    }

    /// <summary>Harmless target the harness detours to exercise module-scoped hooks.</summary>
    public static class HookTarget
    {
        public static int OriginalCalls;
        public static readonly List<string> Order = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Ping(int value)
        {
            // Deliberately chunky: the detour engine rewrites the first bytes of the JIT'd body and
            // refuses methods whose IL is too small (a one-liner would be rejected by that valve).
            var pad = "harness.hooktarget." + value;
            var scaled = value * 2;
            OriginalCalls += pad.Length > 0 ? 1 : 0;
            Order.Add("original:" + value);
            if (scaled > 1000) scaled -= 3;
            return scaled;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Echo(string value)
        {
            var text = value ?? string.Empty;
            var builder = new System.Text.StringBuilder();
            builder.Append("harness:");
            builder.Append(text);
            return builder.ToString();
        }

        public static int OverloadedStringCalls;
        public static int OverloadedIntCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Overloaded(string value)
        {
            var pad = "harness.overloaded.string." + (value ?? string.Empty);
            OverloadedStringCalls += pad.Length > 0 ? 1 : 0;
            return (value ?? string.Empty).Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Overloaded(int value)
        {
            var pad = "harness.overloaded.int." + value;
            OverloadedIntCalls += pad.Length > 0 ? 1 : 0;
            return value * 3;
        }
    }

    /// <summary>Stand-in for PCL.ModLoader: taskbar helpers plus a LoaderBase instance.</summary>
    public static class ModLoader
    {
        public static int TaskbarRefreshCalls;
        public static double TaskbarProgressValue = 0.42;
        public static object LastTaskbarCombo;

        public static double LoaderTaskbarProgressGet() => TaskbarProgressValue;
        public static void LoaderTaskbarProgressRefresh() => TaskbarRefreshCalls++;
        public static void LoaderTaskbarAdd(object combo) => LastTaskbarCombo = combo;

        /// <summary>Mirrors the host's taskbar registry (MeloongCore.ConcurrentList in the real host).</summary>
        public static readonly List<LoaderBase> LoaderTaskbar = new List<LoaderBase>();

        // The real host raises value-typed events (LoadState enums, doubles); the stand-in mirrors
        // that shape so the payload's delegate bridge is exercised for real.
        public enum LoadState { Waiting, Loading, Finished, Failed, Canceled }

        public delegate void OnStateChangedUiEventHandler(LoaderBase Loader, LoadState NewState, LoadState OldState);
        public delegate void ProgressChangedEventHandler(double NewValue, double OldValue);
        public delegate void PreviewFinishEventHandler(LoaderBase Loader);

        public class LoaderBase
        {
            public string Name = "harness loader";
            public LoadState StateValue = LoadState.Waiting;
            public string State => StateValue.ToString();
            public double ProgressWeight = 1.0;
            public Exception Error;

            private double _progress;
            public double Progress
            {
                get => _progress;
                set
                {
                    if (Math.Abs(_progress - value) < double.Epsilon) return;
                    var old = _progress;
                    _progress = value;
                    ProgressChanged?.Invoke(value, old);
                }
            }

            public int StartCalls;
            public int CancelCalls;
            public int FailedCalls;
            public object LastInput;
            public bool LastForceRestart;

            public event OnStateChangedUiEventHandler OnStateChangedUi;
            public event ProgressChangedEventHandler ProgressChanged;
            public event PreviewFinishEventHandler PreviewFinish;

            public virtual List<LoaderBase> GetLoaderList(bool onlyRunning = false) =>
                new List<LoaderBase> { this };

            public void RaisePreviewFinish() => PreviewFinish?.Invoke(this);

            protected void SetState(LoadState state)
            {
                var old = StateValue;
                StateValue = state;
                OnStateChangedUi?.Invoke(this, state, old);
            }

            public void Start(object input, bool forceRestart)
            {
                StartCalls++;
                LastInput = input;
                LastForceRestart = forceRestart;
                SetState(LoadState.Loading);
            }

            public void Cancel()
            {
                CancelCalls++;
                SetState(LoadState.Canceled);
            }

            public void Failed(Exception error)
            {
                FailedCalls++;
                Error = error;
                SetState(LoadState.Failed);
            }
        }

        /// <summary>Stand-in for the host's LoaderCombo&lt;TInput&gt; (aggregate loader).</summary>
        public class LoaderCombo<TInput> : LoaderBase
        {
            public List<LoaderBase> Loaders;
            public TInput Input;

            public LoaderCombo(string name)
            {
                Loaders = new List<LoaderBase>();
                Name = name;
            }

            public LoaderCombo(string name, IEnumerable<LoaderBase> loaders) : this(name)
            {
                foreach (var loader in loaders)
                {
                    if (loader == null) continue;
                    Loaders.Add(loader);
                    loader.GetLoaderList();   // mirror the host's parent wiring side effect
                }
            }

            public override List<LoaderBase> GetLoaderList(bool onlyRunning = false) => Loaders;
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
        public FormMain()
        {
            PageCurrent = new PageStackData { Page = PageType.Launch };
            PageLast = PageCurrent;
            PageRight = new MyPageRight();
            PageStack = new List<PageStackData> { PageCurrent };
        }

        public class PageStackData
        {
            public PageType Page;

            public static implicit operator PageStackData(PageType page) =>
                new PageStackData { Page = page };
        }

        public enum PageType
        {
            Launch,
            Download,
            Link,
            Setup,
            Other,
            InstanceSelect,
            TaskManager,
            InstanceSetup,
            ResourceDetail,
            HelpDetail
        }

        public enum PageSubType
        {
            Default,
            Mod,
            Pack,
            SetupLaunch,
            SetupLink,
            SetupUI,
            SetupSystem,
            DownloadInstall,
            DownloadMod,
            OtherHelp,
            OtherAbout
        }

        public PageStackData PageCurrent;
        public PageStackData PageLast;
        public List<PageStackData> PageStack;
        public object PageRight;
        public PageSubType PageCurrentSub { get; private set; }
        public int PageChangeCalls;
        public int PageBackCalls;

        public void PageChange(PageStackData data, PageSubType sub)
        {
            PageChangeCalls++;
            PageLast = PageCurrent;
            PageCurrent = data ?? new PageStackData { Page = PageType.Launch };
            PageCurrentSub = sub;
            PageStack.Add(PageCurrent);
            PageRight = new MyPageRight();
        }

        public void PageBack()
        {
            PageBackCalls++;
            if (PageStack.Count > 1) PageStack.RemoveAt(PageStack.Count - 1);
            if (PageStack.Count > 0) PageCurrent = PageStack[PageStack.Count - 1];
            PageRight = new MyPageRight();
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
        public static FormMain FrmMain = new FormMain();

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

