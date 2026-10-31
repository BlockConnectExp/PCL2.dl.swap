using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    public enum SwapServiceChangeKind
    {
        Registered,
        Unregistered
    }

    /// <summary>Read-only description of one service published by a loaded module.</summary>
    public sealed class SwapServiceInfo
    {
        public string ServiceId { get; set; }
        public string Provider { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string ContractType { get; set; }
        public DateTime RegisteredAtUtc { get; set; }
    }

    public sealed class SwapServiceChangedEventArgs : EventArgs
    {
        public SwapServiceInfo Service { get; set; }
        public SwapServiceChangeKind Kind { get; set; }
    }

    /// <summary>
    /// Process-local service registry for cooperation between modules. Registrations are scoped
    /// to their provider and disappear when that module is disabled, reloaded or unloaded.
    /// </summary>
    public interface IAddonServices
    {
        IReadOnlyList<SwapServiceInfo> Services { get; }

        IDisposable Register(string serviceId, object service, string version = null,
            string description = null);

        bool TryGet<T>(string serviceId, out T service) where T : class;
        bool TryGet(string serviceId, Type contractType, out object service);
        object Get(string serviceId, Type contractType);

        event EventHandler<SwapServiceChangedEventArgs> Changed;
    }
}
