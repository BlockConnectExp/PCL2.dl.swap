# PCL2.dl.swap v26.3-Alpha 2

> Second Alpha of v26.3: modules now have durable, versioned configuration and a real
> cross-module service registry, while feature scopes remain the default runtime gate.

![Feature scopes in Swap Addon](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha2/shot-scopes-v2632.png)

![Demo feature-scope controls](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.3-Alpha2/shot-demo-scopes-v2632.png)

## SDK API 1.10

`Host.ApiVersion` is now **1.10.0**. All additions are source-compatible with API 1.9.

- `ISwapApi.Config` implements `IAddonConfig`: schema version, sorted key snapshot, change
  notification, removal, and transactional `Migrate(targetVersion, migration)`.
- Legacy flat `config.json` files are read as schema 1. New saves use a schema envelope and an
  atomic replace with `config.json.bak`; failed migrations leave the previous values and schema
  intact.
- `ISwapApi.Services` implements `IAddonServices`: module-owned service registration, typed
  discovery, duplicate-id rejection, provider provenance and registry change events.
- Service registrations are removed automatically on module disable, reload, failure cleanup and
  uninstall. The `services` permission gates registration, lookup and subscription in strict mode.
- `IModuleCatalog` exposes `ConfigSchemaVersion`, `ConfigKeyCount` and `Services`, and the module
  detail page renders config/service diagnostics without reading private module paths.

## Template

`examples/demo-addon` now:

- migrates its config through `api.Config`;
- publishes `demo.sample.echo` through `api.Services`;
- removes the service when `demo.core` is disabled;
- renders a service/config summary through the native table UI.

## Verification

| Gate | Result |
|---|---|
| Full Release build + offset manifest verification | PASS |
| Eight harness suites | PASS |
| PCL 2.99.0.0 real injection + selftest | PASS, `ok=76 fail=0` |
| PCL 2.99.0.1 real injection + selftest | PASS, `ok=76 fail=0` |
| Config legacy load, migration, rollback and backup | PASS |
| Cross-module service lookup and duplicate rejection | PASS |
| Service cleanup on disable/reload/uninstall | PASS |
| English module detail UI | PASS |

> The service registry keeps CLR objects inside one PCL process. It is cooperation plumbing, not
> a sandbox; modules should share explicit contracts or JSON DTOs. ExLoader remains a manual
> listing/upload path.
