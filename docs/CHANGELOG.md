<p style="font-size:28px;text-align:center;"><b>PCL2.dl.swap ChangeLog</b></p>

> Records every released change set, newest first.

> [!NOTE]
> The artifact name carries both versions: `pcl2swap-{PCL2Ver.}-{Ver.}.dll`.

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

