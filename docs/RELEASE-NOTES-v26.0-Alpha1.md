# PCL2.dl.swap v26.0-Alpha 1

> First public Pre-Release: download-module restoration, silent-OTA gate and sandboxed update
> verification for official PCL 2.99.0.

> [!WARNING]
> Pre-Release software. The download module was reverse engineered from official binaries and the
> patch touches launcher internals; keep a copy of the original executable and expect upstream
> layout changes to require new offsets.

---

## Highlights

1. **Downloads work again on 2.99.0.** Upstream replaced the download page loader with a stub that
   throws `为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能`. The patch restores the real
   `ModDownload.DlClientListLoader`, which also fixes the "change game version" flow in the instance
   manager (both share the same page and loader).
2. **Silent OTA is gone.** `ModSecret.UpdateStart` now asks before replacing the launcher; cancelling
   keeps the current build. `ModSecret.HasUpdatePermission` is hard-denied as a second gate.
3. **Launcher-cloud downloads are verified before they land.** Staged into a sandbox, stamped with
   Mark-of-the-Web, scanned by every participating engine (Microsoft Defender by default; ClamAV,
   VirusTotal and MetaDefender Cloud by configuration). Doubtful payloads are quarantined and never
   replace the launcher.

## Artifacts

| File | Purpose |
|---|---|
| `pcl2swap-2.99.0-v26.0-Alpha1.dll` | the patch engine (this release's main artifact) |
| `PclDlSwap.Loader.exe` | patches a copy of the target and launches it |
| `PclDlSwap.Payload.dll` | sandbox scanner, watchdog, logging |
| `data/offset-manifest.json` | per-build tokens and RVAs (6 methods × 4 builds) |
| `PclDlSwap.Profiler.dll` | optional native CLR profiler (experimental, see limitations) |

### Follow-up bundle: `pcl2swap-2.99.0.1-v26.0-Alpha1.zip`

Same loader, payload and patch engine, with the offset manifest extended to build 415. Upstream
2.99.0.1 is a positional rebuild: the three patched methods are byte-identical to 2.99.0.0, every
tracked token is unchanged and the RVAs moved 42-244 bytes, so per this repository's policy the
bundle is attached here instead of opening a new release. A single loader bundle covers both
2.99.0.0 and 2.99.0.1 because the manifest ships all retained builds.

## Build Information

| Item | Value |
|---|---|
| Tool version | `v26.0-Alpha 1` |
| Validated target | PCL 2.99.0.0 Release, VersionCode 413 |
| Also validated | PCL 2.99.0.1 Release, VersionCode 415 (`pcl2swap-2.99.0.1-v26.0-Alpha1.zip`) |
| Validated baseline | PCL 2.14.0.0 Release, VersionCode 411 |
| Target SHA-256 | `6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386` |
| Target MVID | `926d447e-daff-4169-8c64-adc3d37d5f78` |
| Offset verification | `tools/VerifyOffsets` PASS on all four samples |
| Runtime | .NET Framework 4.8, x86 |

## Verification Performed

- Patched binary starts and reports `正式版 2.99.0.0 (413)` from the patched path.
- Re-reading the patched IL confirms all three rewrites (`LoaderInit`, `UpdateStart`,
  `HasUpdatePermission`).
- Microsoft Defender executed a real scan through the sandbox pipeline; uninstalled engines do not
  block, failed engines do.

## Known Limitations

1. The in-process CLR profiler route is **not functional** on .NET Framework 4.8 x86: the profiler
   DLL loads and its COM factory works, but `Initialize` is never invoked. The profiler ships as an
   experimental component only; disk patching is the supported path.
2. `-watch` supervision is opt-in; scanning happens on the desktop side rather than inside the
   launcher process.
3. Later official builds will need fresh tokens/RVAs in `data/offset-manifest.json`; the verification
   tool reports the missing build instead of patching blindly.
4. If upstream deletes `ModDownload.DlClientListLoader` entirely, a self-contained module (planned,
   see `docs/REA-analysis.md` section 11) becomes mandatory.

## Installation

```powershell
# 1. Extract the release archive next to PCL (or anywhere).
# 2. Patch and launch:
.\PclDlSwap.Loader.exe "D:\PCL\Plain Craft Launcher 2.exe" -watch
# 3. To return to vanilla PCL, delete PclDlSwap.Patched.exe and launch the original.
```

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->

