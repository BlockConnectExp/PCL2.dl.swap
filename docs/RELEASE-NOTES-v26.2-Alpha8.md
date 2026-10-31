# PCL2.dl.swap v26.2-Alpha 8

> Alpha 8 turns startup recovery from a broad "skip every addon" switch into a usable diagnosis
> path. Swap now records the exact module lifecycle phase before module code runs, names the
> provisional suspect in the next safe-mode session, and can disable that module before returning to
> a normal launch.

![Suspect module recovery in Swap Addon](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha8/shot-recovery-suspect.png)

## Suspect Module Recovery

- Before every addon reaches `create`, `OnLoad` or `OnEnable`, Swap atomically publishes the module
  id and lifecycle phase to `<data root>\recovery\startup-journal.json`.
- A process that exits before the stable window now leaves `LastStartedModule`,
  `LastCompletedModule`, `LifecyclePhase`, `SuspectPhase` and a provisional `SuspectModule`.
- The recovery card shows the suspect, the last phase and the last module that completed startup.
- **Disable suspect and leave safe mode** persists that module as disabled in `modules.json`, clears
  the persistent safe-mode flag, and keeps the current process isolated until restart.
- If the journal points to a module that no longer exists, Swap keeps safe mode enabled instead of
  clearing the recovery guard.

The suspect is a diagnostic clue, not an automatic verdict. A crash after every addon finished
loading can still make the last module look suspicious; the UI wording keeps that boundary explicit.

## SDK API 1.5

`Host.ApiVersion` is now **1.5.0**. All changes are additive; addons built for API 1.0-1.4 remain
source-compatible.

```csharp
bool safe = api.Recovery.SafeMode;
string reasonCode = api.Recovery.SafeModeReasonCode;
string phase = api.Recovery.LifecyclePhase;
string started = api.Recovery.LastStartedModule;
string completed = api.Recovery.LastCompletedModule;
string suspect = api.Recovery.SuspectModule;
string suspectPhase = api.Recovery.SuspectPhase;
string journal = api.Recovery.JournalPath;
string diagnostics = api.Recovery.Diagnostics();
```

`IRecoveryAccess` is intentionally read-only. An ordinary addon cannot clear safe mode or disable
another module. Mutation belongs to the built-in recovery UI.

The new front-end builder is a semantic page-level status surface rather than another card:

```csharp
page.Children.Add(api.Ui.Banner(
    "Network unavailable",
    "Requests will retry when the connection returns.",
    SwapHintType.Warning));
```

## English Coverage

The Swap Addon manager, detail view, install/update/uninstall confirmations, module status, scope
labels, logs and recovery card now select Chinese or English at construction time. This removes the
previous delay and mixed-language result for rows containing runtime values. PCL host text remains
on the incremental `HostTranslator` dictionary; Alpha 8 does not claim full launcher localization.

## Verification

| Gate | Result |
|---|---|
| Full payload / patch / loader build | PASS |
| Harness: `addontest` / `pcltest` / `pagetest` / `scantest` / `loaderhooktest` / `surfacetest` / `uitest` | PASS |
| Harness: `safemodetest` | PASS; lifecycle and reason code persisted, suspect identified, disabled in `modules.json`, next launch normal |
| SDK API 1.5 recovery backend | PASS |
| SDK API 1.5 `Banner` builder on STA Dispatcher | PASS |
| Real PCL 2.99.0.1 safe-mode injection | `ok=66 fail=0` |
| English recovery UI on real PCL 2.99.0.1 | Reason, suspect, phase, state and actions all rendered in English |
| Recovery UI screenshot | `shot-recovery-suspect.png` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha8.zip` | WinUI 3 injector, self-contained, includes the shim seed |
| `PCL2.dl.swap-v26.2-Alpha8.zip` | ExLoader-facing shim `.dll` + `.dat` + checksums |
| `PCL2.dl.swap-Loader-v26.2-Alpha8.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.2-Alpha8.dll` | Patch engine release-name artifact |
| `source-v26.2-Alpha8.zip` | Source snapshot at the signed tag |
| `shot-recovery-suspect.png` | English recovery UI evidence |

| Artifact SHA-256 | Value |
|---|---|
| Injector zip | `2137ecd80afef814abf94614eb6a0c1b76d59714d2de7e754bb97860a834aeb0` |
| ExLoader zip | `2d32955848729e1972da49791c09cd5a2c15e4dbabe7bb95639afb596ab91329` |
| Loader zip | `b5e9b4c457520aadb12b9c53a559258ac125b7defe4ac797a136fb1b052303a6` |

## Boundaries

- Safe mode still protects only addons managed by SwapHost; launcher failures before SwapHost starts
  are outside this recovery layer.
- The current session remains isolated after clearing safe mode or disabling the suspect.
- PCL 2.12.8.1 remains an extraction baseline only; the supported runtime line is official PCL
  2.99.0.0 and above, with 2.99.0.1 included in this release gate.

<!-- G itH ub@BlockConnectEx p | Blo ck Co nn ect La b@Sta rsai lsClove r -->
