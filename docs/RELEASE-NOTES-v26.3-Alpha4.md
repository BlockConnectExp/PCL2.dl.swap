# PCL2.dl.swap v26.3-Alpha 4

> Fourth Alpha of v26.3: a focused bug-fix release that restores native modpack installation on
> official PCL 2.99.x. The file picker and every supported pack format now reach PCL's own
> installation code again instead of stopping at the regulatory gate.

![Native modpack picker on PCL 2.99.0.0](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha4/shot-modpack-picker-29900.png)

![Native modpack picker on PCL 2.99.0.1](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha4/shot-modpack-picker-29901.png)

## Modpack installation restored

PCL 2.99.0.0 and 2.99.0.1 contain two independent `PCL.ModModpack.ModpackInstall` gates:

- `ModpackInstall()` had been reduced to a regulatory hint and no longer opened the file picker.
- `ModpackInstall(string, string, string)` still detected the archive format, but threw before the
  native `InstallPack*` dispatch switch.

This release restores both overloads behind the existing `Download MC` module:

- The native `选择整合包压缩文件` picker opens with the PCL main window as its owner.
- CurseForge, Modrinth, HMCL, MMC, MCBBS, launcher-pack and plain-archive inputs are dispatched to
  PCL's unchanged `InstallPack*` implementations.
- Archive base folders plus `InstanceName` and `Logo` are preserved.
- PCL continues to own loader selection, taskbar registration, cancellation, cleanup and every
  pack-specific installation step. Swap does not ship a replacement installer.
- Disabling `Download MC` forwards both overloads to the original gated methods.
- Hosts without the known gate, or hosts missing any required native installer, remain untouched.

The complete issue-to-merge evidence is in
[issue #1](https://github.com/BlockConnectExp/PCL2.dl.swap/issues/1),
[PR #2](https://github.com/BlockConnectExp/PCL2.dl.swap/pull/2), and
[`docs/evidence/issue-1/`](https://github.com/BlockConnectExp/PCL2.dl.swap/tree/v26.3/docs/evidence/issue-1).

## Compatibility

- Addon API remains **1.11.0**; no SDK or package-format changes.
- Supported and re-tested hosts: official PCL **2.99.0.0 (413)** and **2.99.0.1 (415)**.
- PCL CE and the owner's daily PCL 2.12 installation are outside this release verification.
- ExLoader remains a manual listing/upload path; this release does not automate it.

## Verification

| Gate | Result |
|---|---|
| Full Release build | PASS |
| Offset manifest: PCL 2.99.0.0 | PASS |
| Offset manifest: PCL 2.99.0.1 | PASS |
| Harness: copy, ABI2, hooks, UI, scan, loader hook, addon, surface, PCL, modpack, page, safe mode | **12/12 PASS** |
| Managed ABI stress | PASS, 2,000 calls |
| Modpack format/dispatch regression | PASS, 14/14 assertions |
| Real official PCL 2.99.0.0 injection | PASS, `ok=84 fail=0` |
| Real official PCL 2.99.0.1 injection | PASS, `ok=84 fail=0` |
| CurseForge fixture on PCL 2.99.0.0 | PASS, native `LoaderCombo<string>` |
| CurseForge fixture on PCL 2.99.0.1 | PASS, native `LoaderCombo<string>` |

The real-host gate uses isolated copies and isolated `TEMP` / `PCL2SWAP_DATA`. No external modpack
server was required for the dispatch test.

## Core hashes

| Artifact | SHA-256 |
|---|---|
| `PclDlSwap.Payload.dll` | `aa9730156207cd849f6a3c11de7f300eec72d07b5da732323ba39c5c42a27778` |
| `PCL2Swap.Api.dll` | `e6ff6d0dd269c2f849031b81d8ace694bf9207c726d58e1dfeb26faaa00be103` |
| `Pcl2Swap.Shim.x64.dll` | `6f96232f00f462110b655c3dddb968bd06984e6d8f09980de2c8b7cfab2e64b2` |
| `Pcl2Swap.Shim.x64.dat` | `6395376c94e1244cde3478dfbbe6909ab7584d892b5fd224a474dc6ca5a288dd` |
| Injector ZIP | `744f4b499f0f7f335ca14b0e4c09171a7e4043d046769d449c619c80a3cffcc4` |
| Patch engine DLL | `655c6406663bf67c017629728013f5114470b7b6a0b799aa5e0fbb8d331847cc` |

## Artifacts

| Asset | Purpose |
|---|---|
| `PCL2.dl.swap-v26.3-Alpha4.zip` | ExLoader-facing shim DLL + DAT bundle |
| `PCL2.dl.swap-Injector-v26.3-Alpha4.zip` | WinUI injector with seeded shim |
| `PCL2.dl.swap-Loader-v26.3-Alpha4.zip` | Disk-patch shape and dependencies |
| `Pcl2Swap.Shim.x64.dll` | Raw native shim |
| `Pcl2Swap.Shim.x64.dat` | ExLoader decimal-byte form |
| `pcl2swap-2.99.0-v26.3-Alpha4.dll` | Patch-engine artifact |
| `source-v26.3-Alpha4.zip` | Signed-tag source snapshot |
| `SHA256SUMS.txt` | Digests for all other uploaded release assets |
| `shot-modpack-picker-29900.png` | Native picker evidence on PCL 2.99.0.0 |
| `shot-modpack-picker-29901.png` | Native picker evidence on PCL 2.99.0.1 |
