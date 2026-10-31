using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>One declared feature scope and its current user-selected state.</summary>
    public sealed class SwapScopeInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string NameEnglish { get; set; }
        public string Description { get; set; }
        public string DescriptionEnglish { get; set; }
        public bool DefaultEnabled { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>Read-only snapshot of one installed module for dashboards and diagnostics.</summary>
    public sealed class SwapModuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string ApiVersion { get; set; }
        public bool BuiltIn { get; set; }
        public bool Enabled { get; set; }
        public string Status { get; set; }
        public string Directory { get; set; }
        public string LogPath { get; set; }
        public string ConfigPath { get; set; }
        public string TrustKind { get; set; }
        public string Signature { get; set; }
        public IReadOnlyList<string> Shapes { get; set; }
        public IReadOnlyList<string> HostVersions { get; set; }
        public IReadOnlyList<SwapScopeInfo> Scopes { get; set; }
        public int ConfigSchemaVersion { get; set; }
        public int ConfigKeyCount { get; set; }
        public IReadOnlyList<SwapServiceInfo> Services { get; set; }
        public IReadOnlyList<string> RequestedPermissions { get; set; }
        public IReadOnlyList<string> GrantedPermissions { get; set; }
        public IReadOnlyList<string> DeniedPermissions { get; set; }
        public int HookCount { get; set; }
        public long HookHits { get; set; }
        public long HookErrors { get; set; }
    }

    /// <summary>Live, read-only module inventory. Mutations remain in the built-in manager UI.</summary>
    public interface IModuleCatalog
    {
        IReadOnlyList<SwapModuleInfo> Modules { get; }
        SwapModuleInfo Find(string id);
        event EventHandler Changed;
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
