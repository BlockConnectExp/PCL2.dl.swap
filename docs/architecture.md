# Architecture / 架构说明

> Summary of this document: how the loader, patcher and payload cooperate, and which constraints
> shaped that design.

> [!NOTE]
> Every design decision below is backed by a measurement recorded in `docs/REA-analysis.md`;
> the rejected routes are listed there so they are not attempted twice.

---

## 1 Components

```
dist/
  PclDlSwap.Loader.exe      entry point: patch a copy of the target, then start it
  PclDlSwap.Patcher.dll     the patch engine (= pcl2swap-<pcl2>-<ver>.dll)
  PclDlSwap.Payload.dll     sandbox scanner, multi-engine AV gate, landing watchdog, logging
  data/offset-manifest.json per-build method tokens and RVAs
```

## 2 Flow

```
1. Loader reads the target exe, hashes it, and looks the hash/MVID up in offset-manifest.json.
2. Loader asks PclDlSwap.Patcher to produce PclDlSwap.Patched.exe next to the original.
3. Loader starts the patched copy (optionally with -watch to supervise it).
4. Inside the patched copy:
     PageDownloadInstall.LoaderInit   -> pushes the real ModDownload.DlClientListLoader
     ModSecret.UpdateStart            -> prompts explicitly for silent OTA, returns on cancel
     ModSecret.HasUpdatePermission    -> always false
5. With -watch, the watchdog locks each staged update payload in %TEMP%\PCL, scans it with
   every participating engine, and quarantines anything that is not clean.
```

## 3 Constraints That Shaped It

| Constraint | Consequence |
|---|---|
| Official PCL is not strong-name signed | IL can be rewritten without invalidating a signature |
| dnlib cannot append an AssemblyRef to a loaded module | patches may only reference types the host already uses |
| Managed detours are unstable under the x86 fast-call convention | no JIT-time trampoline tricks |
| `MethodRental` refuses cross-assembly body swaps | no in-memory method replacement |
| PCL already supports running from a nested launcher directory | a patched sibling executable is a supported shape |

## 4 Trust Boundaries

- The original executable is never modified; deleting the generated files restores vanilla PCL.
- Cloud downloads are staged, scanned and only then published; unverified results block by default.
- Scanning failure is treated as "not clean"; `AllowDegradedCommit` is the only opt-out.

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
