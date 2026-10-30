# PCL2.dl.swap v26.2-Alpha 5

> Fifth Alpha of v26.2: the owner's **PCL 2.12.8.1 sponsor build** enters the offset manifest as
> a read-only **extraction baseline**, with a full API surface, a 6063-row method table and the
> seven tracked method offsets. No runtime injection was performed against the daily launcher.

---

## What's new

### 1. 2.12.8.1 REA 基线

| 项 | 值 |
|---|---|
| 版本 | 2.12.8.1（赞助版） |
| 大小 | 5,586,944 字节 |
| SHA-256 | `d4f3e8f789cfd449…` |
| MVID | `8558c9c3-b0f8-4e5c-8944-969432ccfb74` |
| 清单 id | `v2128-rel410`（role `extraction-baseline`） |

- 全量 API 面：`data/pcl-surface-2.12.8.json`（311 类型 / 2452 方法 / 1084 属性 / 1518 字段）；
- 全量方法表：`data/methods-v2128-rel410.tsv`（6063 行方法记录）；
- 关键方法（ModSecret/Net/Download/SetupLeft/OtherLeft）**全部存在**；
- 偏移：`ModSecret.UpdateStart 0x06000419 @0x268f4` 等 7 项已写入 manifest。

### 2. 边界（明确不宣称支持注入）

- 原文件为 owner 日常使用，**只读复制**到 `test/2.12.8`，未执行、未注入、未修改；
- role 保持 `extraction-baseline`；运行时注入需先在克隆副本（互斥体改名）验证 self-test、
  下载页恢复、关于页/反馈分流等，通过后再升级为 `primary-injection-target`。

### 3. 新工具 `tools/MethodDump`

把任意 PCL 样本导出为与 `data/methods-v*.tsv` 同构的方法表（token/RID/RVA/签名/标志），
用于每个新构建的基线归档。

## 已实测

| 场景 | 结果 |
|---|---|
| harness manifest · 2.12.8.1 | `resolved: v2128-rel410 @8558c9c3…` · PASS |
| harness manifest · 2.99.0.0 | `v299-rel413` · PASS |
| harness manifest · 2.99.0.1 | `v2990_1-rel415` · PASS |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha5.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.2-Alpha5.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.2-Alpha5.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.2-Alpha5.dll` | 磁盘补丁产物 |

---

> 详细分析见 `docs/REA-2.12.8-baseline.md`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
