using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PclDlSwap.PclInject
{
    /// <summary>
    /// Local stand-in for ExLoader: start PCL (or attach to a running instance) and run
    /// LoadLibraryW on the swap shim, so the injected shape can be exercised end to end
    /// without the ExLoader UI. The shim itself extracts the payload and hosts the CLR.
    /// </summary>
    internal static class Program
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint MemCommit = 0x00001000;
        private const uint MemReserve = 0x00002000;
        private const uint PageReadWrite = 0x04;
        private const uint Infinite = 0xFFFFFFFF;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string exe = null, dll = null, extraArgs = null;
            var pid = 0;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--exe": exe = args[++i]; break;
                    case "--dll": dll = args[++i]; break;
                    case "--args": extraArgs = args[++i]; break;
                    case "--pid": pid = int.Parse(args[++i]); break;
                    default:
                        Console.Error.WriteLine("usage: PclInject --exe <target.exe> --dll <shim.dll> [--args \"...\"]");
                        Console.Error.WriteLine("   or: PclInject --pid <processId> --dll <shim.dll>");
                        return 2;
                }
            }

            if (dll == null || !File.Exists(dll))
            {
                Console.Error.WriteLine("shim not found: " + dll);
                return 2;
            }
            dll = Path.GetFullPath(dll);

            if (pid != 0)
            {
                var handle = OpenProcess(0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
                if (handle == IntPtr.Zero)
                {
                    Console.Error.WriteLine("OpenProcess failed: " + Marshal.GetLastWin32Error());
                    return 3;
                }
                try
                {
                    return Inject(handle, dll) ? 0 : 4;
                }
                finally { CloseHandle(handle); }
            }

            if (exe == null || !File.Exists(exe))
            {
                Console.Error.WriteLine("target not found: " + exe);
                return 2;
            }
            exe = Path.GetFullPath(exe);
            var workingDirectory = Path.GetDirectoryName(exe);
            var commandLine = extraArgs == null ? "\"" + exe + "\"" : "\"" + exe + "\" " + extraArgs;

            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CreateSuspended, IntPtr.Zero, workingDirectory, ref startup, out var info))
            {
                Console.Error.WriteLine("CreateProcess failed: " + Marshal.GetLastWin32Error());
                return 3;
            }

            Console.WriteLine("started pid=" + info.dwProcessId + " (suspended)");
            try
            {
                if (!Inject(info.hProcess, dll)) return 4;
                ResumeThread(info.hThread);
                Console.WriteLine("shim injected; process resumed");
                return 0;
            }
            finally
            {
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
        }

        private static bool Inject(IntPtr process, string dllPath)
        {
            var bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            var remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes.Length,
                MemCommit | MemReserve, PageReadWrite);
            if (remote == IntPtr.Zero)
            {
                Console.Error.WriteLine("VirtualAllocEx failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            if (!WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out _))
            {
                Console.Error.WriteLine("WriteProcessMemory failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            // System DLLs share a base address across processes of one boot, so the local
            // LoadLibraryW address is valid in the target as well.
            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                Console.Error.WriteLine("GetProcAddress(LoadLibraryW) failed");
                return false;
            }

            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread == IntPtr.Zero)
            {
                Console.Error.WriteLine("CreateRemoteThread failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            WaitForSingleObject(thread, 30000);
            CloseHandle(thread);
            Console.WriteLine("LoadLibraryW(" + dllPath + ") dispatched");
            return true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
        private static extern bool CreateProcess(string app, string commandLine, IntPtr pa, IntPtr ta,
            bool inheritHandles, uint flags, IntPtr environment, string currentDirectory,
            ref StartupInfo startup, out ProcessInformation info);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size,
            uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
            UIntPtr size, out UIntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize,
            IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
