# PCL2.dl.swap v26.2-Alpha 1

> First Alpha of v26.2: the swap gains an **LSPosed-style module repository**. A JSON index lists
> community modules; the Swap Addon page shows them and installs one with a single click -
> download, SHA-256 check and the normal trust/permission pipeline included.

![Module repository card](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha1/shot-repository.png)

![PCL2 Themes installed through the repository](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha1/shot-themes-installed.png)

---

## What's new

### 1. 模块仓库（索引 + 一键安装）

- `data/addons-index.json`：模块清单（id/name/version/description/project/download/sha256/size）；
- 平台默认读取 `raw.githubusercontent.com/BlockConnectExp/PCL2.dl.swap/main/data/addons-index.json`；
- 网络失败自动回落到 **payload 内置索引**；
- 「模块仓库」卡片：条目列表 + Install / Reinstall / Project page / Refresh repository / Open index；
- 安装链路：后台下载 → SHA-256 校验（不匹配即删）→ 与文件选择同一安装器（签名/文件哈希/权限/作用域照常）；
- 首个条目：**PCL2 Themes v26.0-Alpha4**。

### 2. 权限兼容性修复（实测暴露）

| 问题 | 修复 |
|---|---|
| 第三方模块使用 `host.hooks.attach` / `host.ui.pages` / `host.settings.write` 等命名，严格模式误拒 | 权限名归一化：去 `host.` 前缀并映射 `hooks.attach→hooks`、`ui.pages→pages`、`settings.write→settings` 等 |
| `reflection` 门控导致 PCL2Themes 初始化失败 | **反射不再门控**：同进程无法真正沙箱化，权限只约束副作用（hooks/pages/files/network/…） |

## 已实测（2.99.0.1 真机）

| 场景 | 结果 |
|---|---|
| 索引解析 | `[repo] 已解析 1 个模块（内置索引）`；自检 `仓库.索引解析/内置索引 = ok` |
| 仓库卡片 | 显示 PCL2 Themes v26.0-Alpha4 / StarsailsClover / 26 KB / 描述与按钮 |
| 一键安装 | 下载 + SHA-256 校验 + 安装；`Addon 已启用：com.starsailsclover.pcl2themes（v26.0-Alpha 4）` |
| PCL2Themes 运行 | 钩子 `ModSecret.ThemeCheckOne ← pcl2themes`、主题 `#0 H=210 S=85 L=0`、backdrop/music 引擎启动，侧栏出现 `PCL2 Themes` 页面 |
| 权限别名 | `权限.别名归一化 = True`，无「权限被拒绝」 |
| 自检 | `ok=48 fail=0` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha1.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.2-Alpha1.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.2-Alpha1.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.2-Alpha1.dll` | 磁盘补丁产物 |

---

> 索引格式与发布流程见 `docs/addon-repository.zh.md`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
