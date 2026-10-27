using System;
using System.Windows;

namespace PCL2Swap.Api
{
    public enum SwapShape
    {
        Injected,
        Patched
    }

    public enum HookMode
    {
        /// <summary>Handler runs before the target; the original still runs afterwards.</summary>
        Prefix,
        /// <summary>Handler runs after the target returned.</summary>
        Postfix,
        /// <summary>Handler replaces the target entirely; the original stays reachable for the host.</summary>
        Replace
    }

    /// <summary>Static facts about the running swap host.</summary>
    public interface IHostInfo
    {
        string SwapVersion { get; }
        string ApiVersion { get; }
        SwapShape Shape { get; }
        int ProcessId { get; }
        string LogDirectory { get; }
        string AddonDirectory { get; }
        /// <summary>The PCL assembly the swap is bound to (may be null before the host resolves it).</summary>
        object PclAssembly { get; }
    }

    /// <summary>Bytecode-hook surface. Attach() returns a token that detaches when disposed.</summary>
    public interface IHooks
    {
        IDisposable Attach(string declaringType, string methodName, string hookId, Delegate handler, HookMode mode);
        bool IsHooked(string declaringType, string methodName);
        event EventHandler<DownloadEventArgs> DownloadCompleted;
        event EventHandler<DialogEventArgs> DialogOpening;
        event EventHandler<UpdateEventArgs> UpdateRequested;
    }

    /// <summary>UI surface: pages, hints and the native PCL message box.</summary>
    public interface IUi
    {
        /// <summary>Pages are inserted into 设置 sidebar as "Swap Addon" style entries.</summary>
        void RegisterSettingsPage(string id, string title, Func<FrameworkElement> viewFactory);
        void Hint(string text);
        int MsgBox(string caption, string title, string button1 = "确定", string button2 = "");
    }

    /// <summary>Per-addon persisted key/value store.</summary>
    public interface IData
    {
        string Get(string key, string fallback = null);
        void Set(string key, string value);
        void Save();
    }

    /// <summary>Escape hatch: any host member stays reachable even when the typed surface lags.</summary>
    public interface IReflection
    {
        Type HostType(string fullName);
        object GetMember(object instanceOrType, string name);
        void SetMember(object instanceOrType, string name, object value);
        object Call(object instanceOrType, string name, params object[] args);
    }

    public interface ISwapApi
    {
        IHostInfo Host { get; }
        IHooks Hooks { get; }
        IUi Ui { get; }
        IData Data { get; }
        IReflection Reflect { get; }
        void Log(string message);
        void LogWarning(string message);
        void LogError(string message, Exception exception = null);
    }

    public sealed class DownloadEventArgs : EventArgs
    {
        public string[] Urls { get; set; }
        public string LocalPath { get; set; }
        public bool IsPclCloud { get; set; }
    }

    public sealed class DialogEventArgs : EventArgs
    {
        public string Caption { get; set; }
        public string Title { get; set; }
        public bool IsWarn { get; set; }
        public bool ForceWait { get; set; }
        public string Button2 { get; set; }
        public bool CancelAdded { get; set; }
    }

    public sealed class UpdateEventArgs : EventArgs
    {
        public bool Silent { get; set; }
        public string[] Urls { get; set; }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
