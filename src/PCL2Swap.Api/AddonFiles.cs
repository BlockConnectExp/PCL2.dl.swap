using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Convenience file store rooted at this module's private data directory. Paths are relative,
    /// traversal outside the root is rejected, and text is UTF-8 without a BOM.
    /// </summary>
    public interface IAddonFiles
    {
        string Root { get; }
        string GetPath(string relativePath);
        bool Exists(string relativePath);
        string ReadText(string relativePath);
        void WriteText(string relativePath, string contents, bool append = false);
        byte[] ReadBytes(string relativePath);
        void WriteBytes(string relativePath, byte[] contents);
        bool Delete(string relativePath);

        /// <summary>Returns normalized relative paths with forward slashes.</summary>
        IReadOnlyList<string> List(string relativeDirectory = null, string searchPattern = "*",
            bool recursive = false);
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
