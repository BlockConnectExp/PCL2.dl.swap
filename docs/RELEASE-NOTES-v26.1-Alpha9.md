# PCL2.dl.swap v26.1-Alpha 9

> Ninth Alpha of the v26.1 line: the first **real addon template** ships under `examples/demo-addon`,
> and the install/update/uninstall lifecycle is exercised end to end - fixing two hot-update bugs
> found along the way.

![Demo addon settings page](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha9/shot-demo-addon.png)

---

## What's new

### 1. Addon 模板工程（examples/demo-addon）

一个可直接编译、打包、安装的最小完整模块：

- **两类页面槽位**：设置页 + 更多页；
- **API 1.1/1.2 控件**：Slider / TextInput / Link / Separator / Card / InfoRow / Button；
- **双语**：`I18n.T("中文","English")`；
- **持久化**：`Data` 保存加载次数与自定义文本（重启后递增）；
- **后端**：`Pcl.Clipboard.SetText`、`Pcl.Shell.OpenFolder`；
- **严格权限**：manifest 声明 `pages,files,clipboard`，未声明的敏感调用会被拒绝。

配套 `docs/addon-template.zh.md` 说明构建、安装与复制方式。

### 2. 生命周期修复（实测暴露）

| 缺陷 | 修复 |
|---|---|
| 同版本更新时，新归档被旧版卸载删除，随后 `FileNotFoundException` | 更新前先把新包复制到 `.installing` 暂存，Retire 后再落正式归档名 |
| 热更新时旧 DLL 被 CLR 锁定，解包到同版本目录 `IOException` | 自动改用 `1.0.0-2` / `-3` 兄弟目录，旧目录排队延后删除 |
| `PCL2SWAP_E2E_UNINSTALL` 嵌套在安装分支内，单独设置时不执行 | 卸载改为独立分支，并同时刷新两个侧栏 |
| 自检权限探针在 `addons` 目录残留 | SelfTest finally 清理 `selftest.perms.scoped` |

## 已实测（2.99.0.1 真机）

| 阶段 | 结果 |
|---|---|
| 安装 | `Demo addon loaded (launch #1)`，状态 enabled，页面截图见上 |
| 同版本更新 | `Addon 更新：demo.sample 1.0.0 -> 1.0.0`，fallback 目录 `1.0.0-2`，无异常；launch #2 |
| 卸载 | `[e2e] 卸载结果：True`，`[page] 已注销 Demo Settings (102)`、`[more] 已注销 Demo Page (200)` |
| 清理 | 重启后 `待删除模块文件已清理完成`，`addons` 只剩三个内置模块 |
| 自检 | 每轮 `ok=45 fail=0` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha9.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha9.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha9.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha9.dll` | 磁盘补丁产物 |

---

> 模板工程位于仓库 `examples/demo-addon`，不属于运行时分发；复制后改 id/权限即可开始开发。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
