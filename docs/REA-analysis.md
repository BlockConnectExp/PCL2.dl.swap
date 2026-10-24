# PCL2 download-module restoration, OTA gate and sandbox injection

> Summary of this document: full static analysis of the official PCL builds this project targets,
> the exact gate that disables downloads, the silent-OTA chain, and the patch routes that were
> measured and either adopted or rejected.

> [!IMPORTANT]
> Everything here comes from decompiled IL of official binaries. Each claim can be re-checked with
> the hashes in section 1 and the tooling listed in section 13.

---

## 1 Sample Ledger (verified)

| Ref | Version | Build type | VersionCode | File | SHA-256 | MVID |
|---|---|---|---|---|---|---|
| S1 | 2.12.8.1 | Snapshot | 401 | `Plain Craft Launcher 2.exe` | `d4f3e8f789cfd449ae9902ccadbadf5f35f3ac1b88793db37db5744cfd158fac` | `8558c9c3-b0f8-4e5c-8944-969432ccfb74` |
| S2 | 2.14.0.0 | **Release (extraction baseline)** | 411 | `Plain Craft Launcher.exe` | `5582952ded3b443503f66c23b810802bd20f1a3e847b54d9e09653a825859a36` | `e6e9028d-8366-47b6-a8b0-1ce46a3e5278` |
| S3 | 2.14.0.0 | Snapshot | 412 | `快照版 2.14.0.0.exe` | `77fbc26a13abf725dbd26038cba023224c2ce5518f3ee9c4608acb2274d753f0` | `2a08e6c9-87e4-41c0-b237-17bd168c67fe` |
| S4 | 2.99.0.0 | **Release (injection target)** | 413 | `Plain Craft Launcher 2.exe` | `6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386` | `926d447e-daff-4169-8c64-adc3d37d5f78` |

All four are PE32 / x86 / .NET Framework 4.8 managed assemblies (72-byte CLI header, `il-only`),
so both module extraction and patching happen at the managed layer.

## 2 Assembly and Resource Shape

Each official executable is the complete PCL assembly (not packed, type names intact). Manifest
resources for 2.99.0: `Plain Craft Launcher 2.g.resources` and `PCL.Resources.resources`.

`*.g.resources` holds the compiled WPF BAML, including the download pages — for example
`/Plain Craft Launcher 2;component/pages/pagedownload/resource/pagedownloadmod.xaml`. Extracting
"the download module including its pages" therefore has to move the **assembly plus its resources**;
recompiling decompiled C# would lose every page.

## 3 Download Module Boundary

The module is byte-for-byte identical in 2.14.0 and 2.99.0 for the engine classes:

| Type | Role |
|---|---|
| `PCL.ModDownload` | version/loader catalogue parsing (Forge, Fabric, NeoForge, OptiFine, …) |
| `PCL.ModDownloadLib` | download orchestration and Minecraft install manifest landing |
| `PCL.ModNet` | HTTP layer, multi-source retry, rate limiting, `NetFile` / `LoaderDownload` |
| `PCL.ModLoader` | `LoaderTask` / `LoaderCombo` progress and cancellation |
| `PCL.PageDownloadLeft` | download section navigation |
| `PCL.PageDownloadInstall` | install/download page **and** the "change game version" select mode |
| `PCL.PageDownloadMod` / `Pack` / `DataPack` / `ResourcePack` / `Shader` | resource subpages |
| `PCL.PageDownloadResourceDetail`, `PCL.PageResource` | resource search and detail |

Page registration (`PageDownloadLeft.PageGet` → `FormMain.PageSubType`):

```
Default=0 -> PageDownloadInstall        DownloadMod=2 -> PageDownloadMod
DownloadPack=3 -> PageDownloadPack      DownloadDataPack=4 -> PageDownloadDataPack
DownloadResourcePack=5                  DownloadShader=6
```

## 4 Silent OTA Chain

