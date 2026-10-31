# PCL2.dl.swap v26.2-Alpha 9

> Alpha 9 adds the module-governance layer: a live module catalog, user-editable permission grants,
> signature-aware hook selection, and richer native front-end controls. It moves Swap closer to the
> LSPosed model without changing the existing addon package format.

![Permission governance in Swap Addon](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha9/shot-permission-governance.png)

![Permission revoked in place](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha9/shot-permission-revoked.png)

## Module Governance

- `api.Modules` exposes a thread-safe, read-only snapshot of every installed module: status, scope,
  trust chain, requested/granted/denied permissions, hook count, hits and errors.
- `api.Modules.Changed` fires after enable/disable, install/uninstall and permission edits.
- Manifest permissions now mean **requested** permissions. New modules receive all requests by
  default; older state files without a permission array keep the same compatibility behavior.
- Permission edits persist independently of `enabled` in `modules.json`.
- Revoking `hooks` immediately makes existing chain entries pass through to the original method.
  Regranting restores them without reinstalling or reloading the module.
- The module detail page updates its `granted/total` badge and checkbox in place instead of
  rebuilding the page and losing the scroll position.

## Signature-Aware Hooks

The old name-based `IHooks.Attach` stays available. It now warns when an overload is ambiguous.
New modules can discover and select an exact target:

```csharp
var targets = api.Hooks.FindTargets("PCL.ModNet", "NetDownloadByClient");
var target = targets.Single(t => t.ParameterTypes.Count == 7);

using (api.Hooks.Attach(target, "demo.download", handler, HookMode.Prefix))
{
}
```

`SwapMethodTarget` accepts full/short/C#-alias parameter type names plus optional return-type and
static filters. Unknown or ambiguous signatures throw with the full candidate list. `IsHooked`
means at least one active handler remains; a dormant empty gate does not count.

## SDK API 1.6

`Host.ApiVersion` is now **1.6.0**. Additive surfaces:

- `ISwapApi.Modules` / `SwapModuleInfo` / `IModuleCatalog`
- `IHooks.FindTargets` / `Attach(SwapMethodTarget, …)` / `IsHooked(SwapMethodTarget)`
- `IUi.Badge` / `IUi.Expander` / `IUi.Tabs`

`Expander` constructs its content lazily; `Tabs` uses native WPF controls. All API 1.0-1.5 modules
remain source-compatible.

## Verification

| Gate | Result |
|---|---|
| Full payload / patch / loader build | PASS |
| Harness: `addontest` / `pcltest` / `pagetest` / `scantest` / `loaderhooktest` / `surfacetest` / `uitest` / `safemodetest` | PASS |
| Permission grant/revoke/regrant | PASS; hook behavior and scoped API change immediately |
| Permission persistence | PASS across a fresh manager state load |
| Signature overload resolution | PASS; exact string overload hooked while int overload stayed untouched |
| Module catalog snapshots/events | PASS |
| API 1.6 front-end controls on STA Dispatcher | PASS |
| Real PCL 2.99.0.1 English UI | `ok=66 fail=0`; checkbox `3/3 -> 2/3`, `modules.json` updated, scroll stayed in place |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha9.zip` | WinUI 3 injector, self-contained, includes the shim seed |
| `PCL2.dl.swap-v26.2-Alpha9.zip` | ExLoader-facing shim `.dll` + `.dat` + checksums |
| `PCL2.dl.swap-Loader-v26.2-Alpha9.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.2-Alpha9.dll` | Patch engine release-name artifact |
| `source-v26.2-Alpha9.zip` | Source snapshot at the signed tag |
| `shot-permission-governance.png` | English permission UI evidence |
| `shot-permission-revoked.png` | In-place revoke evidence (`3/3 -> 2/3`) |

| Artifact SHA-256 | Value |
|---|---|
| Injector zip | `426f3ae4b22899ddd1128a44905c40e9c87c45656cf177e9ffea8b9a4d47c16b` |
| ExLoader zip | `93bf1c5aa42a0d8e115ec47d041707159a5f200d11e22309c92e7304a0a063b0` |
| Loader zip | `0fd0bfa3d7de23527c948798e27b68982741e6bbc77b18e5eb9074f2d630ee06` |

## Boundaries

- Permissions are application-level governance inside one .NET process, not a security sandbox.
  `Reflect` and `Surface` remain explicit host escape hatches.
- The interactive permission list covers manifest requests; a module cannot gain a permission it
  never requested.
- PCL 2.12.8.1 remains an extraction baseline only.

<!-- Git Hub @BlockConnectExp | Block Connect Lab@StarsailsClover -->
