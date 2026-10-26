<p style="font-size:28px;text-align:center;"><b>PCL2.dl.swap ChangeLog</b></p>

> Records every released change set, newest first.

> [!NOTE]
> The artifact name carries both versions: `pcl2swap-{PCL2Ver.}-{Ver.}.dll`.

---
## 2026.10.26-22:40:00 Alpha 2 (release cut): download-path performance and sidebar crash fixes

> 版本号规则（Owner，2026-10-26）：Alpha 号随**发布**递增。Alpha 1 是唯一已发布版本，因此本次
> 对外构建收敛为 **v26.0-Alpha 2**；下方 Alpha 3-5 的条目属于未发布的内部迭代，全部并入本次
> Alpha 2 的变更集。

GitCommitHash: 1f1514a (release notes and hash recorded in the follow-up docs commit)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +57 -45
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +55 -8
.\src\PclDlSwap.Payload\Sandbox\DownloadSandbox.cs       +35 -0
.\src\PclDlSwap.Payload\Sandbox\ScannerSet.cs            +59 -8
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +77 -12
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +17 -8
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +105 -4
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|CoreInstaller/DownloadSandbox|Launcher-cloud downloads now run through PCL's own multi-source downloader; the scanner runs afterwards on the produced file and deletes it when a scanner objects. The previous flow re-downloaded every launcher-cloud file through a single-threaded WebClient inside the hook and spawned one MpCmdRun process per file - both halves of the reported performance loss|2026.10.26-22:40:00|PCL2.dl.swap
2|CoreInstaller|Scan policy (per Owner): only executable/archive extensions are scanned, and anything landing inside the Minecraft directory (game files, mods, resource packs, shaders) skips the scanner entirely, while launcher files fetched from PCL's servers keep it. JSON/PNG and friends pass through untouched|2026.10.26-22:40:00|PCL2.dl.swap
3|DownloadPageRestore|The 2.99.0.1 sidebar repair no longer writes the PageID field. It now drives PCL's own navigation (Checked -> PageChange -> PageGet) and only finishes after verifying ModMain.FrmDownloadInstall exists. The old field write skipped PageGet, left the page object null, and the next click died in PCL.PageDownloadLeft.ItemInstall_Click with a NullReferenceException (field logs 22:23:35 and 22:25:28)|2026.10.26-22:40:00|PCL2.dl.swap
4|ScannerSet/SwapLog/PclUi|MpCmdRun and clamscan lookups are cached, the log mirror disables itself after the first failure, the host logger is resolved once, and the dispatcher method table is cached (the pollers call OnUiThread every 300 ms until a repair lands)|2026.10.26-22:40:00|PCL2.dl.swap
5|Harness|New --scantest (extension + game-directory policy) and a click-safety regression that reproduces the field NullReferenceException path; integration suite is now 10/10|2026.10.26-22:40:00|PCL2.dl.swap

### Loader (disk-patch) shape: Alpha 2 rebuild (2026-10-27)

- The disk-patch shape is a pure static IL rewrite (download-page gate restore, 2.99.0.1 sidebar repair, silent-OTA confirmation prefix, update-permission gate). Its runtime profiler hook was never wired up, so the bundle is a version-alignment rebuild rather than a behaviour change.
- `HookEntryPoints.DownloadHook` (the loader-side hook used once profiling is wired) now rebuilds PCL's own `NetFile`/`LoaderDownload` pipeline through reflection - multi-source failover, progress sync into the loader and FileChecker validation - instead of the old single-threaded WebClient that also skipped the checker. The harness regression `--loaderhooktest` pins the call chain against a stub that mirrors PCL 2.99.0's IL.
- `ScanPolicy` (shared by both shapes) now owns the "game content is never scanned" rule; `CoreInstaller` keeps a thin wrapper so the existing `--scantest` still passes.
- `src\PclDlSwap.Profiler\build.cmd`: the trailing watermark line became `rem` (the bare `#` line made cmd exit non-zero after a successful build, so build.ps1 always reported "profiler failed").
- Bundle: `PCL2.dl.swap-Loader-v26.0-Alpha2.zip` (loader + patcher + payload + dnlib + MonoMod.Utils/Mono.Cecil + Capstone + `data/offset-manifest.json`).

version: v26.0-Alpha 2

