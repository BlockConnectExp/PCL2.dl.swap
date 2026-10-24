using System;
using System.Runtime.InteropServices;

namespace PclDlSwap.Payload.Interop
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        internal const uint PageExecuteReadWrite = 0x40;
        internal const uint PageExecuteRead = 0x20;
        internal const uint PageReadWrite = 0x04;

        internal const int MemCommit = 0x1000;
        internal const int MemReserve = 0x2000;
        internal const int MemRelease = 0x8000;
        internal const uint PageExecuteReadWriteAlloc = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, int flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, int dwFreeType);

        internal enum MessageBoxResult
        {
            Cancel = 2,
            Yes = 6,
            No = 7
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        internal const uint MB_OK = 0x0;
        internal const uint MB_YESNOCANCEL = 0x3;
        internal const uint MB_ICONWARNING = 0x30;
        internal const uint MB_ICONINFORMATION = 0x40;
        internal const uint MB_SETFOREGROUND = 0x10000;
        internal const uint MB_TOPMOST = 0x40000;
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover