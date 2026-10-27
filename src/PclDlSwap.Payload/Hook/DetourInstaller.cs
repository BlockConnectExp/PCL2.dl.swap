using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Resolves a host method, JITs it, installs an in-process detour whose target is another *managed method
    /// body*, and exposes the original through an IL copy.
    ///
    /// Both halves matter: the hook target must be a managed method (a Marshal delegate thunk assumes it is
    /// entered from unmanaged code and corrupts the GC mode when JIT code calls it), and the original must be
    /// a real managed method too (executing a relocated prologue from managed code has the same problem in
    /// the opposite direction). See docs/av-root-cause.md.
    /// </summary>
    internal sealed class DetourInstaller
    {
        private readonly X64Detour _detour64;
        private readonly X86Detour _detour32;
        private readonly MethodInfo _originalCopy;
        private readonly bool _isInstance;

        public MethodBase Method { get; private set; }
        public MethodInfo HookMethod { get; private set; }
        public MethodInfo OriginalMethod => _originalCopy;
        public bool Is64Bit => _detour64 != null;

        private DetourInstaller(X64Detour detour, MethodInfo originalCopy, bool isInstance)
        {
            _detour64 = detour;
            _originalCopy = originalCopy;
            _isInstance = isInstance;
        }

        private DetourInstaller(X86Detour detour, MethodInfo originalCopy, bool isInstance)
        {
            _detour32 = detour;
            _originalCopy = originalCopy;
            _isInstance = isInstance;
        }

        /// <summary>
        /// Installs a detour from <paramref name="method"/> to <paramref name="hookMethod"/>.
        /// The hook must be a static method whose parameter list matches the target's ABI: reference
        /// parameters may be declared as <c>object</c>, value parameters must keep their exact type
        /// (<c>bool</c> stays <c>bool</c>), and an instance target gets its <c>this</c> as the first
        /// parameter.
        /// </summary>
        public static DetourInstaller Install(MethodBase method, MethodInfo hookMethod)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (hookMethod == null) throw new ArgumentNullException(nameof(hookMethod));
            if (!hookMethod.IsStatic) throw new InvalidOperationException("hook must be a static method: " + hookMethod);

            var expected = method.GetParameters().Length + (method.IsStatic ? 0 : 1);
            if (hookMethod.GetParameters().Length != expected)
                throw new InvalidOperationException(
                    $"钩子签名不匹配：{hookMethod.DeclaringType?.Name}.{hookMethod.Name} 有 " +
                    $"{hookMethod.GetParameters().Length} 个参数，目标需要 {expected} 个。");

            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            RuntimeHelpers.PrepareMethod(hookMethod.MethodHandle);
            var target = method.MethodHandle.GetFunctionPointer();
            var hook = hookMethod.MethodHandle.GetFunctionPointer();

            var originalCopy = OriginalFactory.CreateCopy(method);
            var isInstance = !method.IsStatic;
            SwapLog.Info($"[detour] 目标入口字节（前 16，安装前）：{HexDump(target)}");

            DetourInstaller result;
            if (IntPtr.Size == 8)
            {
                var detour = X64Detour.Install(target, hook);
                result = new DetourInstaller(detour, originalCopy, isInstance);
            }
            else
            {
                var detour = X86Detour.Install(target, hook);
                result = new DetourInstaller(detour, originalCopy, isInstance);
            }
            SwapLog.Info($"[detour] 目标入口字节（前 16，安装后）：{HexDump(target)}");

            result.Method = method;
            result.HookMethod = hookMethod;
            SwapLog.Info($"钩子已安装：{method.DeclaringType?.FullName}.{method.Name} -> " +
                         $"{hookMethod.DeclaringType?.Name}.{hookMethod.Name}" +
                         $"（原始方法副本文档：" + (originalCopy == null ? "无" : originalCopy.Name) + "）");
            return result;
        }

        /// <summary>Byte-level evidence for hook diagnosis: the first 16 entry bytes as hex.</summary>
        private static string HexDump(IntPtr address)
        {
            try
            {
                var bytes = new byte[16];
                Marshal.Copy(address, bytes, 0, bytes.Length);
                return BitConverter.ToString(bytes).Replace("-", " ");
            }
            catch (Exception ex)
            {
                return "(dump failed: " + ex.Message + ")";
            }
        }

        /// <summary>
        /// Calls the original method through its IL copy. The copy is a genuine managed method, so this is an
        /// ordinary managed call (reflection) - no Marshal transition thunk is involved.
        /// </summary>
        public object InvokeOriginal(object self, params object[] args)
        {
            if (_originalCopy == null)
                throw new InvalidOperationException("该钩子没有可用的原始方法副本。");

            var list = new List<object>();
            if (_isInstance) list.Add(self);
            if (args != null) list.AddRange(args);
            return _originalCopy.Invoke(null, list.ToArray());
        }

        public T InvokeOriginal<T>(object self, params object[] args)
        {
            return (T)InvokeOriginal(self, args);
        }

        public string Verify()
        {
            try
            {
                var target = Method.MethodHandle.GetFunctionPointer();
                var patched = new byte[6];
                Marshal.Copy(target, patched, 0, 6);
                return $"method={Method?.DeclaringType?.FullName}.{Method?.Name} " +
                       $"bits=0x{patched[0]:X2}{patched[1]:X2} (FF25=abs-jmp)";
            }
            catch (Exception ex)
            {
                return "verify failed: " + ex.Message;
            }
        }

        public static MethodInfo ResolveTypeMethod(Assembly host, string typeName, string methodName, bool isStatic)
        {
            var type = host.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                       BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var candidate in type.GetMethods(flags))
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) continue;
                if (candidate.IsStatic != isStatic) continue;
                return candidate;
            }
            return null;
        }

        /// <summary>
        /// True when this method's JIT entry already carries a detour this tool wrote (x64: FF 25,
        /// x86: E9 - see the respective engine). A second injection into the same process (a newer
        /// shim, or ExLoader firing twice) must not stack a second hook: the first engine's patched
        /// body would then be copied as the "original" and the two payloads would recurse.
        /// </summary>
        public static bool IsHooked(MethodBase method)
        {
            try
            {
                if (method == null) return false;
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                var entry = method.MethodHandle.GetFunctionPointer();
                var b0 = Marshal.ReadByte(entry);
                if (b0 == 0xFF && Marshal.ReadByte(entry, 1) == 0x25) return true;
                return b0 == 0xE9;   // x86 relative jump
            }
            catch
            {
                return false;
            }
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
