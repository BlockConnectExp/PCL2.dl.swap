# PCL2.dl.swap v26.0-Alpha 3

> Third public Pre-Release: the Swap API platform - a readable addon SDK, a bytecode-hook bus,
> `.swapaddon` packages, and two new settings pages (**Swap Addon** / **Swap Settings**).

---

## Highlights

1. **Swap API 1.0.** `PCL2Swap.Api.dll` is the addon-facing contract: `ISwapAddon` lifecycle,
   `ISwapApi` facade (`Host` / `Hooks` / `Ui` / `Data` / `Reflect`), semantic events
   (`DownloadCompleted` / `DialogOpening` / `UpdateRequested`), manifest model and paths.
   The reflection bridge keeps every host member reachable even where the typed surface is thin.
2. **Addon packages.** A `.swapaddon` is a zip with `manifest.json` + a net48 dll (+ optional
   icon/assets). Packages drop into `%TEMP%\PCL2Swap\addons\`; the host unpacks, validates
   `apiVersion`, discovers the `ISwapAddon` implementation, and isolates failures per addon.
   A reference addon ships in the repository (`src/PCL2Swap.SampleAddon`).
3. **设置 → Swap Addon / Swap Settings.** Two new sidebar pages appear under PCL's settings.
   Swap Addon lists every addon with live status; Swap Settings shows host facts and the exact
   scan policy. Mechanism (measured against 2.99.0): detour on `PageSetupLeft.PageCheck`,
   two `MyListItem` entries with non-conflicting page ids, official navigation semantics replayed,
   and pages built from plain `MyPageRight` instances.
4. **Three built-in addons enabled by default**: Download MC, OTA Protector, Cancel Button.
   Their behaviour still ships inside the host core in this alpha; the addon migration is the
   next milestone.

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha3.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha3.zip` | source snapshot of this release |

SHA-256:

```
047fb60afcf681c9a57ea10ddb2bcd51937297d5c8285920dac0bda1e766d7c6  Pcl2Swap.Shim.x64.dll
a9c4a1ba4f41f8cb9f42a5678dcfbcdb8139928807d4071b7f19acdda1aaae6f  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. Open 设置: the sidebar now ends with **Swap Addon** and **Swap Settings**.
3. Swap Addon lists three addons as 已启用 (Download MC / OTA Protector / Cancel Button);
   Swap Settings shows the host version, shape, API version and the scan policy.
4. Existing behaviour is unchanged: download page, About row, feedback routing and the
   cancellable update prompt all keep working.

## Known Limitations (also in docs/swap-api.md)

- Detours cannot hot-unload yet; the per-addon enable/disable toggle is display-only for now.
- `Hooks.Attach` accepts static handlers only in API 1.0 and cannot be undone (the `IDisposable`
  token expresses intent; restore support is on the roadmap).
- Addons share the host process and CLR: they are trusted code by definition.
- A crashed addon is isolated (status `failed`) and never blocks the host or other addons.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
