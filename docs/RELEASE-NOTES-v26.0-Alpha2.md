# PCL2.dl.swap v26.0-Alpha 2

> Second public Pre-Release: the ExLoader hot-injection feature set, the WPF marshalling and
> sidebar-crash fixes, and the launcher-cloud scan policy rework that removed the performance loss.

> [!IMPORTANT]
> Version rule (2026-10-26): the Alpha number only advances on release. Alpha 1 is the only
> published build, so this release is **Alpha 2**; the Alpha 3-5 changelog entries describe
> internal iterations that are all part of this change set.

---

## Highlights

1. **The injected feature set now works on 2.99.0 and 2.99.0.1.** The payload adds the
   "PCL2 Swap By BlockConnect" row to 更多 → 关于与鸣谢 → 关于, routes 反馈 through a PCL-vs-Swap
   choice, restores the "原版游戏" download entry and keeps the cancellable "悲报" update prompt.
   The About row was missing because the WPF dispatcher was looked up through a `Invoke(Delegate)`
   overload that does not exist, and because `System.Windows.Application` could not bind from the
   injected assembly's LoadFrom context.
2. **The 2.99.0.1 sidebar crash is gone.** The repair used to write the `PageID` field directly,
   skipped `PageGet` and left `ModMain.FrmDownloadInstall` null - the next click then crashed in
   `PCL.PageDownloadLeft.ItemInstall_Click` with a NullReferenceException. It now drives PCL's own
   `Checked -> PageChange -> PageGet` navigation and only reports success once the sub-page object
   exists.
3. **Downloads are back to stock speed.** Launcher-cloud downloads run through PCL's own
   multi-source downloader again; the scan runs afterwards and only for executable/archive files
   outside the Minecraft directory. Game content (versions, mods, resource packs, shaders) and
   metadata (JSON, icons, translations) are never scanned - per the owner policy of 2026-10-26 -
   and a blocked verdict deletes the file before the download reports success. The old flow
   re-downloaded every launcher-cloud file through a single-threaded WebClient and spawned one
   `MpCmdRun` process per file.
4. **Manifest resolution fixed.** The disk and embedded offset manifests are merged by target id,
   so 2.99.0 (MVID `926d447e`) and 2.99.0.1 (MVID `5613aa62`) both resolve to their manifest
   entries instead of logging "宿主不在偏移清单中".

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha2.zip` | both files above plus `SHA256SUMS.txt` |
| `PCL2.dl.swap-Loader-v26.0-Alpha2.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies) rebuilt for this version |

SHA-256:

```
393944d64e67665efb525f10c62f6a6a25b9886ca10317982809a2ecd31bc175  Pcl2Swap.Shim.x64.dll
313d209520849e598a65635791482b646a28633e688a8d0f47754bb53ee2c2fb  Pcl2Swap.Shim.x64.dat
```

## Disk-Patch Shape (Loader)

The loader bundle patches a **copy** of PCL and launches it; the original executable is never
modified. This shape is a pure **static IL rewrite** and provides: the download-page gate restore,
the 2.99.0.1 sidebar repair, silent-OTA confirmation (a cancellable prompt before any background
update) and the update-permission gate. Its runtime profiler hook has never been wired up, so this
bundle is a version-alignment rebuild; the payload's `DownloadHook` (used once profiling is
connected) now rebuilds PCL's own `NetFile`/`LoaderDownload` pipeline instead of replacing it with
a single-threaded WebClient.

```powershell
# Lay the zip next to PCL (or anywhere), then:
.\PclDlSwap.Loader.exe "D:\PCL\Plain Craft Launcher 2.exe"
# -repatch  force a fresh patched copy, -watch  supervise the launched process
```

## Field Verification (owner, 2.99.0 and 2.99.0.1)

- **2.99.0**: download page reaches the real loader, the sidebar entry is already visible, the
  About row is inserted, feedback routing asks first, the update announcement stays cancellable;
  no crash in PCL's log.
- **2.99.0.1**: the sidebar entry is restored ("项可见、子页为 Default、FrmDownloadInstall 已就绪"),
  the download page reaches the real loader, the About row is inserted, and the click path that
  previously raised the NullReferenceException completes; PCL exits with `Success`.
- Browsing and downloading are back to vanilla speed (no per-file scanner process, no second
  download).

## Installation (ExLoader)

1. Upload `Pcl2Swap.Shim.x64.dll` on the ExLoader injector card for Plain Craft Launcher (the
   `.dat` is the same bytes in ExLoader's userFiles encoding, for deployments that need it).
2. Start PCL through ExLoader. Logs land in `%TEMP%\PCL2Swap\v26.0-Alpha2\pcl2swap.log` and the
   native shim log in `%TEMP%\pcl2swap-native.log`.
3. If a security suite flags the freshly built files, allow/restore them once (Defender's ML
   heuristics have false-positived on the `.dat` before).

The disk-patch shape (PclDlSwap.Loader.exe + patched copy) remains available from the Alpha 1
release and is unchanged by this build.

## Known Limitations

1. The in-process page repairs wait up to 300 ms after a page first appears.
2. 2.99.0.1's `LabResource` group label stays collapsed, matching the disk-patch behaviour.
3. ClamAV / VirusTotal / MetaDefender stay opt-in; Microsoft Defender is the default engine.
4. Injected into an already-patched process (ExLoader firing twice) the payload refuses to install
   a second time by design; restart PCL to pick up a new build.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
