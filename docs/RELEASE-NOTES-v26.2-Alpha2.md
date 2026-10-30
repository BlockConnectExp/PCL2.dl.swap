# PCL2.dl.swap v26.2-Alpha 2

> Second Alpha of v26.2: the module repository becomes safe and practical - an install
> confirmation with permissions/trust, a 30-minute refresh throttle, an offline disk cache, and
> update detection for installed modules.

![Install confirmation with permissions and trust](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha2/shot-repo-confirm.png)

![Installed module and refreshed sidebar count](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.2-Alpha2/shot-repo-installed.png)

---

## What's new

### 1. 安装前确认

一键安装现在分两步：

1. 后台下载 + SHA-256 校验 + `Review`（读 manifest 与信任链）；
2. 弹窗显示 **名称 / 版本 / 权限声明 / 信任状态**，用户点「安装」才落盘；取消则删除临时包。

### 2. 更新检测

- 索引版本 > 已装版本时，卡片显示「可更新（已装 x）」且按钮变为 **Update**；
- 已装同版本显示「已安装 / Reinstall」；
- 版本比较按数字段（`v26.0-Alpha 4` > `v26.0-Alpha 3`），自检 `仓库.版本比较` 覆盖。

### 3. 索引缓存与刷新节流

- 在线加载成功 → 写入 `<数据根>\repo\addons-index.json`；
- 在线失败 → 磁盘缓存 → 内置索引（三级回落）；
- 30 分钟内自动刷新直接返回缓存；「Refresh repository」按钮强制走网络。

### 4. 侧栏计数修复

安装/更新后重建设置侧栏，`Manage Swap addons (N)` 立即反映新数量（此前要重启）。

## 已实测（2.99.0.1 真机）

| 场景 | 结果 |
|---|---|
| 在线索引 | `[repo] 已解析 1 个模块（raw.githubusercontent…）` |
| 缓存写入 | `<数据根>\repo\addons-index.json`（607 字节） |
| 确认对话框 | 显示 `PCL2 Themes v26.0-Alpha 4 / Permissions: host.hooks.attach… / Trust: 未签名；文件校验通过（1 个）` |
| 安装 | 点 Install 后 `Addon 已启用：com.starsailsclover.pcl2themes`，主题/backdrop/music 正常 |
| 侧栏 | `Manage Swap addons (4)` 即时刷新，`PCL2 Themes` 页面出现 |
| 自检 | `ok=49 fail=0` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.2-Alpha2.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.2-Alpha2.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.2-Alpha2.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.2-Alpha2.dll` | 磁盘补丁产物 |

---

> 仓库格式与安全模型见 `docs/addon-repository.zh.md`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
