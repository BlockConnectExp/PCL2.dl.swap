using System;
using System.Collections.Generic;
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

    public enum SwapHintType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>A live progress row returned by IUi.Progress.</summary>
    public interface IProgressView
    {
        FrameworkElement Element { get; }
        double Value { get; set; }
        bool IsIndeterminate { get; set; }
        string Status { get; set; }
    }

    /// <summary>Static facts about the running swap host.</summary>
    public interface IHostInfo
    {
        string SwapVersion { get; }
        string ApiVersion { get; }

        /// <summary>
        /// The PCL build the swap is bound to ("2.99.0.0"). Module scopes compare their
        /// <c>hostVersions</c> manifest entries against this.
        /// </summary>
        string PclVersion { get; }

        SwapShape Shape { get; }
        int ProcessId { get; }
        string LogDirectory { get; }
        string AddonDirectory { get; }
        /// <summary>True when this process skipped addons after an unclean-start recovery decision.</summary>
        bool SafeMode { get; }
        /// <summary>Why safe mode was entered; empty in a normal start.</summary>
        string SafeModeReason { get; }
        /// <summary>Directory containing the startup journal and recovery diagnostics.</summary>
        string RecoveryDirectory { get; }
        /// <summary>The PCL assembly the swap is bound to (may be null before the host resolves it).</summary>
        object PclAssembly { get; }
    }

    /// <summary>
    /// Read-only startup recovery facts. Mutation stays in the built-in recovery UI so a module
    /// cannot silently clear safe mode or disable another module while diagnosing a crash.
    /// </summary>
    public interface IRecoveryAccess
    {
        bool SafeMode { get; }
        bool SafeModeExitPending { get; }
        string SafeModeReason { get; }
        string SafeModeReasonCode { get; }
        string JournalPath { get; }
        int UncleanStarts { get; }
        int SafeModeUncleanStarts { get; }
        string LifecyclePhase { get; }
        string LastStartedModule { get; }
        string LastCompletedModule { get; }
        string SuspectModule { get; }
        string SuspectPhase { get; }
        string Diagnostics();
    }

    /// <summary>Bytecode-hook surface. Attach() returns a token that detaches when disposed.</summary>
    public interface IHooks
    {
        IDisposable Attach(string declaringType, string methodName, string hookId, Delegate handler, HookMode mode);
        IReadOnlyList<SwapMethodTarget> FindTargets(string declaringType, string methodName);
        IDisposable Attach(SwapMethodTarget target, string hookId, Delegate handler, HookMode mode);

        /// <summary>Attach a signature target gated by a user-selectable module scope.</summary>
        IDisposable Attach(SwapMethodTarget target, string hookId, string scopeId,
            Delegate handler, HookMode mode);

        /// <summary>Attach a method-name target gated by a user-selectable module scope.</summary>
        IDisposable Attach(string declaringType, string methodName, string hookId, string scopeId,
            Delegate handler, HookMode mode);
        bool IsHooked(SwapMethodTarget target);
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

        /// <summary>
        /// Pages are inserted into the 更多 (More) sidebar. Ids live in a separate 200+ range, so
        /// both slots can be used by the same module (API 1.2).
        /// </summary>
        void RegisterMorePage(string id, string title, Func<FrameworkElement> viewFactory);
        void Hint(string text);
        void Hint(string text, SwapHintType type);
        int MsgBox(string caption, string title, string button1 = "确定", string button2 = "");

        // ---- native-look builders -------------------------------------------------------------
        // PCL's own MyCard / MyButton with the launcher's theme brushes and spacing, so a module
        // page reads like a shipped settings page. When the host build lacks the controls these
        // degrade to plain WPF and the page still renders.

        /// <summary>A settings card: rounded chrome, bold title, 25/40/25/25 body padding.</summary>
        FrameworkElement Card(string title, params FrameworkElement[] content);

        /// <summary>Themed text (secondary = dimmed).</summary>
        FrameworkElement Text(string text, double size = 12.5, bool secondary = false);

        /// <summary>Native PCL button; colorType is "Normal" / "Highlight" / "Red".</summary>
        FrameworkElement Button(string text, Action onClick, string colorType = null);

        /// <summary>Label/value row aligned like the shipped cards.</summary>
        FrameworkElement InfoRow(string label, string value);

        /// <summary>Horizontal 35px button strip.</summary>
        FrameworkElement Buttons(params FrameworkElement[] buttons);

        /// <summary>Secondary explanation line.</summary>
        FrameworkElement Note(string text);

        /// <summary>
        /// A labelled checkbox row matching PCL's own setting rows (falls back to a stock
        /// WPF CheckBox when the host build lacks MyCheckBox).
        /// </summary>
        FrameworkElement CheckBox(string label, bool initial, Action<bool> onChange);

        /// <summary>
        /// A labelled dropdown row; <paramref name="onChange"/> receives the selected index.
        /// Uses the host's MyComboBox when available, a stock ComboBox otherwise.
        /// </summary>
        FrameworkElement Combo(string label, IReadOnlyList<string> options, int selectedIndex,
            Action<int> onChange);

        // ---- wider front-end builders (API 1.1) -----------------------------------------------
        // The panel shapes addons kept re-implementing by hand. Slider, TextInput and Link mirror
        // the settings rows; Separator is the hairline between groups.

        /// <summary>A labelled slider row; onChange receives the live value.</summary>
        FrameworkElement Slider(string label, double min, double max, double step, double initial,
            Action<double> onChange);

        /// <summary>A labelled text field row; onChange fires on every edit.</summary>
        FrameworkElement TextInput(string label, string initial, Action<string> onChange);

        /// <summary>An underlined link that opens the URL through the launcher's own helper.</summary>
        FrameworkElement Link(string text, string url);

        /// <summary>A themed 1px separator with the page's vertical rhythm.</summary>
        FrameworkElement Separator();

        // ---- richer front-end builders (API 1.3) ------------------------------------------------

        /// <summary>A mutually exclusive choice row; onChange receives the selected index.</summary>
        FrameworkElement RadioGroup(string label, IReadOnlyList<string> options, int selectedIndex,
            Action<int> onChange);

        /// <summary>A numeric field with -/+ controls; the callback fires for valid values.</summary>
        FrameworkElement NumberInput(string label, double initial, double min, double max,
            double step, Action<double> onChange);

        /// <summary>A masked text field that never places the value in the visible text tree.</summary>
        FrameworkElement PasswordInput(string label, string initial, Action<string> onChange);

        /// <summary>A multi-line editor with a stable minimum height.</summary>
        FrameworkElement MultilineTextInput(string label, string initial, int minHeight,
            Action<string> onChange);

        /// <summary>Plays a local image through PCL's MyImage control when available.</summary>
        FrameworkElement Image(string path, double width = 0, double height = 0);

        /// <summary>A live progress row; update the returned handle from any thread.</summary>
        IProgressView Progress(string label, double initial = 0, bool indeterminate = false);

        /// <summary>
        /// A compact status banner with a semantic accent. Use for one important state or recovery
        /// message instead of nesting another card inside the page.
        /// </summary>
        FrameworkElement Banner(string title, string text, SwapHintType type = SwapHintType.Warning);

        /// <summary>A compact semantic status pill for headers and summaries.</summary>
        FrameworkElement Badge(string text, SwapHintType type = SwapHintType.Info);

        /// <summary>A native disclosure section whose content is constructed on demand.</summary>
        FrameworkElement Expander(string title, bool initiallyExpanded,
            Func<FrameworkElement> contentFactory);

        /// <summary>A compact tabbed surface; contentFactory is called once per tab.</summary>
        FrameworkElement Tabs(IReadOnlyList<string> titles, int selectedIndex,
            Func<int, FrameworkElement> contentFactory);

        /// <summary>A compact vertical event timeline using the tool's semantic accent colors.</summary>
        FrameworkElement Timeline(IReadOnlyList<SwapDiagnosticEvent> events, int maxHeight = 260);

        /// <summary>A compact read-only table with weighted columns and vertical scrolling.</summary>
        FrameworkElement Table(IReadOnlyList<string> columns,
            IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<double> columnWeights = null,
            int maxHeight = 280);

        /// <summary>A labelled search field with a clear affordance and live query callback.</summary>
        FrameworkElement SearchBox(string label, string placeholder, string initial,
            Action<string> onChange);

        /// <summary>A compact mutually-exclusive mode switch for two to five short options.</summary>
        FrameworkElement SegmentedControl(string label, IReadOnlyList<string> options,
            int selectedIndex, Action<int> onChange);

        /// <summary>A labelled #RRGGBB color field with a live swatch preview.</summary>
        FrameworkElement ColorInput(string label, string initialHex, Action<string> onChange);

        /// <summary>A compact selectable list for a small fixed set of choices.</summary>
        FrameworkElement SelectList(string label, IReadOnlyList<string> items, int selectedIndex,
            Action<int> onChange);

        /// <summary>A centered zero-content state with an optional single action.</summary>
        FrameworkElement EmptyState(string title, string message,
            FrameworkElement action = null);

        /// <summary>A read-only monospace code block for diagnostics and configuration snippets.</summary>
        FrameworkElement CodeBlock(string text, int maxHeight = 240);

        /// <summary>
        /// Wraps a long module page in PCL's native vertical scroller (falling back to stock WPF).
        /// Page factories should return this when content can exceed the viewport.
        /// </summary>
        FrameworkElement Scroll(FrameworkElement content);
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
        /// <summary>Versioned, atomic configuration backend for this module (API 1.10).</summary>
        IAddonConfig Config { get; }
        IReflection Reflect { get; }
        /// <summary>Machine-readable index of every callable host member (tools/PclSurface dump).</summary>
        ISwapSurface Surface { get; }
        /// <summary>Readable wrappers over PCL's own subsystems (Secret / Net / Animation).</summary>
        IPclAccess Pcl { get; }
        /// <summary>Bilingual text surface shared with the running launcher.</summary>
        II18n I18n { get; }
        /// <summary>Read-only startup recovery state and diagnostics (API 1.5).</summary>
        IRecoveryAccess Recovery { get; }
        /// <summary>Read-only inventory of installed modules and their governance state (API 1.6).</summary>
        IModuleCatalog Modules { get; }
        /// <summary>Bounded runtime diagnostic timeline and export surface (API 1.7).</summary>
        IDiagnosticsAccess Diagnostics { get; }
        /// <summary>Read-only state of the user-selected feature scopes (API 1.9).</summary>
        IModuleScopes Scopes { get; }
        /// <summary>Sandboxed per-module file storage (API 1.8).</summary>
        IAddonFiles Files { get; }
        /// <summary>In-process module message bus (API 1.8).</summary>
        IAddonMessaging Messaging { get; }
        /// <summary>Cross-module service registry (API 1.10).</summary>
        IAddonServices Services { get; }
        /// <summary>Read-only package assets shipped under <c>assets/</c> (API 1.11).</summary>
        IAddonAssets Assets { get; }
        /// <summary>Module-owned, lifetime-scoped localization pack (API 1.11).</summary>
        IAddonLocalization Localization { get; }
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