---
## 2026.10.26-21:45:00 Alpha 5: UI-thread marshalling, manifest merge and double-injection guards

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +92 -27
.\src\PclDlSwap.Payload\Host\OffsetManifest.cs           +111 -45
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +58 -0
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +2 -2
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs          +28 -0
.\src\PclDlSwap.Payload\Features\AboutBranding.cs        +44 -18
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +32 -8
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +158 -12
.\src\PclDlSwap.ExLoaderHarness\PclDlSwap.ExLoaderHarness.csproj   +6 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PclUi|OnUiThread now marshals through WPF's real overloads (`Invoke(Delegate, object[])` / `Invoke(Action)`). The previous code looked for a single-parameter `Invoke(Delegate)` that WPF does not have, silently ran the body on the polling thread and produced the field-log failure "调用线程无法访问此对象" for the About row (2.99.0 and 2.99.0.1) and the 2.99.0.1 sidebar repair|2026.10.26-21:45:00|PCL2.dl.swap
2|PclUi|The WPF Application type is resolved by walking the loaded assemblies. A named `Type.GetType("System.Windows.Application, PresentationFramework")` does not bind from the payload's LoadFrom context - measured by the new uitest, which now runs a real Dispatcher on an STA thread|2026.10.26-21:45:00|PCL2.dl.swap
3|OffsetManifest|The disk and embedded manifests are parsed and merged by target id (disk wins on conflict), and JSON arrays are read as `IEnumerable`. The previous disk-first single source made every retained build log "宿主不在偏移清单中" even though the MVIDs matched; both 2.99.0 and 2.99.0.1 now resolve to their manifest entries|2026.10.26-21:45:00|PCL2.dl.swap
4|AboutBranding/DownloadPageRestore|A failed insert/repair is retried up to 40 poll rounds (first failure warns, every tenth logs) instead of silencing the feature for the whole session; the 2.99.0.1 sidebar collapse now recovers after the page appears|2026.10.26-21:45:00|PCL2.dl.swap
5|CoreInstaller/DetourInstaller|`HostAlreadyPatched` / `IsHooked` probe the JIT entry (FF25 on x64, E9 on x86) before installing; a second injection into the same process (ExLoader firing twice, or a newer shim over a running PCL) is refused instead of stacking a second detour over an already-patched body|2026.10.26-21:45:00|PCL2.dl.swap
6|Harness|New `--uitest` (real WPF dispatcher; cross-thread and inline assertions) and `--manifest` (per-host MVID match against real PCL binaries; run once per host because .NET reuses same-identity assemblies inside one process). Integration suite stays 9/9|2026.10.26-21:45:00|PCL2.dl.swap
7|Shim|`TOOL_VERSION` -> v26.0-Alpha5: the extraction directory moves to `%TEMP%\PCL2Swap\v26.0-Alpha5` so a payload locked by an already-running PCL cannot block the new build (the Alpha 4 directory held a 129024-byte payload locked by PID 42888 while the new payload is 132608 bytes)|2026.10.26-21:45:00|PCL2.dl.swap

version: v26.0-Alpha 5

