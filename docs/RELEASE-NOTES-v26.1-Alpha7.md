# PCL2.dl.swap v26.1-Alpha 7

> Seventh Alpha of the v26.1 line: manifest `permissions` stops being decoration. A module that
> declares permissions now runs under **strict gating** - any sensitive call it did not declare is
> rejected with a readable error. Modules without a declaration keep working in compatibility mode.

---

## What's new

### 1. 权限 opt-in 严格门控

规则（LSPosed 式「声明了才约束」）：

| manifest.permissions | 行为 |
|---|---|
| 非空 | **严格模式**：未声明的 gated 访问抛 `UnauthorizedAccessException`，并写入该模块日志 |
| 空 / 未声明 | **兼容模式**：保持 Alpha 6 及以前的行为（全部允许） |
| `<数据根>\security.json` 含 `"forceStrict":true` | 强制所有模块严格（连没声明的也按「无权限」处理） |

门控点：

| 权限 | 覆盖 |
|---|---|
| `hooks` | `IHooks.Attach`（模块作用域钩子） |
| `pages` | `Ui.RegisterSettingsPage` / `RegisterMorePage` |
| `reflection` | `Reflect` / `Surface` |
| `files` | `Pcl.Shell.OpenFolder/OpenFile` |
| `clipboard` | `Pcl.Clipboard.GetText/SetText` |
| `network` | `Pcl.Net.*` |
| `logs` | `Pcl.Logs.LogPath/Tail` |
| `secret` | `Pcl.Secret.*` |
| `launch` | `Pcl.Launch.*` |
| `settings` | `Pcl.Settings.*` |
| `instance` | `Pcl.Instance.*` |

透传（无害读取）：`Loader`、`Animation`、`Base`、`Download`、`Ui`、`I18n`、`Data`、`Log`。
声明 `"*"` 等价于全部允许。

### 2. UI 呈现

- 模块行：声明了权限的模块在权限摘要后显示「（严格）」；未声明的显示兼容语义。
- 模块详情：「权限声明」行显示 `（严格门控）` 或 `（未声明 —— 兼容模式，全部访问）`。
- 安装预览：权限行注明「严格门控：未声明的敏感访问会被拒绝」。

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | 全部 PASS（现有无权限模块走兼容模式，无回归） |
| 2.99.0.1 真机自检 | `ok=45 fail=0` |
| 宽松判定 | 内置模块（无 manifest）`HasPermission(hooks) = True` |
| 严格判定 | 声明 `hooks` 的 record：`hooks=True`、`pages=False` |
| Scoped 实际拦截 | 仅声明 `pages` 的模块调用 `Hooks.Attach` → `blocked`（UnauthorizedAccessException） |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha7.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha7.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha7.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha7.dll` | 磁盘补丁产物 |

---

> 本轮为安全/行为改进，没有新增页面或控件，因此没有 UI 截图；严格模式自检结果见上表。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
