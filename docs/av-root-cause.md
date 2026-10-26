# AccessViolation root cause (Alpha 3 field test, 2026-10-26)

## Evidence

- 2.99.0 crash: CLR20r3 `P7=0xc9b` (RID 3227 = `PCL.PageOtherLeft.TryFeedback(instance)`),
  `P8=0xd` (IL offset 13 = `e.Handled = true`).
- 2.99.0.1 crash: `P7=0x11d3` (RID 4563 = `PCL.MyImage/_Closure$__24-0._Lambda$__0`), `P8=0xb8` (IL +184).
- Both crashes happen right after a hooked method returns; PCL's own log shows the routed dialog, the
  browser open and the hint had already completed.
- Controlled experiment (`harness --abi2`, managed caller + allocation-heavy hook + 2000 iterations):
  - hook target = managed method body (PrepareMethod + GetFunctionPointer): **survives 2000 calls**.
  - hook target = `Marshal.GetFunctionPointerForDelegate` thunk: **AccessViolation around call 5**.
- The same test showed the thunk itself preserves callee-saved registers and the detour engine is clean;
  the failure is specifically the transition stub + GC.

## Root cause

`Marshal.GetFunctionPointerForDelegate` emits a thunk that assumes it is entered from unmanaged code
(it performs the "managed -> native" half of a transition). JIT-compiled callers enter it in cooperative
GC mode, so the thunk's GC-mode bookkeeping is wrong; the next GC - inside the hook, or shortly after the
hook returns in the caller's JIT code - faults with AccessViolation. That is why the crash lands on innocent
post-call IL (`e.Handled = true`, the MyImage lambda) and why it looks random.

The `original` path has the same problem in the opposite direction: calling the stolen-prologue trampoline
through `Marshal.GetDelegateForFunctionPointer` means a managed caller is executed as if it were native, so
any hook that forwards to the original (non-cloud downloads, dialog pass-through, OTA pass-through) inherits
the same fault.

## Fix (v2)

1. **Detour target**: write the 14-byte absolute jump so that it points at a *managed method body*
   (`PrepareMethod` + `MethodHandle.GetFunctionPointer`) with an ABI-identical signature - the Harmony-style
   replacement - never at a Marshal thunk.
2. **Original**: emit a full IL copy of the target method into a small dynamic assembly (dnlib, the same
   pattern Harmony uses for reverse patches) and invoke that copy with reflection. This removes the
   stolen-prologue trampoline entirely.
3. **X64Detour v2**: only backs up the first 14 bytes and writes the absolute jump - no disassembly, no
   trampoline, no Capstone dependency. (The RIP-relative refusal path also disappears; the old engine's
   crash while rejecting `mov eax,[rip+...]` prologues is gone with it.)

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
