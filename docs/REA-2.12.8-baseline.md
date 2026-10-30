# PCL 2.12.8.1 赞助版 — REA 基线

> Summary: read-only extraction of the owner's PCL 2.12.8.1 build (sponsor line). This is an
> **extraction baseline**, not a claimed injection target - no runtime injection was performed
> against the owner's daily launcher.

---

## 1 样本与原则

| 项 | 值 |
|---|---|
| 版本 | 2.12.8.1（FileVersion / ProductVersion） |
| 文件 | `Plain Craft Launcher 2.exe` |
| 大小 | 5,586,944 字节 |
| SHA-256 | `d4f3e8f789cfd449ae9902ccadbadf5f35f3ac1b88793db37db5744cfd158fac` |
| MVID | `8558c9c3-b0f8-4e5c-8944-969432ccfb74` |
| 样本位置（只读副本） | `test/2.12.8/Plain Craft Launcher 2.exe` |

原则：

- 原文件位于 owner 日常目录，**只读复制**，不执行、不注入、不修改；
- 所有分析在副本上静态完成（dnlib / PclSurface）；
- 不将 2.12 标记为 `primary-injection-target`，避免误导用户。

## 2 Surface 统计对比

| 构建 | types | methods | properties | fields |
|---|---:|---:|---:|---:|
| 2.12.8.1 | 311 | 2452 | 1084 | 1518 |
| 2.99.0.0 | 308 | 2450 | 1117 | 1491 |
| 2.99.0.1 | 308 | 2450 | 1118 | 1491 |

2.12.8 与 2.99 系列规模接近（+3 类型 / +2 方法），属性/字段的增减反映赞助版功能差异。

## 3 关键能力存在性

| 能力 | 存在 |
|---|---|
| `PCL.ModSecret.UpdateStart` | 是 |
| `PCL.ModNet.NetDownloadByLoader` | 是 |
| `PCL.PageDownloadInstall.LoaderInit` | 是 |
| `PCL.PageDownloadLeft..ctor` | 是 |
| `PCL.PageSetupLeft.PageCheck / PageGet` | 是 |
| `PCL.PageOtherLeft.PageCheck / PageGet` | 是 |
| `PCL.ModMain.FrmSetupLeft / FrmOtherLeft / FrmDownloadInstall` | 是 |

结论：Swap 依赖的关键宿主面在 2.12.8 中**全部存在**，结构上与 2.99 同构。

## 4 偏移基线（v2128-rel410）

`data/offset-manifest.json` 已加入 `v2128-rel410`（role `extraction-baseline`，versionCode 410 为估算值）：

| 方法 | token | RVA |
|---|---|---|
| `ModSecret.UpdateStart` | `0x06000419` | `0x268f4` |
| `ModSecret.get_HasUpdatePermission` | `0x06000420` | `0x280ed` |
| `ModSecret.UpdateCheckByButton` | `0x06000418` | `0x26820` |
| `ModSecret.UpdateRestart` | `0x0600041a` | `0x26a78` |
| `ModSecret.UpdateReplace` | `0x0600041b` | `0x26bb4` |
| `PageDownloadInstall.LoaderInit` | `0x0600050c` | `0x308ac` |
| `PageDownloadLeft..ctor` | `0x06000bd6` | `0x64373` |

全量方法表：`data/methods-v2128-rel410.tsv`（6063 行方法记录，含 token/RID/RVA/签名/标志）。
全量 API 面：`data/pcl-surface-2.12.8.json`（311 类型）。

## 5 边界与后续

- **未验证**：注入后 self-test、下载页恢复、关于页/反馈分流、Swap 页面在 2.12.8 上的运行时行为；
- 若要在 2.12.8 上做运行时验证，必须在**克隆副本**上进行（单实例互斥体改名），不得触碰 owner 日常实例；
- 验证通过后再把该条目的 role 提升为 `primary-injection-target`。

## 6 复现命令

```powershell
# 1) 全量 API 面
.\tools\PclSurface\bin\Release\net48\PclSurface.exe `
  ".\test\2.12.8\Plain Craft Launcher 2.exe" ".\data\pcl-surface-2.12.8.json"

# 2) 全量方法表
.\tools\MethodDump\bin\Release\net8.0\MethodDump.exe `
  ".\test\2.12.8\Plain Craft Launcher 2.exe" ".\data\methods-v2128-rel410.tsv"

# 3) 写入 offset manifest（已执行）
.\tools\AddTarget\bin\Release\net8.0\AddTarget.exe `
  ".\data\offset-manifest.json" ".\test\2.12.8\Plain Craft Launcher 2.exe" `
  v2128-rel410 extraction-baseline Release 2.12.8.1 410 data/methods-v2128-rel410.tsv
```

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
