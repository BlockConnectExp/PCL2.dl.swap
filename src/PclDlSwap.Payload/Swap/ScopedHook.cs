using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using PCL2Swap.Api;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Addon-scoped bytecode hooks. The addon hands over an ordinary delegate (instance or static);
    /// the platform installs a generated *gate* whose behaviour follows the module toggle:
    ///
    ///  * module enabled  -> the addon's handler runs and its return value is used;
    ///  * module disabled -> the gate forwards to the original method (as if never installed);
    ///  * attach token disposed -> same as disabled, per hook.
    ///
    /// That gives LSPosed-like semantics without hot-unloading the native prologue: switching a
    /// module off stops its hooks immediately, and the physical jump only goes away with the
    /// process. The gate keeps the target's ABI (reference parameters travel as object, value
    /// parameters keep their exact type), so it can be the detour target itself.
    /// </summary>
    public static class ScopedHook
    {
        internal sealed class Registration
        {
            public int Id;
            public string AddonId;
            public string HookId;
            public HookMode Mode;
            public Delegate Handler;
            public DetourInstaller Installer;
            public volatile bool Active = true;
            public long Calls;
            public long Forwarded;
        }

        private static readonly ConcurrentDictionary<int, Registration> Registry =
            new ConcurrentDictionary<int, Registration>();
        private static readonly object GateLock = new object();
        private static ModuleBuilder _gateModule;
        private static int _nextId;

        internal static Registration Register(string addonId, string hookId, HookMode mode, Delegate handler)
        {
            var registration = new Registration
            {
                Id = Interlocked.Increment(ref _nextId),
                AddonId = addonId,
                HookId = hookId,
                Mode = mode,
                Handler = handler
            };
            Registry[registration.Id] = registration;
            return registration;
        }

        internal static void Activate(Registration registration, DetourInstaller installer)
        {
            registration.Installer = installer;
            SwapLog.Info($"[hookbus] 模块钩子已装载：{registration.AddonId} · {registration.Mode} · " +
                         $"{(string.IsNullOrEmpty(registration.HookId) ? "(unnamed)" : registration.HookId)}" +
                         $"（当前{(SwapHost.AddonEnabled(registration.AddonId) ? "启用" : "禁用")}）");
        }

        internal static void Deactivate(Registration registration)
        {
            registration.Active = false;
            SwapLog.Info($"[hookbus] 模块钩子已停用：{registration.AddonId} · " +
                         $"{(string.IsNullOrEmpty(registration.HookId) ? "(unnamed)" : registration.HookId)}" +
                         "（直通原方法，物理跳转保留到进程结束）");
        }

        internal static string Describe(Registration registration) =>
            $"{registration.AddonId} · {registration.HookId} · {registration.Mode}：命中 {registration.Calls} 次，" +
            $"其中放行 {registration.Forwarded} 次";

        /// <summary>
        /// Entry point of every generated gate. Called with the target's ABI arguments, so value
        /// types arrive boxed in <paramref name="args"/>.
        /// </summary>
        public static object Invoke(int id, object self, object[] args)
        {
            if (!Registry.TryGetValue(id, out var registration)) return null;
            Interlocked.Increment(ref registration.Calls);

            if (!registration.Active || !SwapHost.AddonEnabled(registration.AddonId))
            {
                Interlocked.Increment(ref registration.Forwarded);
                var installer = registration.Installer;
                return installer == null
                    ? null
                    : installer.InvokeOriginal(self, args ?? new object[0]);
            }

            var passthrough = new List<object>();
            if (self != null) passthrough.Add(self);
            if (args != null) passthrough.AddRange(args);
            var arguments = passthrough.ToArray();
            var installerRef = registration.Installer;

            switch (registration.Mode)
            {
                case HookMode.Prefix:
                    // Observe (or veto by throwing) before the host method runs; its own result wins.
                    registration.Handler.DynamicInvoke(arguments);
                    return installerRef == null ? null : installerRef.InvokeOriginal(self, args ?? new object[0]);

                case HookMode.Postfix:
                    // Let the host method run first, then hand the same call to the handler.
                    var result = installerRef == null ? null : installerRef.InvokeOriginal(self, args ?? new object[0]);
                    registration.Handler.DynamicInvoke(arguments);
                    return result;

                default:
                    // Replace: the handler owns the call and its return value is the result.
                    return registration.Handler.DynamicInvoke(arguments);
            }
        }

        /// <summary>Handler hits per registration - used by the self-check and diagnostics.</summary>
        internal static IReadOnlyCollection<string> Snapshot() =>
            Registry.Values.Select(Describe).ToList();

        /// <summary>
        /// Builds the gate method for one target: same ABI (value parameters keep their type,
        /// reference parameters are object), body forwards to <see cref="Invoke"/>.
        /// </summary>
        internal static MethodInfo BuildGate(MethodInfo target, Registration registration)
        {
            var parameters = new List<ParameterExpression>();
            var hasSelf = !target.IsStatic;
            ParameterExpression self = null;

            if (hasSelf)
            {
                var declared = target.DeclaringType ?? typeof(object);
                self = Expression.Parameter(declared.IsValueType ? declared : typeof(object), "self");
                parameters.Add(self);
            }

            foreach (var parameter in target.GetParameters())
            {
                var type = parameter.ParameterType;
                parameters.Add(Expression.Parameter(
                    type.IsValueType ? type : typeof(object), parameter.Name ?? "arg"));
            }

            var boxed = parameters
                .Skip(hasSelf ? 1 : 0)
                .Select(expression => Expression.Convert(expression, typeof(object)));
            var call = Expression.Call(
                typeof(ScopedHook).GetMethod(nameof(Invoke), BindingFlags.Public | BindingFlags.Static),
                Expression.Constant(registration.Id),
                self == null ? (Expression)Expression.Constant(null, typeof(object)) : Expression.Convert(self, typeof(object)),
                Expression.NewArrayInit(typeof(object), boxed));

            var body = target.ReturnType == typeof(void)
                ? (Expression)Expression.Block(call, Expression.Empty())
                : Expression.Convert(call, target.ReturnType);

            var signature = parameters.Select(parameter => parameter.Type).ToList();
            signature.Add(target.ReturnType);
            var delegateType = Expression.GetDelegateType(signature.ToArray());
            var lambda = Expression.Lambda(delegateType, body, parameters);

            // Compile into a real method of a dynamic assembly: the detour engine needs a genuine
            // MethodHandle (RuntimeHelpers.PrepareMethod), which a DynamicMethod cannot provide.
            var typeBuilder = NextGateType(registration.Id);
            var methodBuilder = typeBuilder.DefineMethod("Invoke",
                MethodAttributes.Public | MethodAttributes.Static,
                target.ReturnType,
                parameters.Select(parameter => parameter.Type).ToArray());
            lambda.CompileToMethod(methodBuilder);
            var built = typeBuilder.CreateType();
            return built.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Static);
        }

        private static TypeBuilder NextGateType(int id)
        {
            lock (GateLock)
            {
                if (_gateModule == null)
                {
                    var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                        new AssemblyName("Pcl2Swap.ScopedHooks"), AssemblyBuilderAccess.Run);
                    _gateModule = assembly.DefineDynamicModule("Pcl2Swap.ScopedHooks");
                }
                return _gateModule.DefineType("Gate_" + id,
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
