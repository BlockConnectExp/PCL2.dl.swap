using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Read-only feature-scope view for the current module. Users edit these switches in the
    /// built-in module detail page; addons use this surface to enable or omit optional features.
    /// Localized names come from the manifest's nameEn / descriptionEn fields.
    /// </summary>
    public interface IModuleScopes
    {
        IReadOnlyList<SwapScopeInfo> Scopes { get; }
        IReadOnlyList<string> Enabled { get; }
        bool IsEnabled(string scopeId);
        event EventHandler Changed;
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
