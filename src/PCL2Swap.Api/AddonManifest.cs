using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// One payload file of a package and its SHA-256. The packer writes this list into
    /// <c>manifest.json</c> *before* signing it, so the signature covers the payload hashes too.
    /// </summary>
    public sealed class AddonFileEntry
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }

        public override string ToString() => (Path ?? "(unnamed)") + " " + (Sha256 ?? "(no hash)");
    }

    /// <summary>
    /// One user-selectable feature scope declared by a module. Scopes are independent switches:
    /// the module can omit hooks, tools or pages that the user does not need.
    /// </summary>
    public sealed class AddonScopeDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string NameEnglish { get; set; }
        public string Description { get; set; }
        public string DescriptionEnglish { get; set; }
        public bool DefaultEnabled { get; set; } = true;

        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? (Id ?? "(unnamed)") : Name + " (" + Id + ")";
    }

    /// <summary>
    /// Addon package manifest (manifest.json inside a .swapaddon archive). The host parses it into
    /// this model; see docs/swap-api-research.zh.md section 4 for the schema draft.
    /// </summary>
    public sealed class AddonManifest
    {
        public int Schema { get; set; } = 1;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string ApiVersion { get; set; } = ">=1.0 <2.0";
        public string Entry { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public List<string> Shapes { get; set; } = new List<string>();

        /// <summary>
        /// Which PCL builds the module is scoped to. Entries are OR-ed; each is either an exact
        /// version ("2.99.0.0"), a wildcard ("*") or a comparison (">=2.99.0", "&lt;3.0", "=2.99.0.1").
        /// An empty list means "any build" (the Alpha 9 behaviour).
        /// </summary>
        public List<string> HostVersions { get; set; } = new List<string>();

        /// <summary>
        /// Optional feature scopes shown as user-controlled switches in the module detail page.
        /// A string list entry is shorthand for <c>{ id, name=id, defaultEnabled=true }</c>.
        /// </summary>
        public List<AddonScopeDefinition> Scopes { get; set; } =
            new List<AddonScopeDefinition>();

        public List<string> Permissions { get; set; } = new List<string>();
        public bool DefaultEnabled { get; set; }

        /// <summary>
        /// Payload integrity list (file -> SHA-256) written by the packer. Empty for packages built
        /// before the trust chain (v26.1) - those are simply "no file list".
        /// </summary>
        public List<AddonFileEntry> Files { get; set; } = new List<AddonFileEntry>();

        public override string ToString() =>
            (Name ?? Id ?? "(unnamed)") + (string.IsNullOrEmpty(Version) ? string.Empty : " " + Version);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
