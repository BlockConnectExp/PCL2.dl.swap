using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Resolves a host method (by metadata token when the build is known, by name otherwise),
    /// JITs it, and installs an in-process detour with a callable trampoline.
    /// </summary>
    internal sealed class DetourInstaller
    {
        private readonly X86Detour _detour;
        private readonly object _originalDelegate;

        private DetourInstaller(X86Detour detour, object originalDelegate)
        {
            _detour = detour;
            _originalDelegate = originalDelegate;
        }

        public IntPtr Trampoline => _detour.Trampoline;
        public IntPtr Target => _detour.Target;
        public T Original<T>() where T : class => (T)_originalDelegate;

        public static DetourInstaller Install(MethodInfo method, Delegate hook)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (hook == null) throw new ArgumentNullException(nameof(hook));

            var handle = method.MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            var target = handle.GetFunctionPointer();
            var hookPointer = Marshal.GetFunctionPointerForDelegate(hook);
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
        public MethodInfo Method { get; private set; }

        public string Verify()
        {
            var bytes = new byte[5];
            Marshal.Copy(TargetAddress, bytes, 0, 5);
            return $"method={Method?.DeclaringType?.FullName}.{Method?.Name} target=0x{TargetAddress.ToInt64():X} hook=0x{HookAddress.ToInt64():X} " +
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


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->