using System.Runtime.InteropServices;
using System.Text;
using Pcl2Swap.Injector.Models;
using Pcl2Swap.Injector.Services;

namespace Pcl2Swap.Injector;

/// <summary>
/// Headless mode: `Pcl2Swap.Injector.exe --cli list|inject …`. The GUI is the product, but the
/// same services on a command line make the injector scriptable and keep the launch-and-inject
/// path testable without poking at windows.
/// </summary>
public static class Cli
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    public static async Task<int> RunAsync(string[] args)
    {
        AttachConsole(AttachParentProcess);
        // A WinExe has no console of its own; when the parent is captured through a pipe (scripts,
        // CI) the attached console output is invisible. Everything is teed to a log file as well.
        var logPath = Path.Combine(Path.GetTempPath(), "pcl2swap-cli-last.txt");
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        var tee = new TeeWriter(stdout, logPath);
        Console.SetOut(tee);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "list" => await ListAsync(args[1..]),
                "inject" => await InjectAsync(args[1..]),
                _ => Fail("未知命令：" + args[0])
            };
        }
        catch (Exception ex)
        {
            return Fail("命令执行异常：" + ex);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PCL2 Swap Injector CLI");
        Console.WriteLine("  --cli list                               列出可注入目标与已发布注入包");
        Console.WriteLine("  --cli inject --pid <PID>                 注入到运行中的 PCL2");
        Console.WriteLine("  --cli inject --exe <path>                启动 PCL2 并注入");
        Console.WriteLine("      [--release <tag>]                    指定注入包版本（默认随附/最新）");
        Console.WriteLine("      [--shim <dll>]                       直接指定注入包 dll");
        Console.WriteLine("      [--wait <seconds>]                   启动后最多等待窗口的秒数（默认 45）");
    }

    private static async Task<int> ListAsync(string[] args)
    {
        Console.WriteLine("== 偏移清单宿主目标 ==");
        foreach (var target in OffsetManifest.Targets)
            Console.WriteLine($"  {target.Id} · PCL {target.Version} · MVID {target.Mvid}");

        Console.WriteLine();
        Console.WriteLine("== 运行中的候选进程 ==");
        var ids = TargetProbe.FindCandidateProcessIds();
        if (ids.Count == 0) Console.WriteLine("  （无）");
        foreach (var pid in ids)
        {
            var info = TargetProbe.DescribeProcess(pid);
            Console.WriteLine($"  PID {pid} · {info.ProcessLabel} · {info.VersionLabel} · {info.SupportLabel}");
        }

        Console.WriteLine();
        Console.WriteLine("== GitHub Releases ==");
        var (releases, error) = await ReleaseFeed.LoadAsync();
        if (error != null)
        {
            Console.WriteLine("  读取失败：" + error);
        }
        else
        {
            foreach (var release in releases)
                Console.WriteLine($"  {release.TagName} · {release.BadgeLabel} · {release.PublishedLabel}");
        }

        var seeded = ShimProvider.SeededShim();
        Console.WriteLine();
        Console.WriteLine("== 随附注入包 ==");
        Console.WriteLine(seeded != null
            ? "  " + seeded.Value.Dll
            : "  （无，将按需从 Release 下载）");
        return 0;
    }

    private static async Task<int> InjectAsync(string[] args)
    {
        int pid = 0;
        string? exe = null;
        string? releaseTag = null;
        string? shimPath = null;
        var waitSeconds = 45;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid": pid = int.Parse(args[++i]); break;
                case "--exe": exe = args[++i]; break;
                case "--release": releaseTag = args[++i]; break;
                case "--shim": shimPath = args[++i]; break;
                case "--wait": waitSeconds = int.Parse(args[++i]); break;
                default: return Fail("未知参数：" + args[i]);
            }
        }

        if (pid == 0 && string.IsNullOrEmpty(exe)) return Fail("必须指定 --pid 或 --exe。");

        ReleaseInfo? release = null;
        if (releaseTag != null)
        {
            var (releases, error) = await ReleaseFeed.LoadAsync();
            if (error != null) return Fail("读取 Release 列表失败：" + error);
            release = releases.FirstOrDefault(r =>
                string.Equals(r.TagName, releaseTag, StringComparison.OrdinalIgnoreCase));
            if (release == null) return Fail("找不到 Release：" + releaseTag);
        }

        var shim = shimPath;
        if (shim == null)
        {
            if (release == null)
            {
                var seeded = ShimProvider.SeededShim();
                if (seeded != null)
                {
                    shim = seeded.Value.Dll;
                    Console.WriteLine("使用随附注入包：" + shim);
                }
                else
                {
                    var (releases, error) = await ReleaseFeed.LoadAsync();
                    if (error != null) return Fail("既没有随附注入包，也无法读取 Release 列表：" + error);
                    release = releases.FirstOrDefault();
                    if (release == null) return Fail("没有可用的 Release。");
                }
            }

            if (shim == null && release != null)
            {
                var progress = new Progress<string>(Console.WriteLine);
                var (resolved, error) = await ShimProvider.EnsureAsync(release, progress, default);
                if (resolved == null) return Fail(error ?? "注入包解析失败。");
                shim = resolved;
            }
        }

        if (shim == null || !File.Exists(shim)) return Fail("注入包不存在：" + shim);
        Console.WriteLine("注入包：" + shim);

        var printer = new Progress<string>(Console.WriteLine);
        InjectionResult result;
        int injectedPid;
        TargetInfo? targetInfo = null;
        if (pid > 0)
        {
            injectedPid = pid;
            targetInfo = TargetProbe.DescribeProcess(pid);
            Console.WriteLine($"目标：PID {pid} · {targetInfo.VersionLabel} · {targetInfo.SupportLabel}");
            result = await InjectorEngine.InjectAsync(pid, shim, printer, default);
        }
        else
        {
            Console.WriteLine($"启动并注入：{exe}");
            var (launchedPid, launched) = await InjectorEngine.LaunchAndInjectAsync(
                exe!, shim, printer, default);
            injectedPid = launchedPid;
            result = launched;
        }

        Console.WriteLine();
        Console.WriteLine($"结果：{(result.Success ? "成功" : "失败")} · 阶段 {result.Stage} · PID {injectedPid}");
        Console.WriteLine(result.Detail);
        if (result.Verified) Console.WriteLine("日志验证：通过（" + result.LogPath + "）");
        if (!result.Success)
        {
            var context = new InjectionContext
            {
                InjectorVersion = "v26.2-Alpha 2",
                Target = targetInfo ?? (string.IsNullOrEmpty(exe) ? null : TargetProbe.DescribeFile(exe!)),
                Release = release,
                Result = result,
                ShimPath = shim
            };
            var title = Diagnostics.BuildTitle(context);
            var body = Diagnostics.BuildBody(context);
            Console.WriteLine();
            Console.WriteLine("---- 诊断（可直接粘贴到 Issue）----");
            Console.WriteLine(body);
            var saved = Diagnostics.SaveLocally(title, body);
            Console.WriteLine("诊断已保存：" + saved);
        }

        return result.Success ? 0 : 2;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("错误：" + message);
        Console.WriteLine("错误：" + message);
        return 1;
    }

    /// <summary>Writes to the console and to a log file so GUI-subsystem runs stay inspectable.</summary>
    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly StreamWriter _file;

        public TeeWriter(TextWriter console, string filePath)
        {
            _console = console;
            _file = new StreamWriter(filePath, false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public override Encoding Encoding => _console.Encoding;

        public override void Write(char value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _console.WriteLine(value);
            _file.WriteLine(value);
        }

        public override void Flush()
        {
            _console.Flush();
            _file.Flush();
        }
    }
}
