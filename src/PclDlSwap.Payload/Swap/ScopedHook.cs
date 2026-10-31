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
            public string ScopeId;
            public HookMode Mode;
            public Delegate Handler;
            public volatile bool Active = true;
            public long Calls;
            public long Skipped;
            public long Errors;

            public bool IsLive => Active &&
                                  SwapHost.AddonEnabled(AddonId) &&
                                  SwapHost.AddonPermissionGranted(AddonId, "hooks") &&
                                  SwapHost.AddonScopeEnabled(AddonId, ScopeId);

            /// <summary>
            /// Marker returned when a module handler threw: a Replace handler that fails must not own
            /// the result (the chain would otherwise hand the host a null), so InvokeChain treats it
            /// as "no handler" and falls back to the original method.
            /// </summary>
            internal static readonly object CallFailed = new object();

            public object Call(object[] arguments, SwapHookContext context)
            {
                Interlocked.Increment(ref Calls);
                try
                {
                    using (context?.Activate())
                        return Handler.DynamicInvoke(arguments);
                }
                catch (Exception ex)
                {
                    // Module crashes are isolated from the launcher: the handler is deactivated so it
                    // cannot break the same host path twice, the failure is counted for the module
                    // list, and it lands in the module's own log.
                    Interlocked.Increment(ref Errors);
                    Active = false;
                    var inner = ex is System.Reflection.TargetInvocationException invocation &&
                                invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;
                    SwapLog.Error($"[hookbus] 模块 handler 抛错，已自动停用（{AddonId} · {HookId}）：" +
                                  $"{inner.GetType().Name}: {inner.Message}");
                    ModuleLog.Write(AddonId, "error",
                        $"hook {HookId}（{Mode}）抛错，已自动停用：{inner.GetType().Name}: {inner.Message}", inner);
                    return CallFailed;
                }
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
        internal static IDisposable Attach(string addonId, string hookId, string scopeId,
            HookMode mode, Delegate handler, MethodInfo target)
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
                    ScopeId = scopeId,
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
                         $"{(string.IsNullOrEmpty(hookId) ? "(unnamed)" : hookId)} · " +
                         $"scope {(string.IsNullOrEmpty(scopeId) ? "*" : scopeId)}" +
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

        /// <summary>
        /// Drops every handler one module owns. Uninstall and reload need this: a retired module's
        /// registrations would otherwise stay live (an unknown addon id counts as enabled), so its
        /// hooks would keep running - and a reload would run the previous generation's Prefix handler
        /// twice. Returns how many handlers were removed.
        /// </summary>
        internal static int RemoveAll(string addonId)
        {
            if (string.IsNullOrEmpty(addonId)) return 0;

            var removed = 0;
            foreach (var chain in ChainsById.Values)
            {
                foreach (var registration in chain.Snapshot())
                {
                    if (!string.Equals(registration.AddonId, addonId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Deactivate(chain, registration);
                    removed++;
                }
            }

            if (removed > 0)
                SwapLog.Info($"[hookbus] 已清理模块 {addonId} 的 {removed} 个钩子注册。");
            return removed;
        }

        internal static IReadOnlyCollection<string> Snapshot() =>
            ChainsById.Values
                .SelectMany(chain => chain.Snapshot().Select(registration =>
                    $"{chain.Target.DeclaringType?.Name}.{chain.Target.Name} · {registration.AddonId} · " +
                    $"{registration.ScopeId ?? "*"} · {registration.Mode}：命中 {registration.Calls} 次，" +
                    $"跳过 {registration.Skipped} 次"))
                .ToList();

        /// <summary>How many hooks one module currently has on the chains (for the module list).</summary>
        internal static int CountFor(string addonId) =>
            string.IsNullOrEmpty(addonId)
                ? 0
                : ChainsById.Values.Sum(chain => chain.Snapshot()
                    .Count(registration => string.Equals(registration.AddonId, addonId,
                        StringComparison.OrdinalIgnoreCase)));

        /// <summary>Total handler hits one module accumulated (for the module list).</summary>
        internal static long HitsFor(string addonId) =>
            string.IsNullOrEmpty(addonId)
                ? 0
                : ChainsById.Values.Sum(chain => chain.Snapshot()
                    .Where(registration => string.Equals(registration.AddonId, addonId,
                        StringComparison.OrdinalIgnoreCase))
                    .Sum(registration => registration.Calls));

        /// <summary>How often one module's handlers threw (they are deactivated on the first throw).</summary>
        internal static long ErrorsFor(string addonId) =>
            string.IsNullOrEmpty(addonId)
                ? 0
                : ChainsById.Values.Sum(chain => chain.Snapshot()
                    .Where(registration => string.Equals(registration.AddonId, addonId,
                        StringComparison.OrdinalIgnoreCase))
                    .Sum(registration => registration.Errors));

        internal static bool HasRegistrations(MethodInfo target)
        {
            if (target == null) return false;
            lock (GateLock)
                return ChainsByTarget.TryGetValue(target, out var chain) &&
                       chain.Snapshot().Length > 0;
        }

        internal static bool HasChain(MethodInfo target)
        {
            if (target == null) return false;
            lock (GateLock) return ChainsByTarget.ContainsKey(target);
        }

        /// <summary>Entry point of every generated gate (called with the target's ABI arguments).</summary>
        public static object InvokeChain(int chainId, object self, object[] args)
        {
            if (!ChainsById.TryGetValue(chainId, out var chain)) return null;
            var registrations = chain.Snapshot();
            var arguments = BuildArguments(self, args);

            foreach (var registration in registrations)
            {
                if (registration.Mode != HookMode.Prefix) continue;
                if (registration.IsLive)
                    registration.Call(arguments, BuildContext(chain, registration, self, args));
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
                result = registration.Call(arguments, BuildContext(chain, registration, self, args));
                if (ReferenceEquals(result, Registration.CallFailed))
                {
                    // The module's Replace handler blew up and deactivated itself; keep looking for a
                    // live replacement (or fall back to the host's original method below).
                    replaced = false;
                    continue;
                }
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
                if (registration.IsLive)
                    registration.Call(arguments, BuildContext(chain, registration, self, args));
                else registration.MarkSkipped();
            }

            return result;
        }

        private static SwapHookContext BuildContext(Chain chain, Registration registration,
            object self, object[] arguments)
        {
            var target = chain?.Target;
            var signature = target == null
                ? string.Empty
                : (target.DeclaringType?.FullName ?? "?") + "." + target.Name + "(" +
                  string.Join(", ", target.GetParameters()
                      .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)) +
                  ")";
            return new SwapHookContext(
                registration?.AddonId, registration?.HookId, registration?.Mode ?? HookMode.Prefix,
                signature, self, arguments, overrideArguments =>
                {
                    var installer = chain?.Installer;
                    return installer == null
                        ? null
                        : installer.InvokeOriginal(self, overrideArguments ?? new object[0]);
                });
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
