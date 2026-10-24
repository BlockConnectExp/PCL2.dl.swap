using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Thin reflection bridge to the host assembly. Deliberately narrow: it only touches host
    /// members whose shape is stable across supported builds, and every call is guarded so a
    /// layout change degrades to a logged no-op instead of a crash.
    /// </summary>
    internal static class PclHostBridge
    {
        private static Assembly Host => Assembly.GetEntryAssembly();

        private static MethodInfo Find(string typeName, string methodName, int parameterCount, bool isStatic)
        {
            var type = Host?.GetType(typeName, throwOnError: false);
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return type.GetMethods(flags).FirstOrDefault(m =>
                m.Name == methodName && m.GetParameters().Length == parameterCount && m.IsStatic == isStatic);
        }

        /// <summary>Continues a user-approved (non-silent) update through the host's own flow.</summary>
        public static void UpdateStartDirect(bool silent, IEnumerable<string> urlsOverride)
        {
            try
            {
                var method = Find("PCL.ModSecret", "UpdateStart", 2, true);
                if (method == null)
                {
                    SwapLog.Warn("桥接：找不到 ModSecret.UpdateStart，更新请求已丢弃。");
                    return;
                }
                method.Invoke(null, new object[] { silent, urlsOverride });
            }
            catch (TargetInvocationException ex)
            {
                SwapLog.Error("桥接：UpdateStart 调用失败：" + (ex.InnerException ?? ex));
            }
            catch (Exception ex)
            {
                SwapLog.Error("桥接：UpdateStart 桥接异常：" + ex);
            }
        }

        /// <summary>Original permission value, used when the gate is not hard-denied.</summary>
        public static bool HasUpdatePermissionDirect()
        {
            try
            {
                var type = Host?.GetType("PCL.ModSecret", throwOnError: false);
                var property = type?.GetProperty("HasUpdatePermission", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property == null) return true;
                return (bool)property.GetValue(null, null);
            }
            catch (Exception ex)
            {
                SwapLog.Warn("桥接：HasUpdatePermission 读取失败，按 true 处理：" + ex.Message);
                return true;
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover