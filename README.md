<p style="font-size:28px;text-align:center;"><b>PCL2.dl.swap</b></p>

> Restore Plain Craft Launcher's download capability on official builds that disable it, turn silent
> OTA into an explicit cancellable prompt, and verify every launcher-cloud download in a sandbox
> before it is allowed to land.

> [!IMPORTANT]
> The generated `PclDlSwap.Patched.exe` is a patched **copy**; the official executable is never
> modified. Deleting the generated files restores vanilla PCL immediately.

---

## 1 Introduction

Official PCL 2.99.0 still ships the whole download module but replaces its loader with a stub that
throws `为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能`. Because upstream may delete the module
entirely in a later build, this repository keeps a validated baseline (2.14.0) and a working patch
for today's release (2.99.0).

The artifact is a single DLL plus a thin loader:

```
pcl2swap-{PCL2Ver.}-{Ver.}.dll      the patch engine
PclDlSwap.Loader.exe                patch a copy, launch it, optionally supervise
PclDlSwap.Payload.dll               scanner / sandbox / landing watchdog
data/offset-manifest.json           per-build tokens and RVAs
```

## 2 Quick Start

```powershell
# Lay out the dist directory next to PCL (or anywhere, passing the target explicitly)
.\PclDlSwap.Loader.exe "D:\PCL\Plain Craft Launcher 2.exe"

# -repatch   force regenerating the patched copy
# -watch     supervise the process and scan staged update payloads before they can be applied
```

Expected output:

```
PCL2 DLL-Swap loader (v26.0-Alpha 1)
  目标    : D:\PCL\Plain Craft Launcher 2.exe
  原版哈希: 6b6e1260cb5a1d6a…
  PageDownloadInstall.LoaderInit -> 恢复 ModDownload.DlClientListLoader
  ModSecret.UpdateStart -> 静默 OTA 改为显式确认（可取消）
  ModSecret.HasUpdatePermission -> false（禁止自动更新）
  补丁输出：D:\PCL\PclDlSwap.Patched.exe（3 处重写）
  已启动  : pid=…
```

## 3 What Changes

| Area | Behaviour before | Behaviour after |
|---|---|---|
| Download page | loader stub throws, page unusable | real `ModDownload.DlClientListLoader` restored |
| Change game version | broken (same page, same loader) | works again |
| Launcher OTA | silent background replacement | explicit prompt; cancel keeps the current build |
| Auto-update permission | allowed | hard-denied |
| Launcher-cloud downloads | land straight to disk | staged, multi-engine scanned, quarantined on doubt |

## 4 Requirements

- Windows 10/11 and .NET Framework 4.8 (PCL itself requires it).
- Building only: .NET SDK 8+, plus Visual Studio build tools with the C++ workload for the profiler DLL.
- Scanning: Microsoft Defender is used automatically; ClamAV, VirusTotal and MetaDefender Cloud
  activate through `config/swap.config.json`.

## 5 Building

```powershell
.\scripts\build.ps1                 # builds payload, patcher, loader, tools and assembles dist\
.\scripts\build.ps1 -SkipProfiler   # skip the native profiler DLL
```

## 6 Verifying

```powershell
dotnet run -c Release --project tools\VerifyOffsets -- `
  data\offset-manifest.json `
  "C:\path\to\Plain Craft Launcher 2.exe"
# RESULT: PASS means sha256 + MVID + tokens + RVAs all match the manifest
```

The end-to-end check re-reads the patched file and asserts the three rewrites; see
`docs/REA-analysis.md` section 12 for the expected IL.

## 7 Documentation

| Document | Content |
|---|---|
| `docs/REA-analysis.md` | full static analysis, gate location, OTA chain, measured patch routes |
| `docs/architecture.md` | how loader / patcher / payload cooperate and why |
| `docs/versioning.md` | version scheme, artifact naming, release checklist |
| `docs/CHANGELOG.md` | per-release change log |

## 8 Safety

- The payload only runs inside the launched process; the official binary is never written to.
- Launcher-cloud downloads are staged and scanned; a failed engine blocks by default
  (`AllowDegradedCommit` is the only opt-out).
- This work exists to restore functionality and harden the update path; respect upstream licensing
  and local law.

---

License: MIT

GitHub@BlockConnectExp

GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
