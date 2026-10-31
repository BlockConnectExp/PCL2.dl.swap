# PCL2.dl.swap v26.2

> v26.2 is the LTS release of the v26.2 line. It turns Swap from a feature bundle into a
> module platform: English host translation, module governance, safe recovery, signature-aware
> hooks, bounded diagnostics, module messaging and files, and a broader native WPF front end.

![English PCL2 host with Swap pages](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2/shot-english-v262.png)

![Swap Addon management and API 1.8 surfaces](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2/shot-sdk18.png)

![Safe mode recovery in English](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2/shot-safe-mode-v262.png)

## Platform

- **English without modifying PCL2 files.** The runtime layer now carries 507 exact translations
  and 60 dynamic-phrase mappings. A per-control source ledger makes language switches reversible,
  including rows whose values change at runtime.
- **Language packs.** Extend or override the dictionary through
  `%APPDATA%\PCL2Swap\i18n\en-US.json` or any `i18n\en-US\*.json` file.
- **Module governance.** `api.Modules` exposes a live read-only catalog with requested, granted
  and denied permissions. Revoking hooks, files or messaging takes effect immediately.
- **Startup recovery.** Two unclean starts enter safe mode. The recovery page names the suspect
  module and lifecycle phase, and can disable it before the next normal launch.
- **Signature-aware hooks.** `FindTargets` and `SwapMethodTarget` select exact overloads. Replace
  handlers can call `SwapHookContext.Current.InvokeOriginal()` without recursion.
- **Bounded diagnostics.** `api.Diagnostics` mirrors the runtime log into a 512-entry timeline
  with export, change events and the native `IUi.Timeline` front end.

## SDK API 1.8

`Host.ApiVersion` is **1.8.0**. Every change is additive; API 1.0-1.7 modules remain
source-compatible.

| Surface | Kind | Added |
|---|---|---|
| `I18n.RegisterMany` / `Format` / `TryTranslate` | back end | Batch dictionaries and formatted English |
| `IAddonFiles` | back end | Path-confined module file storage |
| `IAddonMessaging` | back end | In-process topic publish/subscribe |
| `IUi.Table` | front end | Weighted read-only tables |
| `IUi.EmptyState` | front end | Centered zero-content state |
| `IUi.CodeBlock` | front end | Read-only monospace diagnostics |
| `IUi.Scroll` | front end | Native page-level vertical scrolling |

API 1.7 additions are included in the same release: `SwapHookContext`,
`IDiagnosticsAccess` and `IUi.Timeline`.

## Verification

| Gate | Result |
|---|---|
| Full build: payload / patcher / loader / shim / injector | PASS |
| Harness: `addontest` / `pcltest` / `pagetest` / `scantest` / `loaderhooktest` / `surfacetest` / `uitest` / `safemodetest` | PASS |
| API 1.8 file traversal rejection and permission gate | PASS |
| API 1.8 messaging source stamp, disposal and permission gate | PASS |
| English ledger translate/restore | PASS |
| Real PCL 2.99.0.1, round 1: normal injection and self-test | PASS, `ok=73 fail=0` |
| Real PCL 2.99.0.1, round 2: addon install, permissions, files and messaging | PASS; permission badge `3/3 -> 2/3`, state persisted |
| Real PCL 2.99.0.1, round 3: API 1.8 pages and scrolling | PASS; `PCL.MyScrollViewer`, English feedback routing |
| Real PCL 2.99.0.1, round 4: forced safe mode, clear and restart | PASS; all modules skipped, then restored `enabled`; `ok=73 fail=0` |

## Compatibility

Supported host line remains official PCL **2.99.0.0** and **2.99.0.1**. PCL 2.12.8.1 remains an
extraction baseline only. ExLoader injection remains manual by design; use the bundled WinUI 3
injector for local/direct injection.

## Boundaries

- Modules run in the same CLR process. Permissions govern platform entry points, not arbitrary
  `System.IO`, reflection or native code.
- Messaging payloads are in-process object references and are not serialized.
- English remains progressive coverage. Unmapped PCL text falls back to Chinese instead of showing
  a broken or empty label.
- Structural host-page repairs may require a PCL restart to roll back even after a module is
  disabled; hook and event behavior disables immediately.

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2.zip` | WinUI 3 injector with bundled shim |
| `PCL2.dl.swap-v26.2.zip` | ExLoader-facing shim `.dll` + `.dat` + checksums |
| `PCL2.dl.swap-Loader-v26.2.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.2.dll` | Patch-engine release artifact |
| `Pcl2Swap.Shim.x64.dll` | Raw ExLoader injection DLL |
| `Pcl2Swap.Shim.x64.dat` | Raw ExLoader payload container |
| `source-v26.2.zip` | Source snapshot at the signed tag |
| `shot-english-v262.png` | English host/page evidence |
| `shot-sdk18.png` | API 1.8 management/UI evidence |
| `shot-safe-mode-v262.png` | Safe mode recovery evidence |

| Artifact SHA-256 | Value |
|---|---|
| Injector zip | `a1cc2dc1ca33a54171ba115091b06d81f37b02179bad9171f921f15a76838c7e` |
| ExLoader zip | `25f9cb1d2eb536492747ddc86ad0830c55160aa178959756c37d1fc272dbb5bb` |
| Loader zip | `82be477474d36e073d78227bec9fb5f13e2ba82ed07eb38d296661c811f76715` |
| Patch engine | `2fa0cac6b63fb7e4f48a112d6c5571bcdd9352fec9437ab85f72d14e595fef58` |
| Raw shim DLL | `daf2df569d51c800b37f21ddd953cac5aaaef1f6d27c81c7bfecd260304e5900` |
| Raw shim DAT | `13fc5ea3dcfb95e3d0b69ba91a108967acaeb4d76bf6c39182ca5619c54e6247` |

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
