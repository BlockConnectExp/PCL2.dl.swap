using System;
using System.Runtime.InteropServices;
using PclDlSwap.Payload.Host;
using PclDlSwap.Payload.Interop;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Minimal in-process x64 detour (v2).
    ///
    /// The patch is a single 14-byte absolute jump (<c>FF 25 00000000 &lt;imm64&gt;</c>) written over the
    /// method's entry, so the hook target can be any managed method body - the jump costs no stack frame and
    /// the target runs with the exact calling convention the JIT caller expects.
    ///
    /// There is deliberately no prologue relocation and no trampoline here. The original body is reached
    /// through an IL copy (see <see cref="OriginalFactory"/>): executing a stolen prologue from managed code
    /// crosses the CLR boundary in the wrong direction and corrupts the GC mode, which is what crashed the
    /// Alpha 3 build inside PCL.
    /// </summary>
    public sealed class X64Detour
    {
        private const int JumpSize = 14;
        private const byte RetOpcode = 0xC3;

        private readonly IntPtr _target;
        private readonly IntPtr _hook;
        private byte[] _savedEntry;
        private bool _installed;

        public IntPtr Target => _target;
        public IntPtr Hook => _hook;
        public bool IsInstalled => _installed;

        private X64Detour(IntPtr target, IntPtr hook)
        {
            _target = target;
            _hook = hook;
        }

        public static X64Detour Install(IntPtr target, IntPtr hook)
        {
            if (target == IntPtr.Zero) throw new ArgumentException("target address is null", nameof(target));
            if (hook == IntPtr.Zero) throw new ArgumentException("hook address is null", nameof(hook));
            var detour = new X64Detour(target, hook);
            detour.Apply();
            return detour;
        }

        private void Apply()
        {
            _savedEntry = new byte[JumpSize];
            Marshal.Copy(_target, _savedEntry, 0, JumpSize);

            // The jump must stay inside the method body. A ret before the last byte means the body is shorter
            // than the patch and we refuse instead of overwriting whatever follows it in the code heap.
            for (int i = 0; i < JumpSize - 1; i++)
            {
                if (_savedEntry[i] == RetOpcode)
                    throw new InvalidOperationException(
                        "目标方法过短（偏移 " + i + " 即为 ret），无法安装 14 字节跳转。");
            }

            WriteBytes(_target, BuildAbsoluteJump(_hook));
            SwapLog.Info($"[detour] target=0x{_target.ToInt64():X} patched -> hook=0x{_hook.ToInt64():X} " +
                         $"(entry[0..1]=0x{_savedEntry[0]:X2}{_savedEntry[1]:X2})");
            _installed = true;
        }

        public void Remove()
        {
            if (!_installed) return;
            WriteBytes(_target, _savedEntry);
            _installed = false;
        }

        /// <summary>Absolute indirect jump: FF 25 &lt;disp32=0&gt; &lt;imm64 target&gt;.</summary>
        private static byte[] BuildAbsoluteJump(IntPtr to)
        {
            var bytes = new byte[JumpSize];
            bytes[0] = 0xFF;
            bytes[1] = 0x25;
            bytes[2] = 0x00;
            bytes[3] = 0x00;
            bytes[4] = 0x00;
            bytes[5] = 0x00;
            var address = BitConverter.GetBytes(to.ToInt64());
            Buffer.BlockCopy(address, 0, bytes, 6, 8);
            return bytes;
        }

        private static void WriteBytes(IntPtr address, byte[] bytes)
        {
            uint old;
            if (!NativeMethods.VirtualProtect(address, (UIntPtr)bytes.Length, NativeMethods.PageExecuteReadWrite, out old))
                throw new InvalidOperationException("VirtualProtect 失败，错误码 " + Marshal.GetLastWin32Error());
            Marshal.Copy(bytes, 0, address, bytes.Length);
            NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), address, (UIntPtr)bytes.Length);
            uint ignored;
            NativeMethods.VirtualProtect(address, (UIntPtr)bytes.Length, old, out ignored);
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
