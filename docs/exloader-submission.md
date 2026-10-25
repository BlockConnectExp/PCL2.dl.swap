# ExLoader submission draft (Add Your mod to ExLoader)

Form: https://tally.so/r/RGoY0v

Captured from the live form on 2026-10-26: 34 questions, all character limits included below.
The form is English-only and reviewers are strict: answer as written, do not improvise.

## Blockers / open questions (owner decides)

1. **"No internet connections" is a stated criterion.** This mod requires internet by design
   (it restores PCL's own download feature and gates launcher updates). The draft declares this
   honestly in several fields; expect a review question. There is no way around it: the feature
   *is* networking.
2. **"Your mod must have at least some update history."** Alpha 1 shipped 2026-10-25, Alpha 2 is
   today. Both are public on GitHub; the release notes are the history.
3. **The form's injection model assumes a game process.** PCL2 Swap injects into the *launcher*
   (`Plain Craft Launcher 2.exe`), which then launches the game. Question 29 (launch timing) has
   no exact match; the draft picks the closest option and explains it in question 63.
4. **Screenshots must be imgur links.** Owner uploads after the first successful ExLoader test.
   Suggested set: download page restored (2.99.0.1), 悲报 with the 取消 button, About page row,
   feedback routing dialog.

## Question-by-question draft

| # | Question | Answer |
|---|---|---|
| 1 | Mod name (3-20) | `PCL2.dl.swap` |
| 2 | Game (3-28) | `Minecraft (PCL2 launcher)` |
| 3 | Hardware limitations | *(leave empty)* |
| 4 | Mod type (3-50) | `x64 native DLL injected into the launcher` |
| 5 | Main features (<=500) | `Restores the launcher's built-in download page on official builds that disable it; turns silent launcher self-updates into an explicit cancellable prompt; adds a Cancel button to the forced update notice; stages and virus-scans launcher-cloud downloads before they land; adds a Swap row to About and routes Feedback to the right project.` |
| 6 | Screenshots (20-400) | `IMGUR LINKS HERE` (owner) |
| 7 | Launch timing | select: `You should close the game first and then launch mod before game is opened again` (closest match; explained in #24) |
| 8 | Game/store version | `Official PCL2 releases 2.99.0 and 2.99.0.1 (Windows). Older supported builds: 2.14.x.` |
| 9 | Platform / OS | `Windows 10/11 x64. .NET Framework 4.8 (already required by PCL2 itself).` |
| 10 | Component requirements | `None extra. Only .NET Framework 4.8, which PCL2 ships with.` |
| 11 | Launch arguments | *(leave empty)* |
| 12 | How to inject (15-500) | `Manual-map inject the DLL into Plain Craft Launcher 2.exe (x64) at process start - tested with the ExLoader "Injector / Custom mods" tab. No export or entry configuration is required: DllMain starts a worker thread (Pcl2SwapStart / Pcl2SwapInit exports exist if your injector prefers one). The worker hosts PCL's own CLR and installs all hooks once the main assembly is loaded; give it 2-3 seconds.` |
| 13 | Author + public page (10-150) | `BlockConnect (StarsailsClover) - https://github.com/BlockConnectExp` |
| 14 | Support URL | `https://github.com/BlockConnectExp/PCL2.dl.swap/issues` |
| 15 | How to open the menu | `No menu. The mod has no overlay; its features show up directly in PCL2's own UI (download page, About page, feedback dialog, update prompts).` |
| 16 | Warnings / known issues | `Needs an official x64 PCL2 build on Windows. Internet is required by design (the download feature is the point). On builds outside the embedded manifest the hooks fall back to name-based resolution and may be partial.` |
| 17 | Configs (20-600) | `Optional: place config/swap.config.json next to the launcher to tune the sandbox (API keys, feature toggles, allow-degraded-commit). Without the file the recommended defaults are active. No scripts.` |
| 18 | Extra files | `None at install time. At runtime the injector writes its extracted payload to %TEMP%\PCL2Swap\<version>\ only.` |
| 19 | Offsets auto-update | `No auto-updater. A per-build token manifest is embedded at release time; unknown builds degrade to name-based method resolution.` |
| 20 | Delete on update | `Nothing to delete. Each version extracts into its own %TEMP%\PCL2Swap\<version>\ folder; the DLL can be replaced normally by the injector.` |
| 21 | Feedback link | `yes - https://github.com/BlockConnectExp/PCL2.dl.swap/issues` |
| 22 | Crash collection | `Collect %TEMP%\PCL2Swap\ - plain-text pcl2swap.log (and optional sandbox files). No personal data is written.` |
| 23 | Region limits | `None. UI additions are in Chinese, matching PCL2's user base.` |
| 24 | Anything else | `Important: this injects into the Minecraft launcher process (Plain Craft Launcher 2), not into the game. It is a functionality-restoration and update-hardening tool: it restores the game-download feature that newer official launcher builds disable, and makes silent launcher updates explicit. Public source on GitHub.` |
| 25 | Mod url | `https://github.com/BlockConnectExp/PCL2.dl.swap` |

## Pre-submit checklist

- [ ] ExLoader injects the DLL and PCL2 stays healthy (owner test)
- [ ] All four in-launcher features seen working once (owner test)
- [ ] Screenshots uploaded to imgur, links pasted in #6
- [ ] Repository pushed (main + v26.0) with the Alpha 2 commits
- [ ] GitHub Release v26.0-Alpha2 published with the shim DLL attached

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
