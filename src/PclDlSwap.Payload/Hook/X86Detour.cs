using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Gee.External.Capstone;
using Gee.External.Capstone.X86;
using PclDlSwap.Payload.Interop;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Minimal in-process x86 detour: relocates the prologue, writes a 5-byte relative jump,
    /// and exposes a trampoline that resumes the original method body.
    /// Only the x86 encoding is supported because every official PCL build is PE32/AnyCPU-x86.
    /// </summary>
    public sealed class X86Detour
    {
        private const int JumpSize = 5;

        private readonly IntPtr _target;
        private readonly IntPtr _hook;
        private IntPtr _trampoline;
        private byte[] _originalPrologue;
        private bool _installed;

        public IntPtr Trampoline => _trampoline;
        public bool IsInstalled => _installed;
        public IntPtr Target => _target;

        private X86Detour(IntPtr target, IntPtr hook)
        {
            _target = target;
            _hook = hook;
        }

        public static X86Detour Install(IntPtr target, IntPtr hook)
        {
            if (target == IntPtr.Zero) throw new ArgumentException("target address is null", nameof(target));
            if (hook == IntPtr.Zero) throw new ArgumentException("hook address is null", nameof(hook));
            var detour = new X86Detour(target, hook);
            detour.Apply();
            return detour;
        }

        private void Apply()
        {
            var instructions = Disassemble(_target, 64);
            if (instructions.Count == 0)
                throw new InvalidOperationException("无法反汇编目标方法序言。");

            int stolenLength = 0;
            int limit = instructions.Count;
            for (int i = 0; i < limit; i++)
            {
                stolenLength += instructions[i].Bytes.Length;
                if (stolenLength >= JumpSize) break;
            }
            if (stolenLength < JumpSize)
                throw new InvalidOperationException("目标方法序言过短，无法安装跳转。");

            _originalPrologue = new byte[stolenLength];
            Marshal.Copy(_target, _originalPrologue, 0, stolenLength);

            var relocated = Relocate(instructions, stolenLength);
            if (relocated == null)
                throw new InvalidOperationException("序言包含无法重定位的指令，已中止 detour。");

            _trampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                (UIntPtr)(relocated.Count + JumpSize),
                NativeMethods.MemCommit | NativeMethods.MemReserve,
                NativeMethods.PageExecuteReadWriteAlloc);
            if (_trampoline == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAlloc 分配 trampoline 失败。");

            Marshal.Copy(relocated.ToArray(), 0, _trampoline, relocated.Count);

            // trampoline: stolen (relocated) prologue + jmp back to target+stolenLength
            var backJump = BuildRelativeJump(
                _trampoline + relocated.Count,
                _target + stolenLength);
            Marshal.Copy(backJump, 0, _trampoline + relocated.Count, JumpSize);

            var patch = BuildRelativeJump(_target, _hook);
            WriteBytes(_target, patch);
            _installed = true;
        }

        public void Remove()
        {
            if (!_installed) return;
            WriteBytes(_target, _originalPrologue);
            _installed = false;
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
            using (var disassembler = CapstoneDisassembler.CreateX86Disassembler(X86DisassembleMode.Bit32))
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

        private static byte[] BuildRelativeJump(IntPtr from, IntPtr to)
        {
            var bytes = new byte[JumpSize];
            bytes[0] = 0xE9;
            long delta = (long)to - ((long)from + JumpSize);
            if (delta < int.MinValue || delta > int.MaxValue)
                throw new InvalidOperationException("跳转距离超出 x86 rel32 范围。");
            var rel = BitConverter.GetBytes((int)delta);
            Buffer.BlockCopy(rel, 0, bytes, 1, 4);
            return bytes;
        }

        private static List<byte> Relocate(List<X86Instruction> instructions, int stolenLength)
        {
            var output = new List<byte>(stolenLength + 16);
            int consumed = 0;
            foreach (var instruction in instructions)
            {
                if (consumed >= stolenLength) break;
                var raw = instruction.Bytes;
                consumed += raw.Length;

                switch (instruction.Id)
                {
                    case X86InstructionId.X86_INS_JMP:
                    case X86InstructionId.X86_INS_CALL:
                    {
                        long operandImmediate;
                        try { operandImmediate = instruction.Details.Operands[0].Immediate; }
                        catch (Exception) { return null; }
                        long originalTarget = (long)instruction.Address + raw.Length + operandImmediate;
                        long newAddress = (long)instruction.Address + output.Count;
                        long delta = originalTarget - (newAddress + raw.Length);
                        if (delta < int.MinValue || delta > int.MaxValue) return null;
                        for (int i = 0; i < raw.Length; i++) output.Add(raw[i]);
                        var rel = BitConverter.GetBytes((int)delta);
                        int relOffset = raw.Length - 4;
                        output[output.Count - 4 + 0] = rel[0];
                        output[output.Count - 4 + 1] = rel[1];
                        output[output.Count - 4 + 2] = rel[2];
                        output[output.Count - 4 + 3] = rel[3];
                        break;
                    }
                    case X86InstructionId.X86_INS_JAE:
                    case X86InstructionId.X86_INS_JA:
                    case X86InstructionId.X86_INS_JBE:
                    case X86InstructionId.X86_INS_JB:
                    case X86InstructionId.X86_INS_JCXZ:
                    case X86InstructionId.X86_INS_JECXZ:
                    case X86InstructionId.X86_INS_JE:
                    case X86InstructionId.X86_INS_JGE:
                    case X86InstructionId.X86_INS_JG:
                    case X86InstructionId.X86_INS_JLE:
                    case X86InstructionId.X86_INS_JL:
                    case X86InstructionId.X86_INS_JNE:
                    case X86InstructionId.X86_INS_JNO:
                    case X86InstructionId.X86_INS_JNP:
                    case X86InstructionId.X86_INS_JNS:
                    case X86InstructionId.X86_INS_JO:
                    case X86InstructionId.X86_INS_JP:
                    case X86InstructionId.X86_INS_JRCXZ:
                    case X86InstructionId.X86_INS_JS:
                        return null;
                    default:
                        output.AddRange(raw);
                        break;
                }
            }
            return output;
        }
    }
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->