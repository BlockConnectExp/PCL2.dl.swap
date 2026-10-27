using System;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Contract every Swap addon implements. The host calls these four methods on its UI thread;
    /// <see cref="OnLoad"/> is where an addon registers pages, hooks and event handlers.
    /// </summary>
    public interface ISwapAddon
    {
        void OnLoad(SwapContext context);
        void OnEnable();
        void OnDisable();
        void OnUnload();
    }

    /// <summary>Everything an addon is handed when it loads.</summary>
    public sealed class SwapContext
    {
        public SwapContext(ISwapApi api, AddonManifest manifest, string baseDirectory)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
            Manifest = manifest;
            BaseDirectory = baseDirectory ?? string.Empty;
        }

        public ISwapApi Api { get; }

        /// <summary>Null for built-in addons that carry no manifest file.</summary>
        public AddonManifest Manifest { get; }

        /// <summary>Directory the addon was unpacked into (the payload directory for built-ins).</summary>
        public string BaseDirectory { get; }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
