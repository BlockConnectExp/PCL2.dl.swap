# PCL2.dl.swap v26.3-Alpha 1

> First Alpha of v26.3: addon feature scopes become a real user-facing and runtime-enforced SDK
> contract, with bilingual scope metadata and a public Demo that consumes the API end to end.

![Editable bilingual feature scopes in Swap Addon](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha1/shot-scopes-v263.png)

![Demo Addon showing API 1.9 and live scope state](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha1/shot-demo-scopes-v263.png)

## SDK API 1.9

`Host.ApiVersion` is now **1.9.0**. The change is additive; modules built against API
1.0-1.8 continue to load.

- `ISwapApi.Scopes` exposes the current module's declared scopes, enabled subset and live
  `IsEnabled(id)` query.
- Manifest `scopes[]` accepts `id`, `name`, `nameEn`, `description`, `descriptionEn` and
  `defaultEnabled`. The old string-array shorthand remains valid.
- `Hooks.Attach(..., scopeId, ...)` binds a hook to one scope. Disabling that scope immediately
  forwards the hook to the original method without disabling the whole module or reattaching.
- Scope selections persist in `modules.json`, survive module reload and hot update, and remain
  isolated from other modules' `Changed` events.
- `PclAddonPack --scope` accepts repeatable
  `id;Name;Description;defaultEnabled;NameEn;DescriptionEn` records.

## Front end

Swap Addon module details now include an English/Chinese feature-scope editor with live
enabled-count badges and per-scope descriptions. The UI updates in place and keeps the user's
scroll position.

`examples/demo-addon` now demonstrates the full surface: `demo.core` controls module messaging and
the page body; `demo.advanced` controls an optional table/empty-state/code-block page section.

## Verification

| Gate | Result |
|---|---|
| Full Release build + offset manifest verification | PASS |
| Eight harness suites | PASS |
| PCL 2.99.0.0 real injection + selftest | PASS, `ok=74 fail=0` |
| PCL 2.99.0.1 real injection + selftest | PASS, `ok=74 fail=0` |
| Demo hot install on both official hosts | PASS, `enabled` |
| English scope UI: `1/2 -> 2/2` | PASS |
| Scope persistence in `modules.json` | PASS, `["demo.advanced","demo.core"]` |
| Scope event reaches the owning module | PASS |

## Artifacts

| Asset | Purpose |
|---|---|
| `PCL2.dl.swap-v26.3-Alpha1.zip` | ExLoader-facing shim DLL/DAT |
| `PCL2.dl.swap-Injector-v26.3-Alpha1.zip` | WinUI injector with seeded shim |
| `PCL2.dl.swap-Loader-v26.3-Alpha1.zip` | Disk-patch shape |
| `pcl2swap-2.99.0-v26.3-Alpha1.dll` | Patch-engine artifact |
| `source-v26.3-Alpha1.zip` | Signed-tag source snapshot |
| `SHA256SUMS.txt` | Release asset digests |

> English coverage remains progressive. Strings outside the dictionary still fall back to Chinese.
> ExLoader remains a manual listing/upload path; runtime validation in this release used the
> project's direct injector.
