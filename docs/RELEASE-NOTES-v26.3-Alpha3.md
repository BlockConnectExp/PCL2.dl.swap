# PCL2.dl.swap v26.3-Alpha 3

> Third Alpha of v26.3: the addon platform gains packaged assets, module-scoped localization,
> host appearance/window back ends and four native front-end controls. English coverage is
> broader, and its runtime translator no longer performs an unbounded translation storm.

![API 1.11 assets, localization and front-end controls](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha3/shot-api111-frontends-en.png)

![English Swap Settings with translation diagnostics](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha3/shot-swap-settings-performance-en.png)

## English layer

- The built-in exact dictionary now has **610 unique keys**. The phrase layer has **60 entries**;
  ten overlap the exact dictionary, so the combined declared set is **660 distinct keys**.
- `scripts/i18n-audit.ps1` now separates exact and phrase dictionaries. Against the 34 page
  classes in official PCL 2.99.0.1 it resolves **388 of 1,692 distinct page `ldstr` strings**:
  **22.93% exact page-string coverage**. Logs, exceptions, crash analysis and easter eggs remain
  in that denominator, so this is not a claim of 22.93% visible-UI completion.
- The previous `89.2%` claim is not reproducible and has been withdrawn.
- Translation passes are coalesced after 500 ms of layout quiet, with a 1.5 s maximum delay and
  a 4 s fallback scan. The observed live run took 37.5 ms for the first 13 strings and 1.7 ms for
  the next pass, replacing the former minute-scale re-translation storm.
- English remains progressive. Unmapped launcher text falls back to Chinese rather than displaying
  a placeholder.

## SDK API 1.11

`Host.ApiVersion` is now **1.11.0**. All API 1.0-1.10 addons remain source-compatible.

- `ISwapApi.Assets` provides a read-only, path-confined view of `assets/`, including normalized
  paths, lengths, SHA-256 values, text/byte/stream reads and directory listing.
- `ISwapApi.Localization` reads `i18n/en-US.json` and `i18n/zh-CN.json` from the module package.
  Registrations and loaded packs are module-owned, hot-reloadable and disappear with the module;
  they never mutate the process-wide host dictionary.
- `PclAddonPack --assets <dir> --i18n <dir>` packages both trees and includes every file in the
  signed manifest inventory.
- `Pcl.Appearance` exposes validated 2.99.x theme fields and `ThemeRefreshMain` without resetting
  `ThemeNow`.
- `Pcl.Window` exposes state, topmost, title, activate, minimize, maximize and restore on the real
  PCL WPF window; every mutation is dispatched to the launcher UI thread.
- Strict permission mode gains `theme` and `window`. Revoking either permission blocks the
  corresponding accessor immediately.
- `IModuleCatalog` now reports packaged asset count and available locales.

## Front end

Four controls extend the native WPF component set:

- `SearchBox`: labelled search field, persistent placeholder, inline clear action.
- `SegmentedControl`: compact mutually-exclusive mode switch for two to five options.
- `ColorInput`: validated `#RRGGBB` field with a live colour swatch.
- `SelectList`: short visible selection list for choices that should not be hidden in a dropdown.

The public Demo now consumes the complete API 1.11 surface and persists its search, density, accent
colour and page preset through `AddonSettings`.

## Correctness and security

- Addon unpacking now requires a real directory-separator boundary. A sibling path such as
  `../1.0.0-evil/` can no longer pass a plain `StartsWith("<version>")` check.
- Packaged assets use the same boundary rule for every read and listing operation.
- Built-in modules with an empty package directory no longer reach `Path.GetFullPath("")`; all
  three built-ins start normally.
- Module localization reload detects content changes even when the locale filename set is unchanged.

## Verification

| Gate | Result |
|---|---|
| Full Release build + offset verification | PASS |
| Harness: copy, ABI2 managed, hooks, UI, scan, loader hook, addon, surface, PCL, page, safe mode | **11/11 PASS** |
| Managed ABI stress | PASS, 2,000 calls |
| API 1.11 front ends | PASS |
| Addon assets, module localization and sibling-prefix zip-slip regression | PASS |
| Offset manifest: 2.99.0.0 / 2.99.0.1 | PASS |
| Real official PCL 2.99.0.0 injection | PASS, `ok=84 fail=0` |
| Real official PCL 2.99.0.1 injection | PASS, `ok=84 fail=0` |
| Demo hot install on both hosts | PASS, `enabled`, `assets=1`, `locales=en-US,zh-CN` |
| Real PCL `PageChange` / `PageChangeRun` route | PASS on both hosts |

The real-host gate uses isolated copies and isolated `TEMP` / `PCL2SWAP_DATA`. ExLoader remains a
manual listing/upload path; this release does not automate it.

## Core hashes

| Artifact | SHA-256 |
|---|---|
| `PclDlSwap.Payload.dll` | `d2d0fa9dcc193fb0c2827d866154768fb5135670e862d13217e32412c69c0770` |
| `PCL2Swap.Api.dll` | `275b446bd09c9226e8f1978d1097fe7241b952cb58f71d89bd194b2b7645fb37` |
| `Pcl2Swap.Shim.x64.dll` | `536dd66b8a5c23a6a2ef0cfcfc818e49419acdbd5d5f030d281715e9db94a9db` |
| `Pcl2Swap.Shim.x64.dat` | `acfd8a096f71f9a5b24648614a9ae55cbe5514d6efc3f32ef6f06a0a7949b2d3` |
| Injector ZIP | `ad4eee422f04209fc4b58174684f102c4a7fa0ff55518eaaa1f13ce598539f04` |
| Patch engine DLL | `b660db698189b2ba4b4439b554039eb207122c9d2a5bbb2b2a7f33a6cc37c0e3` |

## Artifacts

| Asset | Purpose |
|---|---|
| `PCL2.dl.swap-v26.3-Alpha3.zip` | ExLoader-facing shim DLL + DAT bundle |
| `PCL2.dl.swap-Injector-v26.3-Alpha3.zip` | WinUI injector with seeded shim |
| `PCL2.dl.swap-Loader-v26.3-Alpha3.zip` | Disk-patch shape and dependencies |
| `Pcl2Swap.Shim.x64.dll` | Raw native shim |
| `Pcl2Swap.Shim.x64.dat` | ExLoader decimal-byte form |
| `pcl2swap-2.99.0-v26.3-Alpha3.dll` | Patch-engine artifact |
| `source-v26.3-Alpha3.zip` | Signed-tag source snapshot |
| `SHA256SUMS.txt` | Digests for all uploaded files |
| `shot-api111-frontends-en.png` | API 1.11 front-end evidence |
| `shot-swap-settings-performance-en.png` | English performance diagnostics evidence |

Supported host line: official PCL **2.99.0.0** and **2.99.0.1**. PCL CE and the owner's daily 2.12
installation are outside this release verification.
