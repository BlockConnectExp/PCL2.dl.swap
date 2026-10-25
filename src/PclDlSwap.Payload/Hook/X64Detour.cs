using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Gee.External.Capstone;
using Gee.External.Capstone.X86;
using PclDlSwap.Payload.Interop;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Minimal in-process x64 detour. The patch is a 14-byte absolute jump
    /// (<c>FF 25 00000000 &lt;imm64&gt;</c>), so no 2 GB reachability dance is needed and the hook
    /// can live in managed memory. The stolen prologue is copied into a trampoline with a jump
    /// back to the original body.
    ///
    /// Relocation policy: instructions are copied verbatim, relative CALL/JMP targets are
    /// recomputed, and anything the engine cannot prove safe (RIP-relative memory operands,
    /// conditional branches, absolute operands) makes the installation fail loudly instead of
    /// producing a subtly broken hook. JIT-compiled .NET prologues are stack manipulation only,
    /// so this covers every hook this payload installs.
    /// </summary>
    public sealed class X64Detour
    {
        private const int JumpSize = 14;

        private readonly IntPtr _target;
        private readonly IntPtr _hook;
        private IntPtr _trampoline;
        private byte[] _originalPrologue;
        private bool _installed;

        public IntPtr Trampoline => _trampoline;
        public bool IsInstalled => _installed;
        public IntPtr Target => _target;

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
            var instructions = Disassemble(_target, 64);
            if (instructions.Count == 0)
                throw new InvalidOperationException("无法反汇编目标方法序言（x64）。");

            int stolenLength = 0;
            foreach (var instruction in instructions)
            {
                stolenLength += instruction.Bytes.Length;
                if (stolenLength >= JumpSize) break;
            }
            if (stolenLength < JumpSize)
                throw new InvalidOperationException("目标方法序言过短，无法安装 x64 跳转。");

            _originalPrologue = new byte[stolenLength];
            Marshal.Copy(_target, _originalPrologue, 0, stolenLength);

            var capacity = stolenLength + 64;
            _trampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                (UIntPtr)capacity,
                NativeMethods.MemCommit | NativeMethods.MemReserve,
                NativeMethods.PageExecuteReadWriteAlloc);
            if (_trampoline == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAlloc 分配 trampoline 失败。");

            var relocated = Relocate(instructions, stolenLength, _trampoline);
            if (relocated == null)
            {
                NativeMethods.VirtualFree(_trampoline, UIntPtr.Zero, NativeMethods.MemRelease);
                _trampoline = IntPtr.Zero;
                throw new InvalidOperationException("序言包含无法安全重定位的指令，已中止 x64 detour。");
            }
            if (relocated.Count + JumpSize > capacity)
            {
                NativeMethods.VirtualFree(_trampoline, UIntPtr.Zero, NativeMethods.MemRelease);
                _trampoline = IntPtr.Zero;
                throw new InvalidOperationException("重定位后的序言超出 trampoline 容量。");
            }

            Marshal.Copy(relocated.ToArray(), 0, _trampoline, relocated.Count);
            Marshal.Copy(BuildAbsoluteJump(_trampoline + relocated.Count, _target + stolenLength), 0,
                         _trampoline + relocated.Count, JumpSize);

            WriteBytes(_target, BuildAbsoluteJump(_target, _hook));
            _installed = true;
        }

        public void Remove()
        {
            if (!_installed) return;
            WriteBytes(_target, _originalPrologue);
            _installed = false;
        }

        /// <summary>Absolute indirect jump: FF 25 &lt;disp32=0&gt; &lt;imm64 target&gt;.</summary>
        private static byte[] BuildAbsoluteJump(IntPtr from, IntPtr to)
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

        private static List<X86Instruction> Disassemble(IntPtr address, int byteCount)
        {
            var buffer = new byte[byteCount];
            Marshal.Copy(address, buffer, 0, byteCount);
            var result = new List<X86Instruction>();
            using (var disassembler = CapstoneDisassembler.CreateX86Disassembler(X86DisassembleMode.Bit64))
            {
                disassembler.EnableInstructionDetails = true;
                var instructions = disassembler.Disassemble(buffer, (long)address);
                foreach (var instruction in instructions)
                {
                    result.Add(instruction);
                    if (result.Count >= 24) break;
                }
            }
            return result;
        }

        private static List<byte> Relocate(List<X86Instruction> instructions, int stolenLength,
            IntPtr trampoline)
        {
            var output = new List<byte>(stolenLength + 16);
            int consumed = 0;
            foreach (var instruction in instructions)
            {
                if (consumed >= stolenLength) break;
                var raw = instruction.Bytes;
                consumed += raw.Length;

                if (HasUnsafeOperand(instruction))
                    return null;

                switch (instruction.Id)
                {
                    case X86InstructionId.X86_INS_CALL:
                    {
                        var operands = instruction.Details?.Operands;
                        if (operands == null || operands.Length == 0) return null;
                        var callTarget = operands[0];
                        if (callTarget.Type == X86OperandType.Register || callTarget.Type == X86OperandType.Memory)
                        {
                            // register/memory-indirect call: position independent as-is
                            output.AddRange(raw);
                            break;
                        }
                        if (callTarget.Type != X86OperandType.Immediate) return null;

                        long operandImmediate;
                        try { operandImmediate = callTarget.Immediate; }
                        catch (Exception) { return null; }
                        long originalTarget = (long)instruction.Address + raw.Length + operandImmediate;
                        // The copy lives at trampoline + output.Count, not at its old address.
                        long newInstructionAddress = (long)trampoline + output.Count;
                        long delta = originalTarget - (newInstructionAddress + raw.Length);
                        if (delta < int.MinValue || delta > int.MaxValue) return null;
                        output.AddRange(raw);
                        var rel = BitConverter.GetBytes((int)delta);
                        int relOffset = output.Count - 4;
                        output[relOffset] = rel[0];
                        output[relOffset + 1] = rel[1];
                        output[relOffset + 2] = rel[2];
                        output[relOffset + 3] = rel[3];
                        break;
                    }
                    case X86InstructionId.X86_INS_JMP:
                    {
                        var operands = instruction.Details?.Operands;
                        if (operands != null && operands.Length == 1 &&
                            (operands[0].Type == X86OperandType.Register || operands[0].Type == X86OperandType.Memory))
                        {
                            // register/memory-indirect jump: position independent as-is
                            output.AddRange(raw);
                            break;
                        }
                        // A relative jump would change layout; refusing keeps the hook honest.
                        return null;
                    }
                    default:
                        output.AddRange(raw);
                        break;
                }
            }
            return output;
        }

        /// <summary>
        /// Returns true for operands whose meaning depends on the instruction's own address
        /// (RIP-relative memory, absolute immediates used as addresses) or on short branch
        /// distances that this engine does not rewrite.
        /// </summary>
        private static bool HasUnsafeOperand(X86Instruction instruction)
        {
            var details = instruction.Details;
            if (details != null && details.Operands != null)
            {
                foreach (var operand in details.Operands)
                {
                    if (operand.Type == X86OperandType.Memory)
                    {
                        var memory = operand.Memory;
                        if (memory.Base != null && memory.Base.Id == X86RegisterId.X86_REG_RIP)
                            return true;
                    }
                }
            }

            switch (instruction.Id)
            {
                case X86InstructionId.X86_INS_JA:
                case X86InstructionId.X86_INS_JAE:
                case X86InstructionId.X86_INS_JB:
                case X86InstructionId.X86_INS_JBE:
                case X86InstructionId.X86_INS_JE:
                case X86InstructionId.X86_INS_JNE:
                case X86InstructionId.X86_INS_JG:
                case X86InstructionId.X86_INS_JGE:
                case X86InstructionId.X86_INS_JL:
                case X86InstructionId.X86_INS_JLE:
                case X86InstructionId.X86_INS_JO:
                case X86InstructionId.X86_INS_JNO:
                case X86InstructionId.X86_INS_JS:
                case X86InstructionId.X86_INS_JNS:
                case X86InstructionId.X86_INS_JP:
                case X86InstructionId.X86_INS_JNP:
                case X86InstructionId.X86_INS_JRCXZ:
                    return true;
                default:
                    return false;
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
