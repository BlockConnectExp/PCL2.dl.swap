using System;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Ambient context available inside a Swap hook handler. Replace handlers can call
    /// <see cref="InvokeOriginal(object[])"/> without recursively entering their own hook chain.
    /// </summary>
    public sealed class SwapHookContext
    {
        [ThreadStatic] private static SwapHookContext _current;

        private readonly Func<object[], object> _invokeOriginal;
        private readonly object[] _originalArguments;

        public SwapHookContext(string addonId, string hookId, HookMode mode, string target,
            object instance, object[] originalArguments, Func<object[], object> invokeOriginal)
        {
            AddonId = addonId ?? string.Empty;
            HookId = hookId ?? string.Empty;
            Mode = mode;
            Target = target ?? string.Empty;
            Instance = instance;
            _originalArguments = Clone(originalArguments);
            _invokeOriginal = invokeOriginal;
        }

        public static SwapHookContext Current => _current;

        public string AddonId { get; }
        public string HookId { get; }
        public HookMode Mode { get; }
        public string Target { get; }
        public object Instance { get; }

        public object[] Arguments => Clone(_originalArguments);

        /// <summary>
        /// Calls the original method through the existing IL copy. Passing no arguments reuses the
        /// current call's arguments; passing an array overrides them.
        /// </summary>
        public object InvokeOriginal(params object[] arguments)
        {
            if (_invokeOriginal == null) return null;
            var selected = arguments == null || arguments.Length == 0
                ? _originalArguments
                : arguments;
            return _invokeOriginal(Clone(selected));
        }

        /// <summary>Activates this context for the duration of one handler invocation.</summary>
        public IDisposable Activate()
        {
            var previous = _current;
            _current = this;
            return new Scope(previous);
        }

        private static object[] Clone(object[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<object>();
            var copy = new object[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        private sealed class Scope : IDisposable
        {
            private readonly SwapHookContext _previous;
            private bool _disposed;

            public Scope(SwapHookContext previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current = _previous;
            }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
