using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Durable per-addon configuration. The legacy <see cref="IData"/> methods stay available;
    /// this surface adds schema versioning, atomic saves and transactional migrations.
    /// </summary>
    public interface IAddonConfig : IData
    {
        /// <summary>Current persisted schema. Legacy flat config files load as version 1.</summary>
        int SchemaVersion { get; }

        /// <summary>Snapshot of all current keys, sorted with ordinal comparison.</summary>
        IReadOnlyList<string> Keys { get; }

        /// <summary>Raised after an in-memory value or schema change. Save() alone does not raise it.</summary>
        event EventHandler Changed;

        /// <summary>Removes a key and returns whether it existed.</summary>
        bool Remove(string key);

        /// <summary>
        /// Applies a migration transactionally. The callback edits a staging copy; the schema and
        /// values are committed only when targetVersion is higher and the atomic save succeeds.
        /// Returns false when the store is already at or above the requested version.
        /// </summary>
        bool Migrate(int targetVersion, Action<IAddonConfigTransaction> migration);
    }

    /// <summary>Staging view passed to an <see cref="IAddonConfig.Migrate"/> callback.</summary>
    public interface IAddonConfigTransaction
    {
        IReadOnlyList<string> Keys { get; }
        string Get(string key, string fallback = null);
        void Set(string key, string value);
        bool Remove(string key);
    }
}
