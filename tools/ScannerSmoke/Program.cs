using System;
using System.IO;
using PclDlSwap.Payload.Sandbox;

namespace PclDlSwap.ScannerSmoke
{
    internal static class Program
    {
        private static int Main()
        {
            var config = new SwapConfig { KeepSandboxFiles = true, AllowDegradedCommit = false };
            var sandbox = new DownloadSandbox(config);

            // Local end-to-end: stage a benign file inside the sandbox and run every configured engine.
            var sandboxRoot = config.ResolvedSandboxDirectory;
            Directory.CreateDirectory(sandboxRoot);
            var staged = Path.Combine(sandboxRoot, "smoke-" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllText(staged, "PCL2.dl.swap scanner smoke test");
            ZoneIdentifier.Stamp(staged);

            var scanners = new ScannerSet(config);
            var verdicts = scanners.Scan(staged);
            var allClean = scanners.AllClean(verdicts, out var summary);

            Console.WriteLine("sandbox       : " + staged);
            Console.WriteLine("engines       : " + verdicts.Count);
            foreach (var verdict in verdicts) Console.WriteLine("  " + verdict);
            Console.WriteLine("all clean     : " + allClean);
            Console.WriteLine("policy verdict: " + (allClean ? "would commit" : "would block (degraded mode disabled)"));
            Console.WriteLine(summary.Length > 0 ? "summary       : " + summary.Replace(Environment.NewLine, " | ") : string.Empty);

            File.Delete(staged);
            return 0;
        }
    }
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->