```
ModSecret.ServerSub(LoaderTask)                      <- notice.cfg / ServerConfig.json
   └─ ServerConfig["Update"][Release|Snapshot]
        ├─ PatchUrls      (incremental patch, MsDelta)
        └─ DownloadUrls   (full package)
   └─ ModSecret.UpdateStart(Silent)
        ├─ Silent = true  -> IsUpdateWaitingRestart = true (background replacement)
        └─ Silent = false -> ModMain.Hint("PCL 正在下载更新…")
   └─ download to %TEMP%\PCL\<version>.<code>.<ext>
   └─ zip extract / MsDelta.Apply
   └─ move to <launcher>\PCL\Plain Craft Launcher 2.exe
        └─ ModSecret.UpdateRestart(TriggerRestartAndByEnd)
             └─ ModSecret.UpdateReplace(ProcessId, OldFileName, NewFileName, TriggerRestart)
```

Key evidence in the 2.14.0 decompilation (`PCL.ModSecret.cs`): `UpdateStart` at 833, the
`IsSilentUpdate` assignment at 851, the silent-only UI suppression at 852/943, `IsUpdateWaitingRestart`
at 206/233, `UpdateRestart`/`UpdateReplace` at 1321/1354, and the `Latest.zip` download at 1465.

`ModSecret.UpdateStart` is the single choke point for OTA, so gating it covers every automatic path.

`ModSecret.HasUpdatePermission` is a potato-code gate in 2.12.8.1 and a constant `true` in
2.14.0/2.99.0; forcing it to `false` is the second, independent gate.

## 5 Official Cloud Endpoints (launcher data, not game assets)

```
https://pcl2-server-1253424809.file.myqcloud.com/notice.cfg{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/minecraft/download.json{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/notice.json{CDN}
```

`{CDN}` is expanded by `ModSecret.SecretCdnSign` (DES-decrypted constant plus a SHA-1 over
`path-timestamp-rand-0-secret`). Live requests currently answer **403**, which is why older builds
behave as if updating was removed.

## 6 Offset Model

`data/offset-manifest.json` is the only file that must change per version:

| Field | Meaning | Use |
|---|---|---|
| `id` / `version` / `versionCode` / `buildType` | build identity | human reading, logs |
| `sha256` | whole-file hash | exact binding |
| `mvid` | module MVID | primary match key |
| `tokens.*` | method metadata tokens | stable identification across rebuilds |
| `rva.*` | method body RVAs | raw offset work |

Verified for six methods across all four samples (`tools/VerifyOffsets` → PASS).
Notably 2.14.0 and 2.99.0 share `ModSecret` tokens and differ only in RVAs — which is exactly why
"adapt only the offsets" holds for later builds.

## 7 The 2.99.0 Download Gate (single point)

2.99.0 does **not** delete the download module. It replaces one argument:

```csharp
// 2.14.0 (works)
PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null,
               ModDownload.DlClientListLoader, a0 => LoadMinecraft_OnFinish());

// 2.99.0 (gated): a throwing stub takes the loader slot
PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null,
               new ModLoader.LoaderTask<string, ModDownload.DlClientListResult>("关闭下载", a0 =>
               {
                   throw new Exception("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……");
               }),
               a0 => LoadMinecraft_OnFinish());
```

`FormMain` (lines 261 / 1259 / 2602) explicitly recognises that message and treats it as an expected
state, which is why the user-visible behaviour is "downloads are unavailable" rather than a crash.
Two sibling lambdas (`_Lambda$__R3-3`, `_Lambda$__3-0`) throw the same message.

Verified tokens for `PageDownloadInstall.LoaderInit`:
401 = `0x0600050C`, 411 = `0x06000513` / RVA `0x2E010`, 412 = `0x06000513` / `0x2E6D4`,
413 = `0x06000513` / `0x2DEB4`.

## 8 Game-Instance Coupling (impact assessed)

The instance manager reuses the download page in select mode:

```
FormMain.cs:2347   PageType.Download && PageSubType.Default && FrmDownloadInstall.IsInSelectPage
PageDownloadInstall.cs:1590 PrepareSelectPage()  -> IsInSelectPage = true
PageDownloadInstall.cs:1616 EnterSelectPage()
PageDownloadInstall.cs:1705 MinecraftSelected()  -> EnterSelectPage()   ("change game version")
```

