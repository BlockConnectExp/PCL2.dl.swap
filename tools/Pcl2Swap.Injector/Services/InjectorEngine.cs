using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pcl2Swap.Injector.Services;

public sealed class InjectionResult
{
    public bool Success { get; init; }
    public string Stage { get; init; } = "";
    public string? Detail { get; init; }
    public int Pid { get; init; }
    public bool Verified { get; init; }
    public string? LogPath { get; init; }
    public string? LogTail { get; init; }
}

/// <summary>
/// The injection itself: LoadLibraryW inside the target, exactly like ExLoader does, minus
/// ExLoader. New PCL builds are injected after their first window appears - injecting while the
/// CLR is still booting fails with 0x80131023, which is also why the launch path waits instead
/// of starting suspended.
/// </summary>
public static class InjectorEngine
{
    private const uint ProcessAccess = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400;
    private const uint MemCommit = 0x00001000;
    private const uint MemReserve = 0x00002000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitTimeout = 0x00000102;

    public static async Task<InjectionResult> InjectAsync(
        int pid, string shimDll, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"正在注入 PID {pid} …");
        var handle = OpenProcess(ProcessAccess, false, pid);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return new InjectionResult
            {
                Pid = pid,
                Stage = "OpenProcess",
                Success = false,
                Detail = $"无法打开目标进程（Win32 {error}）。" +
                         (error == 5 ? " 目标可能以管理员身份运行，请用管理员身份重开注入器。" : "")
            };
        }

        try
        {
            return await Task.Run(() => Inject(handle, pid, shimDll, progress, ct), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Starts the executable normally (never suspended), waits for its window, then injects.
    /// The wait matters: PCL is WPF + .NET Framework, and LoadLibraryW before the CLR is up
    /// returns a failing HRESULT for managed DLLs.
    /// </summary>
    public static async Task<(int Pid, InjectionResult Result)> LaunchAndInjectAsync(
        string exePath, string shimDll, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("正在启动 PCL2 …");
        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            return (0, new InjectionResult
            {
                Stage = "CreateProcess",
                Success = false,
                Detail = "进程启动失败。"
            });
        }

        progress?.Report($"已启动 PID {process.Id}，等待窗口与 CLR 就绪 …");
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    return (process.Id, new InjectionResult
                    {
                        Pid = process.Id,
                        Stage = "WaitForWindow",
                        Success = false,
                        Detail = "PCL2 在注入前退出了（退出码 " + process.ExitCode + "）。"
                    });
                }

                if (process.MainWindowHandle != IntPtr.Zero) break;
            }
            catch
            {
                // keep polling
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        // Give the CLR a moment after the window shows; the first frame is drawn by WPF, which
        // means the runtime is up and LoadLibraryW on a managed DLL will succeed.
        await Task.Delay(1500, ct).ConfigureAwait(false);

        var result = await InjectAsync(process.Id, shimDll, progress, ct).ConfigureAwait(false);
        return (process.Id, result);
    }

    private static InjectionResult Inject(
        IntPtr process, int pid, string dllPath, IProgress<string>? progress, CancellationToken ct)
    {
        var bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
        var remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes.Length,
            MemCommit | MemReserve, PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            return new InjectionResult
            {
                Pid = pid,
                Stage = "VirtualAllocEx",
                Success = false,
                Detail = "VirtualAllocEx 失败：Win32 " + Marshal.GetLastWin32Error()
            };
        }

        try
        {
            if (!WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out _))
            {
                return new InjectionResult
                {
                    Pid = pid,
                    Stage = "WriteProcessMemory",
                    Success = false,
                    Detail = "WriteProcessMemory 失败：Win32 " + Marshal.GetLastWin32Error()
                };
            }

            // System DLLs share a base address across processes of one boot, so the local
            // LoadLibraryW address is valid in the target as well.
            var kernel32 = GetModuleHandleW("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                return new InjectionResult
                {
                    Pid = pid,
                    Stage = "GetProcAddress",
                    Success = false,
                    Detail = "无法解析 LoadLibraryW。"
                };
            }

            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread == IntPtr.Zero)
            {
                return new InjectionResult
                {
                    Pid = pid,
                    Stage = "CreateRemoteThread",
                    Success = false,
                    Detail = "CreateRemoteThread 失败：Win32 " + Marshal.GetLastWin32Error()
                };
            }

            try
            {
                var wait = WaitForSingleObject(thread, 60000);
                if (wait == WaitTimeout)
                {
                    return new InjectionResult
                    {
                        Pid = pid,
                        Stage = "LoadLibraryW",
                        Success = false,
                        Detail = "注入线程 60 秒未返回。"
                    };
                }

                if (GetExitCodeThread(thread, out var exitCode) && exitCode == IntPtr.Zero)
                {
                    return new InjectionResult
                    {
                        Pid = pid,
                        Stage = "LoadLibraryW",
                        Success = false,
                        Detail = "目标进程拒绝加载注入包（CLR 可能尚未就绪）。"
                    };
                }
            }
            finally
            {
                CloseHandle(thread);
            }

            progress?.Report("注入已派发，正在确认 Swap 日志 …");
            var (verified, logPath, tail) = VerifyByLog(ct);
            return new InjectionResult
            {
                Pid = pid,
                Stage = "Verify",
                Success = true,
                Verified = verified,
                LogPath = logPath,
                LogTail = tail,
                Detail = verified
                    ? "注入成功，Swap 已在目标进程中写出日志。"
                    : "注入已派发；未能在本机 TEMP 找到 Swap 日志（目标可能使用独立 TEMP），请到 PCL2 界面上确认。"
            };
        }
        finally
        {
            VirtualFreeEx(process, remote, UIntPtr.Zero, 0x8000);
        }
    }

    /// <summary>
    /// The shim unpacks to &lt;target TEMP&gt;\PCL2Swap\&lt;version&gt;\pcl2swap.log. When the target
    /// shares our TEMP, a fresh log naming the host is the cheapest real-world proof that the
    /// injection ran inside it.
    /// </summary>
    private static (bool Verified, string? LogPath, string? Tail) VerifyByLog(CancellationToken ct)
    {
        var started = DateTime.UtcNow.AddSeconds(-2);
        var root = Path.Combine(Path.GetTempPath(), "PCL2Swap");
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(root))
                {
                    var log = Directory.EnumerateFiles(root, "pcl2swap.log", SearchOption.AllDirectories)
                        .Select(p => new FileInfo(p))
                        .Where(f => f.LastWriteTimeUtc >= started)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (log != null)
                    {
                        var tail = Tail(log.FullName, 40);
                        if (tail.Contains("宿主匹配", StringComparison.Ordinal) ||
                            tail.Contains("SwapHost", StringComparison.Ordinal))
                            return (true, log.FullName, tail);
                    }
                }
            }
            catch
            {
                // log may be mid-write; keep polling
            }

            try { Thread.Sleep(750); } catch { }
        }

        return (false, null, null);
    }

    public static string Tail(string path, int lines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var queue = new Queue<string>(lines);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (queue.Count == lines) queue.Dequeue();
                queue.Enqueue(line);
            }

            return string.Join(Environment.NewLine, queue);
        }
        catch (Exception ex)
        {
            return "(无法读取日志：" + ex.Message + ")";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size,
        uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
        UIntPtr size, out UIntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize,
        IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr handle, out IntPtr exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
