# PCL2.dl.swap v26.2-Alpha 7

> Alpha 7 adds the missing LSPosed-style recovery layer: SwapHost now remembers whether the
> previous process reached a stable startup window, and isolates all addons when it detects a
> repeated early-crash loop.

![Safe mode recovery card in Swap Addon](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha7/shot-safe-mode.png)

## Safe Mode

- Every SwapHost start writes `<data root>\recovery\startup-journal.json`.
- The journal starts as `stable=false`; a background heartbeat marks it stable after the host
  survives the startup window (12 seconds by default).
- Two consecutive starts that never reach the stable window trigger safe mode on the next start.
- Safe mode skips `OnLoad/OnEnable` for every addon, including built-ins, but keeps the Swap pages
  and recovery controls available so the user can repair the installation.
- `PCL2SWAP_SAFE_MODE=1` forces safe mode for diagnostics; `PCL2SWAP_RECOVERY_STABLE_MS` can shorten
  the stable window for automated tests.

The recovery card shows the trigger reason, preserved unclean-start count, recovery directory, and
provides copy/leave/open actions. Leaving safe mode clears the flag for the next launch; it does not
hot-load modules into the current process.

## SDK API 1.4

`Host.ApiVersion` is now **1.4.0**. Additive host facts:

```csharp
api.Host.SafeMode;            // true in a recovery-isolated process
api.Host.SafeModeReason;      // human-readable trigger
api.Host.RecoveryDirectory;   // <data root>\recovery
```

All API 1.0-1.3 addons remain source-compatible.

## Verification

| Gate | Result |
|---|---|
| Full payload / patch / loader build | PASS |
| Harness: `addontest` / `pcltest` / `pagetest` / `scantest` / `loaderhooktest` / `surfacetest` / `uitest` | PASS |
| Harness: `safemodetest` | PASS; three attempts enter safe mode, all modules skipped, clear flag restores next launch |
| Real 2.99.0.1 normal injection | `ok=66 fail=0`; journal reached `stable=true` |
| Real 2.99.0.1 prepared safe-mode injection | `ok=66 fail=0`; demo addon and built-ins all `skipped-safe-mode` |
| Recovery UI screenshot | Safe-mode card and module statuses rendered |

The real test used an isolated copy of PCL 2.99.0.1 and isolated `TEMP`/`PCL2SWAP_DATA`; the
owner's daily 2.12 installation was not touched.

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha7.zip` | WinUI 3 injector, self-contained, includes the shim seed |
| `PCL2.dl.swap-v26.2-Alpha7.zip` | ExLoader-facing shim `.dll` + `.dat` + checksums |
| `PCL2.dl.swap-Loader-v26.2-Alpha7.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.2-Alpha7.dll` | Patch engine release-name artifact |
| `source-v26.2-Alpha7.zip` | Source snapshot at the signed tag |
| `shot-safe-mode.png` | Recovery UI evidence |

| Artifact SHA-256 | Value |
|---|---|
| Injector zip | `65bbd9539fd627f6309586e13974a607cf004d7ea80a813c9d5584f7e1f83ff8` |
| ExLoader zip | `702d6ed46ed40845a62066d52846a67a517d7e269f4522d66c6dbfc63aa9a94c` |
| Loader zip | `86f8b0ee12a8182389872a0860e20f18b87e8eebd982ed8807fe60b62e0bf22f` |

## Boundaries

- Safe mode protects addons managed by SwapHost; it is not a replacement for Windows process or
  device-driver recovery.
- A module that crashes PCL before SwapHost can write its journal is outside this recovery layer.
- The current process remains in safe mode even after the flag is cleared; restart is required.
- PCL 2.12.8.1 remains an extraction baseline only.

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
