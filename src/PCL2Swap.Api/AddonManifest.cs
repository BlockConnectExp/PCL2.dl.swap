using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
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
        public List<string> Permissions { get; set; } = new List<string>();
        public bool DefaultEnabled { get; set; }

        public override string ToString() =>
            (Name ?? Id ?? "(unnamed)") + (string.IsNullOrEmpty(Version) ? string.Empty : " " + Version);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