---
## 2026.10.26-08:35:00 Alpha 4: managed hook targets and IL-copied originals (AccessViolation fix)

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Hook\X64Detour.cs            rewritten (v2)
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs      rewritten (v2)
.\src\PclDlSwap.Payload\Host\OriginalFactory.cs      new (MonoMod IL copies)
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs        hooks -> managed methods
.\src\PclDlSwap.Payload\Features\AnnouncementGuard.cs    hook -> managed method
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  hook -> managed method
.\src\PclDlSwap.Payload\Features\FeedbackRouter.cs       hook -> managed method
.\src\PclDlSwap.Payload\Host\ExLoaderEntry.cs        generic dependency resolver
.\src\Pcl2Swap.Shim\Shim.cpp                         embeds MonoMod.Utils + Mono.Cecil
.\src\Pcl2Swap.Shim\build.ps1                        new resources, /Gs9999999
.\src\PclDlSwap.ExLoaderHarness\Program.cs           v2 tests (copytest/hooktest)
.\docs\av-root-cause.md                              new (root cause writeup)
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|X64Detour/DetourInstaller|The detour now jumps to a managed method body (PrepareMethod + GetFunctionPointer) instead of a Marshal.GetFunctionPointerForDelegate thunk. The thunk assumes it is entered from unmanaged code, and JIT callers trip the GC mode - field crashes proved this (PCL 2.99.0 crash at TryFeedback IL+13 `e.Handled=true`; 2.99.0.1 crash inside the MyImage worker)|2026.10.26-08:35:00|PCL2.dl.swap
2|OriginalFactory|"Original" now runs from a full IL copy of the target method generated with MonoMod.Utils' DynamicMethodDefinition and invoked through reflection - a genuine managed call, no relocated prologue. The stolen-prologue trampoline, Capstone disassembly and the relocation engine are gone|2026.10.26-08:35:00|PCL2.dl.swap
3|X64Detour|v2 only backs up the entry and writes the 14-byte absolute jump, with a short-method guard; the old RIP-relative relocation failures (and the crash while rejecting `mov eax,[rip+...]` prologues) cannot happen any more|2026.10.26-08:35:00|PCL2.dl.swap
4|Hooks (OTA/download/dialog/loader/feedback)|All hooks became static managed methods with the target's ABI (reference parameters as object, value parameters exact); their originals are reached through DetourInstaller.InvokeOriginal|2026.10.26-08:35:00|PCL2.dl.swap
5|Harness|New --copytest (IL copy equivalence) and --hooktest (managed hook + 2000 calls under GC pressure + original through the copy). The old trampoline-based engine/abi tests were retired. Integration suite: 9/9|2026.10.26-08:35:00|PCL2.dl.swap

version: v26.0-Alpha 4

---
## 2026.10.26-05:00:00 Alpha 3: property-aware member access, poll-based page repairs

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +60 -25
.\src\PclDlSwap.Payload\Features\AboutBranding.cs        +170 -150
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +130 -75
.\src\PclDlSwap.Payload\Host\OffsetManifest.cs           +48 -0
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +23 -4
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +2 -2
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +170 -70
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PclUi|Member reads now fall back to properties. PCL exposes PanLoad/PanAllBack/LoadMinecraft/PanMain/ItemAboutPcl as VB.NET properties, and the previous field-only lookup returned null - the direct cause of the PageLoaderInit NullReferenceException and the silently skipped About row|2026.10.26-05:00:00|PCL2.dl.swap
2|DownloadPageRestore|LoaderInit now feeds live page controls to PageLoaderInit (the NRE path is gone), and the 2.99.0.1 sidebar repair became a poller over ModMain.FrmDownloadLeft instead of a constructor detour whose prologue cannot be relocated|2026.10.26-05:00:00|PCL2.dl.swap
3|DownloadPageRestore|Install-time gate probe: LoaderInit is only replaced when its IL carries the "关闭下载 / 不再提供" stub markers, so stock builds (2.12.8.1 and friends) stay on their own download path|2026.10.26-05:00:00|PCL2.dl.swap
4|AboutBranding|Rebuilt as a poller over ModMain.FrmOtherAbout with UI-thread insertion; the constructor detour is gone and the row is inserted a few hundred milliseconds after the page first appears|2026.10.26-05:00:00|PCL2.dl.swap
5|OffsetManifest|ResolveFor(host) matches the assembly that actually carries PCL instead of EntryAssembly (the injector can wrap the launcher), and the payload now logs the host path/version/MVID on every activation|2026.10.26-05:00:00|PCL2.dl.swap
6|SwapLog/Shim|Version bumped to v26.0-Alpha 3, extraction directory v26.0-Alpha3|2026.10.26-05:00:00|PCL2.dl.swap

version: v26.0-Alpha 3

---
## 2026.10.25-14:10:00 Restore the vanilla download entry hidden by 2.99.0.1

GitCommitHash: 438ecbe (base)

