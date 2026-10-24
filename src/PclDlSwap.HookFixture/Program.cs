using System;
using System.IO;
using System.Reflection;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.HookFixture
{
    internal static class Program
    {
        private const string TestSwitch = "PCL2SWAP_TEST";
        private const string FixtureSecretType = "PclDlSwap.HookFixture.FixtureTarget";

        private static int _failures;

        private static int Main()
        {
            Environment.SetEnvironmentVariable(TestSwitch, "1");
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePayload;

            Console.WriteLine("== PCL2.dl.swap hook fixture ==");
            Console.WriteLine($"Test switch: {TestSwitch}={Environment.GetEnvironmentVariable(TestSwitch)}");
            Console.WriteLine($"Fixture type: {FixtureSecretType}");

            // 1. Sanity: the target must behave normally before the detour.
            Check("baseline permission", FixtureTarget.HasUpdatePermission, "true");

            // 2. Install the guards against the stand-in types.
            var payload = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PclDlSwap.Payload.dll"));
            var installer = payload.GetType("PclDlSwap.Payload.Host.CoreInstaller", throwOnError: true);
            var activate = installer.GetMethod("TryInstallFixtures", BindingFlags.Public | BindingFlags.Static);
            activate.Invoke(null, new object[] { Assembly.GetExecutingAssembly(), true });

            // 3. Permission gate must now deny updates.
            Check("permission blocked", FixtureTarget.HasUpdatePermission, "false");

            // 4. A control (non-cloud) download must reach the original method untouched.
            FixtureTarget.DownloadByLoader(new[] { "https://libraries.minecraft.net/x.jar" }, @"C:\tmp\game.jar", null, null, false);
            Check("control download forwarded", FixtureTarget.DownloadCalls.Count > 0, "true");

            // 5. A PCL cloud download must be diverted into the sandbox (and blocked here because
            //    the fake URL cannot be fetched or verified).
            bool diverted = false;
            string divertDetail;
            try
            {
                FixtureTarget.DownloadByLoader(new[] { "https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json" },
                    @"C:\tmp\pcl-cloud.json", null, null, false);
                divertDetail = "original method ran (no sandbox)";
            }
            catch (Exception ex)
            {
                diverted = ex.Message.Contains("PCL2 DLL-Swap");
                divertDetail = ex.GetType().Name + ": " + ex.Message;
            }
            Console.WriteLine($"         divert detail: {divertDetail}");
            Console.WriteLine($"         body hits (must stay 0 for cloud path): {FixtureTarget.BodyHits}");
            Check("cloud download diverted and blocked", diverted, "true");

            Console.WriteLine(_failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_failures})");
            return _failures == 0 ? 0 : 1;
        }

        private static Assembly ResolvePayload(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static void Check(string label, object actual, string expected)
        {
            var text = actual?.ToString() ?? "(null)";
            bool ok = string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
            if (!ok) _failures++;
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: {text} (expected {expected})");
        }
    }
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->