# PCL2.dl.swap v26.2-Alpha 3

> Third Alpha of v26.2: the module repository can point at a **custom index** (intranet mirror or
> a local JSON file) and retries the download once before falling back to cache/bundled data.

![Custom repository source](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha3/shot-repo-source.png)

---

## What's new

### 1. 自定义仓库源

- Swap Addon → Module repository 卡片新增：
  - `Source:` 当前索引地址；
  - `Custom source` 输入框（预填当前地址）；
  - `Save source & refresh` 按钮。
- 自定义源保存在 `<数据根>\repo\source.txt`；清空后恢复默认（GitHub raw）。
- **本地索引**：源可以是本地 JSON 文件路径（离线/内网镜像/自建仓库）。

### 2. 下载重试

索引下载失败自动重试一次（间隔 600ms），仍失败才进入 **磁盘缓存 → 内置索引** 回落。

## 已实测（2.99.0.1 真机）

| 场景 | 结果 |
|---|---|
| 自定义源往返 | `仓库.自定义源 = True`（临时本地 JSON 作为源，解析 1 条后恢复默认） |
| UI | Source 行 + Custom source 输入框 + Save source & refresh 按钮（截图） |
| 自检 | `ok=50 fail=0` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha3.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.2-Alpha3.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.2-Alpha3.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.2-Alpha3.dll` | 磁盘补丁产物 |

---

> 索引格式与安全模型见 `docs/addon-repository.zh.md`；自定义源同样受 SHA-256 与信任链约束。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
