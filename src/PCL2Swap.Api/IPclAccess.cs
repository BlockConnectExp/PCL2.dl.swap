using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Readable wrappers over PCL's own subsystems. Each call lands on the real host method -
    /// including the swap's own hooks - so addons compose with the platform instead of bypassing it.
    /// Members that are absent in some build throw or degrade exactly as documented per member.
    /// </summary>
    public interface IPclAccess
    {
        IPclSecretAccess Secret { get; }
        IPclNetAccess Net { get; }
        IPclAnimationAccess Animation { get; }
    }

    /// <summary>PCL.ModSecret - launcher update entry points.</summary>
    public interface IPclSecretAccess
    {
        /// <summary>Calls ModSecret.UpdateStart (silent flag + optional url overrides).</summary>
        void UpdateStart(bool silent, IReadOnlyList<string> urlsOverride = null);

        bool HasUpdatePermission { get; }

        void UpdateCheckByButton();
    }

    /// <summary>PCL.ModNet - the launcher's own download entry points.</summary>
    public interface IPclNetAccess
    {
        /// <summary>Calls NetDownloadByLoader (multi-source list overload).</summary>
        void DownloadByLoader(IReadOnlyList<string> urls, string localPath,
            object loader = null, object checker = null, bool simulateBrowserHeaders = false);

        /// <summary>Calls NetDownloadByClient (single-url overload).</summary>
        void DownloadByClient(string url, string localPath, bool simulateBrowserHeaders = false);
    }

    /// <summary>PCL.ModAnimation - the launcher's animation gate and player.</summary>
    public interface IPclAnimationAccess
    {
        /// <summary>AniControlEnabled; reads 0 and ignores writes when the host lacks it.</summary>
        int ControlEnabled { get; set; }

        void Stop(string id);

        /// <summary>Calls AniStart with the provided data + id (remaining parameters use defaults).</summary>
        void Start(object animationData, string id);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
