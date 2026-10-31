# Demo Addon

The smallest complete PCL2 Swap addon: one settings page, one More page, persisted data and the
API 1.8 surfaces: Table / EmptyState / CodeBlock, private files, module messaging,
themed form widgets and two sidebar page slots.

## Build and pack

```powershell
# builds DemoAddon.dll and packs demo.sample.swapaddon into this folder
.\build.ps1
```

The build references `src\PCL2Swap.Api\bin\Release\net48\PCL2Swap.Api.dll` by default; pass
`-ApiPath` when using another SDK build.

## Install

- PCL2 Swap → Swap Addon → 安装模块… → choose `demo.sample.swapaddon`, or
- drop it into `<数据根>\addons` and restart PCL.

The manifest declares `permissions: ["pages","files","clipboard"]`, so this module runs in strict
mode; calling a gated API it did not declare (for example `Pcl.Secret.*`) is rejected.

## What to copy into your own module

1. `DemoAddon.csproj` — net48 + WPF, references `PCL2Swap.Api.dll` with `Private=false`;
2. `DemoAddon.cs` — lifecycle + pages + widgets;
3. `build.ps1` — build + `PclAddonPack` with id/name/version/permissions.

Hook example (kept out of the default template so installing the demo cannot change launcher
behaviour):

```csharp
// Requires "hooks" in manifest.permissions.
var token = api.Hooks.Attach(
    "PCL.ModBase", "ToUnixTimestampUtc", "demo.hook",
    (Func<DateTime, long>)(time => 42L), HookMode.Replace);
// token.Dispose() restores the original method.
```
