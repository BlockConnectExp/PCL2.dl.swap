using System;
using PCL2Swap.Api;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// The three default addons. In the current build their behaviour is still installed by the
    /// payload core (CoreInstaller / DownloadPageRestore / AnnouncementGuard); these classes carry
    /// identity, metadata and lifecycle so the Swap Addon page can list and manage them today.
    /// Moving each implementation into its addon class is the P1 migration step.
    /// </summary>
    internal abstract class BuiltInAddonBase : ISwapAddon
    {
        protected SwapContext Context { get; private set; }

        public void OnLoad(SwapContext context)
        {
            Context = context;
            context.Api.Log($"内置 Addon 装载：{GetType().Name}");
        }

        public virtual void OnEnable() =>
            Context?.Api.Log($"内置 Addon 启用：{GetType().Name}（核心实现由宿主装配阶段完成）");

        public virtual void OnDisable() =>
            Context?.Api.Log($"内置 Addon 停用请求：{GetType().Name}（热卸载在路线图上）");

        public virtual void OnUnload() =>
            Context?.Api.Log($"内置 Addon 卸载：{GetType().Name}");
    }

    /// <summary>Download MC — restores the vanilla download page (LoaderInit re-drive + sidebar fix).</summary>
    internal sealed class BuiltInDownloadMcAddon : BuiltInAddonBase
    {
    }

    /// <summary>OTA Protector — silent OTA becomes an explicit prompt; launcher-file downloads are scanned.</summary>
    internal sealed class BuiltInOtaProtectorAddon : BuiltInAddonBase
    {
    }

    /// <summary>Cancel Button — appends a cancel button to dialogs that have none.</summary>
    internal sealed class BuiltInCancelButtonAddon : BuiltInAddonBase
    {
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