ChangedFiles:
```
.\src\PclDlSwap.Patcher\IlPatcher.cs             +135 -0
.\data\offset-manifest.json                      +53 -29
.\tools\AddTarget\Program.cs                     +1 -0
.\tools\VerifyOffsets\Program.cs                 +15 -6
.\docs\REA-analysis.md                           +24 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|src/PclDlSwap.Patcher/IlPatcher.cs|New rewrite: remove the 2.99.0.1-only PageDownloadLeft constructor statements that collapse the "原版游戏" entry, switch the default sub-page to DownloadMod and check the Mod entry|2026.10.25-14:10:00|PCL2.dl.swap
2|data/offset-manifest.json|Every retained build now tracks PCL.PageDownloadLeft..ctor (token + RVA), derived from the samples by tools/AddTarget|2026.10.25-14:10:00|PCL2.dl.swap
3|tools/VerifyOffsets|Constructor manifest keys (Type..ctor) split correctly, and the RVA comparison now reads the real `rva` field instead of the never-present `bodyFileOffset`|2026.10.25-14:10:00|PCL2.dl.swap
4|docs/REA-analysis.md|Recorded the 2.99.0.1 sidebar gate, the verified four rewrites and the runtime evidence|2026.10.25-14:10:00|PCL2.dl.swap

version: v26.0-Alpha 2 (in development)

---

## 2026.10.25-01:50:00 Support PCL 2.99.0.1 (offset-only adaptation)

GitCommitHash: 60d6819 (base)

ChangedFiles:
```
.\data\offset-manifest.json                     +60 -75
.\data\methods-v2990-1-rel415.tsv           +6343 -0
.\data\methods-v2128-snap401.tsv                -0 -6341
.\docs\REA-analysis.md                           +40 -12
.\tools\AddTarget\**                              +130 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|data/offset-manifest.json|Added the 2.99.0.1 (VersionCode 415) target: sha256/MVID plus six method tokens and RVAs|2026.10.25-01:50:00|PCL2.dl.swap
2|data/offset-manifest.json|Retired the oldest entry (2.12.8.1 build 401) to honour the five-version retention policy|2026.10.25-01:50:00|PCL2.dl.swap
3|tools/AddTarget|New helper that derives sha256/MVID/tokens/RVAs from a sample so a new official build is a one-command adaption|2026.10.25-01:50:00|PCL2.dl.swap
4|docs/REA-analysis.md|Recorded the 2.99.0.1 assessment: three tracked methods byte-identical, tokens unchanged, RVAs shifted 42-244 bytes|2026.10.25-01:50:00|PCL2.dl.swap

version: v26.0-Alpha 1 (asset-only follow-up)

---

## 2026.10.25-00:30:00 Repository rebuild and first Pre-Release

GitCommitHash: 9c66ae7

ChangedFiles:
```
.\README.md                                  +120 -0
.\docs\REA-analysis.md                       +260 -0
.\docs\REA-analysis.zh.md                    +261 -0
.\docs\architecture.md                        +60 -0
.\docs\versioning.md                          +60 -0
.\data\offset-manifest.json                   +140 -0
.\src\PclDlSwap.Patcher\IlPatcher.cs         +330 -0
.\src\PclDlSwap.Payload\**                   +900 -0
.\src\PclDlSwap.Loader\Program.cs            +150 -0
.\scripts\build.ps1                          +100 -0
.\tools\VerifyOffsets\Program.cs             +120 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|src/PclDlSwap.Patcher|Disk IL patch engine: restore the download loader, gate silent OTA behind an explicit prompt, deny auto-update permission|2026.10.25-00:30:00|PCL2.dl.swap
2|src/PclDlSwap.Payload|Sandbox staging, multi-engine扫描 gate, landing watchdog, Mark-of-the-Web, logging|2026.10.25-00:30:00|PCL2.dl.swap
3|src/PclDlSwap.Loader|Patch a copy of the target, launch it, optional -watch supervision|2026.10.25-00:30:00|PCL2.dl.swap
4|data/offset-manifest.json|Six methods across four official builds with sha256/MVID/token/RVA, verified PASS|2026.10.25-00:30:00|PCL2.dl.swap
5|docs/REA-analysis.*|Full static analysis, the 2.99.0 gate location, OTA chain, measured patch routes|2026.10.25-00:30:00|PCL2.dl.swap
6|scripts/build.ps1|One-command build and dist assembly with hash listing|2026.10.25-00:30:00|PCL2.dl.swap

version: v26.0-Alpha 1

---

## 2026.10.24-23:00:00 Initial implementation

GitCommitHash: (pre-rebuild history, not retained)

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|(research)|Reverse engineered four official builds; located the single 2.99.0 download gate|2026.10.24-21:00:00|PCL2.dl.swap
2|(research)|Measured and rejected four runtime patch routes; adopted disk IL patching|2026.10.24-23:00:00|PCL2.dl.swap

version: v26.0-Alpha 1 (development)

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->

