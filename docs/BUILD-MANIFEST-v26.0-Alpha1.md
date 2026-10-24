# Build Manifest — v26.0-Alpha 1

> Summary of this document: exactly what was built, from which commit, and with which hashes, so a
> release can be reproduced or audited later.

> [!NOTE]
> Hashes below are SHA-256 of the files inside the Pre-Release archive.

---

## 1 Source

| Item | Value |
|---|---|
| Commit | `9c66ae767427d8e01f6b8f5b72e12d87f0af60f9` |
| Branch | `main` (= `v26.0`) |
| Tool version | v26.0-Alpha 1 |
| Target PCL | 2.99.0.0 Release (VersionCode 413) |
| Baseline PCL | 2.14.0.0 Release (VersionCode 411) |

## 2 Toolchain

| Component | Version |
|---|---|
| .NET SDK | 9.0.314 / 10.0.402 |
| Target framework | .NET Framework 4.8 (x86) |
| MSVC | 19.51.36260 (VS 2026 Build Tools) |
| Windows SDK | 10.0.26100.0 |
| dnlib | 4.4.0 |
| Gee.External.Capstone | 2.3.0 |

## 3 Artifacts

| File | Role |
|---|---|
| `pcl2swap-2.99.0-v26.0.Alpha1.dll` | patch engine (release-named copy of `PclDlSwap.Patcher.dll`) |
| `PclDlSwap.Loader.exe` | patch a copy of the target and launch it |
| `PclDlSwap.Payload.dll` | sandbox scanner, multi-engine gate, landing watchdog |
| `dnlib.dll`, `Gee.External.Capstone.dll`, `capstone.dll` | runtime dependencies of the loader |
| `PclDlSwap.Profiler.dll` | optional native profiler (experimental, not required) |
| `data/offset-manifest.json` | six methods × four official builds |
| `RELEASE-NOTES-v26.0-Alpha1.md` | release notes |
| `source-v26.0-Alpha1.zip` | source archive for the same commit |

## 4 Provenance of Validation

| Check | Result |
|---|---|
| `tools/VerifyOffsets` against four official samples | PASS |
| Patched 2.99.0 launches | PASS (reports `正式版 2.99.0.0 (413)`) |
| Re-read IL: `LoaderInit` pushes real loader | PASS |
| Re-read IL: `UpdateStart` prompts before silent OTA | PASS |
| Re-read IL: `HasUpdatePermission` returns false | PASS |
| Microsoft Defender real scan through the sandbox | PASS (clean file released) |

## 5 Not Covered

1. Live PCL-cloud download path was not exercised end to end (upstream endpoints currently answer 403).
2. The in-process CLR profiler does not initialise on .NET Framework 4.8 x86.
3. No third-party engine (ClamAV / VirusTotal / MetaDefender Cloud) was configured during validation.

## 6 Follow-up Artifacts

This release is kept open for offset-only adaptations of new official builds (repository policy:
attach an extra bundle instead of opening a new release).

| Artifact | Target build | Date | Notes |
|---|---|---|---|
| `PCL2.dl.swap-v26.0-Alpha1.zip` | PCL 2.99.0.0 (413) | 2026-10-25 | initial bundle |
| `source-v26.0-Alpha1.zip` | same commit | 2026-10-25 | source archive |
| `pcl2swap-2.99.0.1-v26.0-Alpha1.zip` | PCL 2.99.0.1 (415) | 2026-10-25 | offset-only adaptation: loader bundle with the updated `data/offset-manifest.json` |

The 2.99.0.1 bundle reuses the same loader, payload and patch engine: no code changed, only the
offset manifest gained the new build's coordinates. Evidence is in `docs/REA-analysis.md` §14.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
