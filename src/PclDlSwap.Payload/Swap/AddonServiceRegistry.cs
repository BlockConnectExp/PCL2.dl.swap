using System;
using System.Collections.Generic;
using System.Linq;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>Thread-safe, module-owned service registry shared by every scoped API.</summary>
    internal static class AddonServiceRegistry
    {
        private sealed class Entry
        {
            public long RegistrationId;
            public string ServiceId;
            public string Provider;
            public object Instance;
            public SwapServiceInfo Info;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static long _nextRegistrationId;

        public static event EventHandler<SwapServiceChangedEventArgs> Changed;

        public static IReadOnlyList<SwapServiceInfo> Snapshot(string provider = null) =>
            Snapshot(provider, null);

        public static IReadOnlyList<SwapServiceInfo> Snapshot(string provider, string contractType)
        {
            lock (Gate)
                return Entries.Values
                    .Where(entry =>
                        (string.IsNullOrWhiteSpace(provider) ||
                         string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrWhiteSpace(contractType) ||
                         string.Equals(entry.Info.ContractType, contractType, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(entry => entry.ServiceId, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => Clone(entry.Info))
                    .ToList();
        }

        public static IDisposable Register(string provider, string serviceId, object service,
            string version, string description)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            serviceId = NormalizeServiceId(serviceId);
            provider = string.IsNullOrWhiteSpace(provider) ? "host" : provider.Trim();

            var entry = new Entry
            {
                RegistrationId = System.Threading.Interlocked.Increment(ref _nextRegistrationId),
                ServiceId = serviceId,
                Provider = provider,
                Instance = service,
                Info = new SwapServiceInfo
                {
                    ServiceId = serviceId,
                    Provider = provider,
                    Version = string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim(),
                    Description = description ?? string.Empty,
                    ContractType = service.GetType().FullName ?? service.GetType().Name,
                    RegisteredAtUtc = DateTime.UtcNow
                }
            };

            lock (Gate)
            {
                if (Entries.TryGetValue(serviceId, out var existing))
                    throw new InvalidOperationException(
                        $"服务 id \"{serviceId}\" 已由模块 {existing.Provider} 注册。");
                Entries.Add(serviceId, entry);
            }
            RaiseChanged(entry.Info, SwapServiceChangeKind.Registered);
            return new ServiceToken(entry.RegistrationId, provider, serviceId);
        }

        public static bool TryGet(string serviceId, Type contractType, out object service)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            serviceId = NormalizeServiceId(serviceId);
            lock (Gate)
            {
                if (!Entries.TryGetValue(serviceId, out var entry) ||
                    !contractType.IsInstanceOfType(entry.Instance))
                {
                    service = null;
                    return false;
                }
                service = entry.Instance;
                return true;
            }
        }

        public static object Get(string serviceId, Type contractType)
        {
            if (TryGet(serviceId, contractType, out var service)) return service;
            SwapServiceInfo known;
            lock (Gate)
            {
                Entries.TryGetValue(NormalizeServiceId(serviceId), out var entry);
                known = entry == null ? null : Clone(entry.Info);
            }
            if (known == null)
                throw new InvalidOperationException($"服务 \"{serviceId}\" 尚未注册。");
            throw new InvalidOperationException(
                $"服务 \"{serviceId}\" 由 {known.Provider} 提供，但契约必须是 {contractType.FullName}；" +
                $"实际类型为 {known.ContractType}。");
        }

        public static int RemoveAll(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return 0;
            List<SwapServiceInfo> removed;
            lock (Gate)
            {
                removed = Entries.Values
                    .Where(entry => string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => Clone(entry.Info))
                    .ToList();
                foreach (var info in removed) Entries.Remove(info.ServiceId);
            }
            foreach (var info in removed)
                RaiseChanged(info, SwapServiceChangeKind.Unregistered);
            return removed.Count;
        }

        private static bool RemoveOne(string provider, string serviceId, long registrationId)
        {
            SwapServiceInfo removed = null;
            lock (Gate)
            {
                if (!Entries.TryGetValue(serviceId, out var entry) ||
                    entry.RegistrationId != registrationId ||
                    !string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase))
                    return false;
                Entries.Remove(serviceId);
                removed = Clone(entry.Info);
            }
            RaiseChanged(removed, SwapServiceChangeKind.Unregistered);
            return true;
        }

        private static void RaiseChanged(SwapServiceInfo service, SwapServiceChangeKind kind)
        {
            var handlers = Changed;
            if (handlers == null) return;
            var args = new SwapServiceChangedEventArgs { Service = service, Kind = kind };
            foreach (EventHandler<SwapServiceChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(null, args); }
                catch (Exception ex)
                {
                    SwapLog.Warn("[services] Changed subscriber failed: " + ex.Message);
                }
            }
        }

        private static SwapServiceInfo Clone(SwapServiceInfo info) =>
            info == null ? null : new SwapServiceInfo
            {
                ServiceId = info.ServiceId,
                Provider = info.Provider,
                Version = info.Version,
                Description = info.Description,
                ContractType = info.ContractType,
                RegisteredAtUtc = info.RegisteredAtUtc
            };

        private static string NormalizeServiceId(string serviceId)
        {
            var value = (serviceId ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 128)
                throw new ArgumentException("服务 id 长度必须在 1..128 之间。", nameof(serviceId));
            if (value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == ':' ||
                                  ch == '-' || ch == '_' || ch == '/')))
                throw new ArgumentException("服务 id 只能包含字母、数字与 . : - _ /。", nameof(serviceId));
            return value;
        }

        private sealed class ServiceToken : IDisposable
        {
            private readonly long _registrationId;
            private readonly string _provider;
            private readonly string _serviceId;
            private int _disposed;

            public ServiceToken(long registrationId, string provider, string serviceId)
            {
                _registrationId = registrationId;
                _provider = provider;
                _serviceId = serviceId;
            }

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
                RemoveOne(_provider, _serviceId, _registrationId);
            }
        }
    }

    internal sealed class ServicesAccess : IAddonServices
    {
        private readonly string _provider;
        private readonly AddonManager.AddonRecord _record;

        public ServicesAccess(string provider, AddonManager.AddonRecord record = null)
        {
            _provider = string.IsNullOrWhiteSpace(provider) ? "host" : provider.Trim();
            _record = record;
        }

        public IReadOnlyList<SwapServiceInfo> Services
        {
            get
            {
                Require();
                return AddonServiceRegistry.Snapshot();
            }
        }

        public IDisposable Register(string serviceId, object service, string version = null,
            string description = null)
        {
            Require();
            return AddonServiceRegistry.Register(_provider, serviceId, service, version, description);
        }

        public bool TryGet<T>(string serviceId, out T service) where T : class
        {
            Require();
            if (AddonServiceRegistry.TryGet(serviceId, typeof(T), out var value))
            {
                service = (T)value;
                return true;
            }
            service = null;
            return false;
        }

        public bool TryGet(string serviceId, Type contractType, out object service)
        {
            Require();
            return AddonServiceRegistry.TryGet(serviceId, contractType, out service);
        }

        public object Get(string serviceId, Type contractType)
        {
            Require();
            return AddonServiceRegistry.Get(serviceId, contractType);
        }

        public event EventHandler<SwapServiceChangedEventArgs> Changed
        {
            add
            {
                Require();
                AddonServiceRegistry.Changed += value;
            }
            remove
            {
                Require();
                AddonServiceRegistry.Changed -= value;
            }
        }

        private void Require()
        {
            if (_record == null) return;
            var manager = SwapHost.Addons;
            if (manager == null) return;
            if (!manager.IsEnabled(_record.Id))
                throw new InvalidOperationException(
                    $"模块 {_record.Id} 当前未启用，不能访问服务注册表。");
            manager.RequirePermission(_record, "services");
        }
    }
}