Consequences:

1. Restoring `LoaderInit()` restores **both** the download page and "change game version"; no
   separate patch is needed.
2. Both paths share `ModDownload.DlClientListLoader`, so one scan gate covers both.
3. If a future build deletes `ModDownload`/`DlClientListLoader`, both paths break together — the
   reason to ship a self-contained module (section 11).

## 9 Sandbox / Scan / Landing Pipeline

```
PCL cloud download request
  ├─ URL on a PCL domain (pcl2-server / meloong.com / pclc.cc / pcl2.net)?
  │    no  -> untouched (game asset downloads are unaffected)
  │    yes ↓
  ├─ stage into %TEMP%\PclDlSwap\sandbox\<guid>.<ext>   (never executed, never the real target)
  ├─ stamp Mark-of-the-Web (Zone.Identifier)
  ├─ multi-engine scan
  │    Microsoft Defender  (MpCmdRun -Scan -ScanType 3 -DisableRemediation)
  │    ClamAV              (clamscan, when installed)
  │    VirusTotal          (hash report, API key)
  │    MetaDefender Cloud  (upload, detected_by)
  ├─ any participating engine flags or fails -> do not land, quarantine, prompt with the report
  └─ all clean -> publish to the destination (MotW preserved)
```

Policy notes: engines that are simply not installed do not block; an engine that was expected to
answer and failed does block. `AllowDegradedCommit` is the only opt-out.

## 10 Runtime Patch Findings (measured, then rejected)

| Route | Measurement | Outcome |
|---|---|---|
| Managed detour on JIT-compiled code | target first byte rewritten to `E9`, hook invoked, but the trampoline's return path breaks under the x86 fast-call convention (`0x80000003`) | rejected |
| `MethodRental.SwapMethodBody` across assemblies | works inside one assembly; across assemblies even a trivial `ldc.i4.0; ret` body crashes | rejected |
| `calli` + function-pointer slot | same CLR refusal (cross-assembly body replacement is not permitted) | rejected |
| Native CLR profiler | profiler DLL loads, COM factory and `QueryInterface` succeed, but `Initialize` is never called on .NET Framework 4.8 x86 | deferred |
| **Disk IL patch (dnlib)** | patched 2.99.0 starts normally; all three rewrites confirmed by re-reading the IL | **adopted** |

## 11 Self-Contained Module (for the day the official module disappears)

Because the module still exists today, restoring the gate is enough. Once upstream deletes it:

1. extract the download module's IL, BAML and resources from 2.14.0 into `pcl2swap-*.dll` itself;
2. redirect the download entry point to that internal implementation;
3. share the same offset manifest so later builds only need new tokens/RVAs.

## 12 Implemented Patches (verified IL)

```
PageDownloadInstall.LoaderInit
   9: ldnull
  10: ldsfld  PCL.ModDownload::DlClientListLoader     <- real loader (was a throwing stub)
  16: call    PCL.MyPageRight::PageLoaderInit<…>

ModSecret.get_HasUpdatePermission
   0: ldc.i4.0 / 1: ret                               <- always false

ModSecret.UpdateStart
   0: ldarg.0 / 1: brfalse <original>                 <- non-silent path untouched
   2..13: ModMain.MyMsgBox("…silent update blocked…", "Continue", "Cancel")
  15: bne.un <ret>                                    <- cancel returns
  18+: original method body
```

All three rewrites reference only types the host already declares, so the patched module gains no
new `AssemblyRef` — the requirement for both CLR loading and future re-patching.

## 13 Reproducing

```powershell
# decompile every type (tolerant batch decompiler)
dotnet run -c Release -- <sample.exe> <outDir>            # tools/DecompAll
# method/token/RVA tables
dotnet run -c Release -- <sample.exe> <out.tsv>           # tools/MethodTable
# validate the offset manifest against real samples
dotnet run -c Release -- data\offset-manifest.json <sample.exe>...
# inspect one method's IL
dotnet run -c Release -- <sample.exe> tmp --il <Type> <Method>
```

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
