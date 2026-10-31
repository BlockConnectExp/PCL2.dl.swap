# PCL2.dl.swap v26.2-Alpha 6

> Alpha 6 focuses on **English coverage** and the next additive SDK layer. The module itself does
> not make network connections; PCL2 keeps doing its own networking.

![English Swap Settings with API 1.3 host information](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha6/shot-sdk13-settings-en.png)

![English download page after runtime translation](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha6/shot-download-en.png)

## English coverage

- The built-in dictionary now contains **354 exact entries plus runtime phrase rules**.
- Added coverage for common status text, version/download cards, settings and Java rows,
  login/skin screens, multiplayer/EasyTier controls, and more of the launcher's dynamic rows.
- Top-level WPF dialogs, menu/tree headers and DataGrid column headers are now included in the
  translation pass, not only the main window.
- `II18n` gains `Count`, `Has(string)` and `LanguageChanged`; addons can observe a real language
  switch and extend their own labels without rebuilding the payload.
- Remaining names, brands and unsupported prose can stay Chinese by design; this is progressive
  coverage, not a binary all-or-nothing switch.

## SDK API 1.3

The host API is now **1.3.0**. All changes are additive; addons built for 1.0, 1.1 or 1.2 remain
compatible.

### More backends

| Backend | Surface |
|---|---|
| `Pcl.Navigation` | Current page/root/subpage, `Go(root, sub)`, `Back()` through PCL's own page stack |
| `Pcl.Threading` | UI-thread check, cross-thread invoke, disposable one-shot/repeating schedules |
| `Pcl.Environment` | Host path/directory, durable data root, PCL version, debug/admin state |
| `Pcl.Profiles` | Current login name/status, opaque `McLoginData`, selected skin object |
| `Pcl.Net.Request/Get` | Host `NetRequestByClientRetry` bridge with retry, headers, encoding and browser headers |

### More frontends

`IUi` now also provides `RadioGroup`, `NumberInput`, `PasswordInput`, `MultilineTextInput`, `Image`
and a live `Progress` handle. `Hint` has typed `Info / Success / Warning / Error` variants mapped to
PCL's native notification colors.

## Verification

| Gate | Result |
|---|---|
| Payload + patch + loader build | PASS |
| Harness `--addontest` / `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` | PASS |
| Harness `--uitest` (real WPF Dispatcher, API 1.3 builders) | PASS |
| Real 2.99.0.1 injection + self-test | `ok=63 fail=0` |
| English real-host UI | Settings/About/Download/More screenshots captured after live language switch |

The real-host run used an isolated copy of PCL 2.99.0.1 and an isolated `TEMP`/`PCL2SWAP_DATA`
directory. The owner's 2.12 daily launcher was not modified or executed.

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha6.zip` | WinUI 3 injector, self-contained, includes the shim seed |
| `PCL2.dl.swap-v26.2-Alpha6.zip` | ExLoader-facing shim `.dll` + `.dat` + checksums |
| `PCL2.dl.swap-Loader-v26.2-Alpha6.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.2-Alpha6.dll` | Patch engine release-name artifact |
| `shot-sdk13-settings-en.png` | English Swap Settings / API 1.3 evidence |
| `shot-download-en.png` | English download-page evidence |
| `shot-about-en.png` | English About / Swap branding evidence |
| `shot-more-en.png` | English More/Help evidence |

| Artifact SHA-256 | Value |
|---|---|
| Injector zip | `b8f8adf6b840a13968c0510b51265225415bf96b102670c4fa64e2c6d1b425cb` |
| ExLoader zip | `c30e5baf047799f291d0cd9dd68ae89b1b8db2b500ca179afcb4fc26eaa95320` |
| Loader zip | `df5627f1ed8333acf94af130fb8941c14012f8f01454f2925893779215cf4439` |

## Boundaries

- PCL 2.99.0.0 and 2.99.0.1 remain the supported injection line.
- PCL 2.12.8.1 remains an extraction baseline only; no runtime support is claimed.
- The shim is unsigned; Windows Defender may classify a fresh build as an `!ml` heuristic. Use the
  release hash and the documented local ExLoader flow rather than weakening host protection.
- The ExLoader GUI cannot be automated by this project; use the released injector for local
  verification or upload the matching shim through ExLoader manually.

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
