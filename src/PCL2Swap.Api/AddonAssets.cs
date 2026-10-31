using System.Collections.Generic;
using System.IO;

namespace PCL2Swap.Api
{
    /// <summary>One file shipped under an addon package's assets directory.</summary>
    public sealed class AddonAssetInfo
    {
        /// <summary>Normalized path relative to the assets root, using forward slashes.</summary>
        public string Path { get; set; }

        public long Length { get; set; }

        /// <summary>Lower-case SHA-256 of the packaged file.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Read-only access to the files an addon shipped under <c>assets/</c>. Paths stay inside that
    /// directory; use <see cref="ISwapApi.Files"/> for mutable private storage instead.
    /// </summary>
    public interface IAddonAssets
    {
        /// <summary>Absolute package asset directory, or an empty string when the addon has none.</summary>
        string Root { get; }

        IReadOnlyList<AddonAssetInfo> Assets { get; }

        IReadOnlyList<string> List(string relativeDirectory = null, string searchPattern = "*",
            bool recursive = true);

        bool Exists(string relativePath);
        string GetPath(string relativePath);
        string ReadText(string relativePath);
        byte[] ReadBytes(string relativePath);
        Stream OpenRead(string relativePath);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
