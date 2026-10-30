# PCL2.dl.swap v26.1-Alpha 3

> Third Alpha of the v26.1 line: Swap gets its own **WinUI 3 injector**, and the whole feature set
> is re-verified on **PCL2 2.99.0.1 (build 415)** - including the four 2.99.0.1-specific quirks that
> used to need separate adaptations.

![注入器：目标探测与注入包列表](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha3/shot-injector.png)

![注入器：注入成功](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha3/shot-injector-success.png)

---

## What's new

### 1. WinUI 3 注入器

每个 Release 现在附带 `PCL2.dl.swap-Injector-<版本>.zip`（self-contained，解压即用）：

- **目标**：自动枚举运行中的 PCL2（`Plain Craft Launcher 2` / 测试副本），显示文件版本、
  MVID、SHA-256 与偏移清单匹配结果；也可手动选择 `Plain Craft Launcher 2.exe` 走「启动并注入」。
- **注入包**：列出 GitHub Releases 与 Release Note（标题/列表/引用/代码/图片），选择后注入；
  随附 shim 可离线工作，切换其它版本时按需从对应 Release 下载。
- **注入**：复用与 ExLoader 相同的 `LoadLibraryW` shim；启动路径会等主窗口出现、
  CLR 就绪后再注入（避开 `0x80131023`），注入后读 Swap 日志确认「宿主匹配」。
- **失败处理**：收集目标指纹/失败阶段/日志尾部，一键 `gh issue create` 提交到本仓库；
  若 gh 不可用，则保存到 `文档\PCL2Swap\reports\` 并复制到剪贴板。
- **CLI**：`--cli list` / `--cli inject --pid|--exe [--release|--shim]`，退出码 0/1/2，
  输出同时写入 `%TEMP%\pcl2swap-cli-last.txt`。

### 2. PCL2 2.99.0.1 全量回归

| 项目 | 2.99.0.0 (413) | 2.99.0.1 (415) |
|---|---|---|
| 注入自检 | `ok=30 fail=0` | `ok=30 fail=0` |
| 下载页「原版游戏」恢复 | 通过 | 通过 |
| 关于页 Swap 品牌卡 | 通过 | 通过 |
| 反馈分流（PCL2 / Swap / 取消） | 通过 | 通过 |
| Swap Addon / Swap Settings 子页 | 通过 | 通过 |
| 内置 Addon ×3 | 通过 | 通过 |

2.99.0.1 的四处历史坑位（侧栏折叠、设置子页 101、关于页/反馈偏移、跨线程 UI 插入）已全部
在 `docs/compat-matrix.zh.md` 中记录修复方式与复验步骤。

### 3. 工具链

```powershell
.\scripts\build-injector.ps1 -ToolVersion "v26.1-Alpha3"
# -> dist\PCL2.dl.swap-Injector-v26.1-Alpha3.zip  （含 shim\ + data\ + 自包含运行时）
```

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest` / `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| 注入器 · 运行中注入（2.99.0.1，PID） | 成功；InfoBar「Swap 已确认在目标进程中运行」，设置页出现 Swap Addon (3) |
| 注入器 · 启动并注入（2.99.0.1，CLI） | 成功；`exit=0`，日志 `宿主匹配：v2990_1-rel415`，验证日志 `%TEMP%\PCL2Swap\v26.1-Alpha3\pcl2swap.log` |
| 注入器 · CLI list | 4 个偏移清单目标 / 12 个 Releases / 随附注入包路径均正确 |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha3.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha3.zip` | 模块/loader 完整包 |
| `PCL2.dl.swap-Loader-v26.1-Alpha3.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha3.dll` | 磁盘补丁产物 |

---

> 安装位置：注入器解压即用；ExLoader 卡片继续使用 `.dat`。两者共用同一 payload 与 Addon 生态。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
