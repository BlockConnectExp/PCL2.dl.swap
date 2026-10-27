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
    /// Module-scoped bytecode hooks. Addons hand over an ordinary delegate (instance or static) and
    /// the platform does the rest:
    ///
    ///  * one dynamic gate per *target method*; the first hook patches the prologue, every further
    ///    hook joins the same chain instead of patching again. Stacking two jumps on one prologue
    ///    corrupts execution - that was measured, not guessed;
    ///  * the gate follows the module toggle: a disabled module's handler is skipped and, when no
    ///    live handler remains, the call goes straight to the host's original method;
    ///  * disposing an attach token removes that handler from the chain immediately.
    ///
    /// Chain semantics per call: run live Prefix handlers in registration order, then run the first
    /// live Replace handler (which owns the result) or the original method when there is none, then
    /// run live Postfix handlers in order.
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
            public volatile bool Active = true;
            public long Calls;
            public long Skipped;

            public bool IsLive => Active && SwapHost.AddonEnabled(AddonId);

            public object Call(object[] arguments)
            {
                Interlocked.Increment(ref Calls);
                return Handler.DynamicInvoke(arguments);
            }

            public void MarkSkipped() => Interlocked.Increment(ref Skipped);
        }

        internal sealed class Chain
        {
            public int Id;
            public MethodInfo Target;
            public DetourInstaller Installer;
            public readonly List<Registration> Registrations = new List<Registration>();

            public Registration[] Snapshot()
            {
                lock (Registrations) return Registrations.ToArray();
            }
        }

        private static readonly object GateLock = new object();
        private static readonly ConcurrentDictionary<int, Chain> ChainsById =
            new ConcurrentDictionary<int, Chain>();
        private static readonly Dictionary<MethodInfo, Chain> ChainsByTarget =
            new Dictionary<MethodInfo, Chain>();
        private static ModuleBuilder _gateModule;
        private static int _nextChainId;
        private static int _nextRegistrationId;

        /// <summary>
        /// Attaches one module handler to a host method, creating the chain (and the single gate
        /// detour) on first use.
        /// </summary>
        internal static IDisposable Attach(string addonId, string hookId, HookMode mode,
            Delegate handler, MethodInfo target)
        {
            Chain chain;
            Registration registration;
            var installedNow = false;

            lock (GateLock)
            {
                if (!ChainsByTarget.TryGetValue(target, out chain))
                {
                    chain = new Chain
                    {
                        Id = Interlocked.Increment(ref _nextChainId),
                        Target = target
                    };
                    ChainsByTarget[target] = chain;
                    ChainsById[chain.Id] = chain;
                }

                registration = new Registration
                {
                    Id = Interlocked.Increment(ref _nextRegistrationId),
                    AddonId = addonId,
                    HookId = hookId,
                    Mode = mode,
                    Handler = handler
                };
                lock (chain.Registrations) chain.Registrations.Add(registration);

                if (chain.Installer == null)
                {
                    var gate = BuildGate(target, chain);
                    chain.Installer = DetourInstaller.Install(target, gate);
                    installedNow = true;
                }
            }

            SwapLog.Info($"[hookbus] {(installedNow ? "新建钩子链并装载" : "加入既有钩子链")}：" +
                         $"{target.DeclaringType?.Name}.{target.Name} ← {addonId} · {mode} · " +
                         $"{(string.IsNullOrEmpty(hookId) ? "(unnamed)" : hookId)}" +
                         $"（当前{(SwapHost.AddonEnabled(addonId) ? "启用" : "禁用")}）");
            return new ScopedToken(chain, registration);
        }

        internal static void Deactivate(Chain chain, Registration registration)
        {
            registration.Active = false;
            lock (chain.Registrations) chain.Registrations.Remove(registration);
            SwapLog.Info($"[hookbus] 模块钩子已移除：{registration.AddonId} · " +
                         $"{(string.IsNullOrEmpty(registration.HookId) ? "(unnamed)" : registration.HookId)}" +
                         (chain.Registrations.Count == 0
                             ? "（链路已空，调用继续直通原方法）"
                             : $"（链上剩 {chain.Registrations.Count} 个 handler）"));
        }

        internal static IReadOnlyCollection<string> Snapshot() =>
            ChainsById.Values
                .SelectMany(chain => chain.Snapshot().Select(registration =>
                    $"{chain.Target.DeclaringType?.Name}.{chain.Target.Name} · {registration.AddonId} · " +
                    $"{registration.Mode}：命中 {registration.Calls} 次，跳过 {registration.Skipped} 次"))
                .ToList();

        /// <summary>Entry point of every generated gate (called with the target's ABI arguments).</summary>
        public static object InvokeChain(int chainId, object self, object[] args)
        {
            if (!ChainsById.TryGetValue(chainId, out var chain)) return null;
            var registrations = chain.Snapshot();
            var arguments = BuildArguments(self, args);

            foreach (var registration in registrations)
            {
                if (registration.Mode != HookMode.Prefix) continue;
                if (registration.IsLive) registration.Call(arguments);
                else registration.MarkSkipped();
            }

            object result = null;
            var replaced = false;
            foreach (var registration in registrations)
            {
                if (registration.Mode != HookMode.Replace) continue;
                if (!registration.IsLive)
                {
                    registration.MarkSkipped();
                    continue;
                }
                result = registration.Call(arguments);
                replaced = true;
                break;
            }

            if (!replaced)
            {
                var installer = chain.Installer;
                result = installer == null ? null : installer.InvokeOriginal(self, args ?? new object[0]);
            }

            foreach (var registration in registrations)
            {
                if (registration.Mode != HookMode.Postfix) continue;
                if (registration.IsLive) registration.Call(arguments);
                else registration.MarkSkipped();
            }

            return result;
        }

        private static object[] BuildArguments(object self, object[] args)
        {
            if (self == null) return args ?? new object[0];
            var all = new object[(args?.Length ?? 0) + 1];
            all[0] = self;
            if (args != null) Array.Copy(args, 0, all, 1, args.Length);
            return all;
        }

        /// <summary>
        /// Builds the gate method for one target: same ABI (value parameters keep their type,
        /// reference parameters are object), body forwards to <see cref="InvokeChain"/>.
        /// </summary>
        internal static MethodInfo BuildGate(MethodInfo target, Chain chain)
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
                typeof(ScopedHook).GetMethod(nameof(InvokeChain), BindingFlags.Public | BindingFlags.Static),
                Expression.Constant(chain.Id),
                self == null
                    ? (Expression)Expression.Constant(null, typeof(object))
                    : Expression.Convert(self, typeof(object)),
                Expression.NewArrayInit(typeof(object), boxed));

            var body = target.ReturnType == typeof(void)
                ? (Expression)Expression.Block(call, Expression.Empty())
                : Expression.Convert(call, target.ReturnType);

            var signature = parameters.Select(parameter => parameter.Type).ToList();
            signature.Add(target.ReturnType);
            // GetDelegateType needs at least one parameter plus the return type; a parameterless
            // void target (e.g. ModBase.GetUuid) is exactly the plain Action case.
            var delegateType = signature.Count == 1 && signature[0] == typeof(void)
                ? typeof(Action)
                : Expression.GetDelegateType(signature.ToArray());
            var lambda = Expression.Lambda(delegateType, body, parameters);

            // Compile into a real method of a dynamic assembly: the detour engine needs a genuine
            // MethodHandle (RuntimeHelpers.PrepareMethod), which a DynamicMethod cannot provide.
            var typeBuilder = NextGateType(chain.Id);
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
            if (_gateModule == null)
            {
                var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName("Pcl2Swap.ScopedHooks"), AssemblyBuilderAccess.Run);
                _gateModule = assembly.DefineDynamicModule("Pcl2Swap.ScopedHooks");
            }
            return _gateModule.DefineType("Gate_" + id,
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        }

        private sealed class ScopedToken : IDisposable
        {
            private readonly Chain _chain;
            private readonly Registration _registration;
            private int _disposed;

            public ScopedToken(Chain chain, Registration registration)
            {
                _chain = chain;
                _registration = registration;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Deactivate(_chain, _registration);
            }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
