using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Builds callable managed copies of host methods.
    ///
    /// A detour replaces the target's first 14 bytes with an absolute jump, so the original body is no longer
    /// reachable at its own entry. Calling a hand-rebuilt prologue trampoline from managed code is unsafe (the
    /// Marshal transition thunk assumes it is entered from unmanaged code and trips the GC mode), so instead we
    /// generate a genuine managed method that contains the original IL - the same approach Harmony uses for
    /// reverse patches, implemented with MonoMod.Utils' DynamicMethodDefinition.
    /// </summary>
    internal static class OriginalFactory
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, MethodInfo> Cache = new Dictionary<string, MethodInfo>();

        public static MethodInfo CreateCopy(MethodBase original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));

            string key;
            try
            {
                key = original.Module.FullyQualifiedName + "|" + original.MetadataToken.ToString("X8");
            }
            catch
            {
                key = original.DeclaringType?.FullName + "." + original.Name + "|" + original.GetParameters().Length;
            }

            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
                var copy = Build(original);
                Cache[key] = copy;
                return copy;
            }
        }

        private static MethodInfo Build(MethodBase original)
        {
            SwapLog.Info("[original] generating IL copy for " + original.DeclaringType?.FullName + "." + original.Name);

            SwapLog.Info("[original] creating DynamicMethodDefinition");
            var definition = new DynamicMethodDefinition(original);
            SwapLog.Info("[original] definition created");
            var copy = definition.Generate();
            SwapLog.Info("[original] generated");
            // DynamicMethod has no metadata handle (MethodHandle throws), so this copy is invoked with
            // MethodInfo.Invoke - that is a normal managed call and stays inside the CLR's calling contract.

            SwapLog.Info("[original] copy ready: " + copy.DeclaringType?.FullName + "." + copy.Name +
                         " (static=" + copy.IsStatic + ")");
            return copy;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
