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
        IPclSettingsAccess Settings { get; }
        IPclBaseAccess Base { get; }
        IPclInstanceAccess Instance { get; }
        IPclDownloadAccess Download { get; }
        IPclLaunchAccess Launch { get; }
    }

    /// <summary>PCL.ModMinecraft - instance folders and the selected instance.</summary>
    public interface IPclInstanceAccess
    {
        string FolderSelected { get; set; }

        /// <summary>The selected PCL.ModMinecraft.McInstance object (shape lives in the host).</summary>
        object InstanceSelected { get; set; }

        string GetFriendlyName(string folderName);

        /// <summary>Enumerates the instances the host sees in the selected folder.</summary>
        IReadOnlyList<object> List(string folder = null);

        string GetName(object instance);
        string GetVersion(object instance);
        string GetPathIndie(object instance);
        bool IsModable(object instance);
        string GetJsonPath(object instance);
        string GetVersionDisplayName(object instance);

        /// <summary>ModLaunch.GetNativesFolder - where the host extracts the instance's natives.</summary>
        string GetNativesFolder(object instance);
    }

    /// <summary>PCL.ModDownload - the launcher's own download source plumbing.</summary>
    public interface IPclDownloadAccess
    {
        bool PreferMojang { get; }
        bool PreferMojangVersionList { get; }

        /// <summary>DlClientListGet(source) - the version list the host builds per source.</summary>
        object ClientListGet(string source);

        IReadOnlyList<string> SourceAssetsGet(string version);
        IReadOnlyList<string> SourceLibraryGet(string version);
        IReadOnlyList<string> SourceLauncherOrMetaGet(string version);
        string SourceModGet(string project);

        /// <summary>DlModRequest - the host's own signed request helper.</summary>
        object ModRequest(string url, string method = "GET", string body = null, string contentType = null);
    }

    /// <summary>PCL.ModLaunch - launching the selected instance and login facts.</summary>
    public interface IPclLaunchAccess
    {
        /// <summary>McLaunchStart(options) - starts the launcher's real launch pipeline.</summary>
        bool Start(object launchOptions);

        string LoginName();

        /// <summary>Empty when the current login data is usable, otherwise the reason.</summary>
        string LoginAble();
    }

    /// <summary>PCL.Settings - the launcher's settings storage (global and per-instance).</summary>
    public interface IPclSettingsAccess
    {
        string Get(string key);
        void Set(string key, object value);
        bool HasSaved(string key);
        void Reset(string key);
        object GetDefault(string key);
    }

    /// <summary>PCL.ModBase - small host utilities (uuid, ini files).</summary>
    public interface IPclBaseAccess
    {
        string GetUuid();
        string ReadIni(string file, string section, string key);
        void WriteIni(string file, string section, string key, string value);
        bool HasIniKey(string file, string section, string key);
        void DeleteIniKey(string file, string section, string key);
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
