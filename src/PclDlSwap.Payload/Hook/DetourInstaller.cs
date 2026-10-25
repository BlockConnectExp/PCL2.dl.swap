using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Resolves a host method (by metadata token when the build is known, by name otherwise),
    /// JITs it, and installs an in-process detour with a callable trampoline. PCL ships as an
    /// AnyCPU assembly, so the same build runs 32-bit on x86 Windows and 64-bit everywhere else;
    /// the engine therefore dispatches on the host process bitness.
    /// </summary>
    internal sealed class DetourInstaller
    {
        private readonly X86Detour _detour;
        private readonly X64Detour _detour64;
        private readonly object _originalDelegate;

        private DetourInstaller(X86Detour detour, object originalDelegate)
        {
            _detour = detour;
            _originalDelegate = originalDelegate;
        }

        private DetourInstaller(X64Detour detour, object originalDelegate)
        {
            _detour64 = detour;
            _originalDelegate = originalDelegate;
        }

        public IntPtr Trampoline => _detour != null ? _detour.Trampoline : _detour64.Trampoline;
        public IntPtr Target => _detour != null ? _detour.Target : _detour64.Target;
        public T Original<T>() where T : class => (T)_originalDelegate;

        public static DetourInstaller Install(MethodBase method, Delegate hook)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (hook == null) throw new ArgumentNullException(nameof(hook));

            var handle = method.MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            var target = handle.GetFunctionPointer();
            var hookPointer = Marshal.GetFunctionPointerForDelegate(hook);

            if (IntPtr.Size == 8)
            {
                var detour64 = X64Detour.Install(target, hookPointer);
                var original64 = Marshal.GetDelegateForFunctionPointer(detour64.Trampoline, hook.GetType());
                GC.KeepAlive(hook);
                return new DetourInstaller(detour64, original64)
                {
                    HookAddress = hookPointer,
                    Method = method
                };
            }

            var detour = X86Detour.Install(target, hookPointer);
            var marker = Marshal.ReadByte(target);
            var hookMarker = Marshal.ReadByte(hookPointer);
            var original = Marshal.GetDelegateForFunctionPointer(detour.Trampoline, hook.GetType());
            GC.KeepAlive(hook);
            return new DetourInstaller(detour, original)
            {
                TargetAddress = target,
                HookAddress = hookPointer,
                TargetByteAfterPatch = marker,
                TrampolineByte = hookMarker,
                Method = method
            };
        }

        public IntPtr TargetAddress { get; private set; }
        public IntPtr HookAddress { get; private set; }
        public byte TargetByteAfterPatch { get; private set; }
        public byte TrampolineByte { get; private set; }
        public MethodBase Method { get; private set; }

        public string Verify()
        {
            if (IntPtr.Size == 8)
            {
                var patched = new byte[6];
                Marshal.Copy(Target, patched, 0, 6);
                return $"method={Method?.DeclaringType?.FullName}.{Method?.Name} bits=x64 target=0x{Target.ToInt64():X} " +
                       $"hook=0x{HookAddress.ToInt64():X} trampoline=0x{Trampoline.ToInt64():X} " +
                       $"target[0..1]=0x{patched[0]:X2}{patched[1]:X2} (FF25=abs-jmp)";
            }

            var bytes = new byte[5];
            Marshal.Copy(TargetAddress, bytes, 0, 5);
            return $"method={Method?.DeclaringType?.FullName}.{Method?.Name} bits=x86 target=0x{TargetAddress.ToInt64():X} hook=0x{HookAddress.ToInt64():X} " +
                   $"target[0]=0x{bytes[0]:X2} (E9=jmp) rel=0x{BitConverter.ToInt32(bytes, 1):X8}";
        }

        public static MethodInfo ResolveTypeMethod(Assembly host, string typeName, string methodName, bool isStatic)
        {
            var type = host.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var candidate in type.GetMethods(flags))
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) continue;
                if (candidate.IsStatic != isStatic) continue;
                return candidate;
            }
            return null;
        }

        public static MethodInfo ResolveByToken(Assembly host, string tokenHex)
        {
            if (string.IsNullOrEmpty(tokenHex)) return null;
            try
            {
                var module = host.ManifestModule;
                int token = Convert.ToInt32(tokenHex, 16);
                var resolved = module.ResolveMethod(token);
                return resolved as MethodInfo;
            }
            catch
            {
                return null;
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
