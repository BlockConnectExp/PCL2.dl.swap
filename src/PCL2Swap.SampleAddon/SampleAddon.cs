using System;
using PCL2Swap.Api;

namespace PCL2Swap.SampleAddon
{
    /// <summary>
    /// Minimal reference addon: records its lifecycle so the harness can assert the package loader
    /// ran end to end (unpack → manifest validation → ISwapAddon discovery → OnLoad/OnEnable).
    /// Doubles as the packaging example referenced by the addon-format documentation.
    /// </summary>
    public sealed class SampleAddon : ISwapAddon
    {
        public static int LoadCalls;
        public static int EnableCalls;
        public static int DisableCalls;
        public static int UnloadCalls;
        public static string LastApiVersion;

        public void OnLoad(SwapContext context)
        {
            LoadCalls++;
            LastApiVersion = context?.Api?.Host?.ApiVersion;
            context?.Api?.Data?.Set("loaded", "1");
            context?.Api?.Data?.Save();
        }

        public void OnEnable() => EnableCalls++;
        public void OnDisable() => DisableCalls++;
        public void OnUnload() => UnloadCalls++;
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
