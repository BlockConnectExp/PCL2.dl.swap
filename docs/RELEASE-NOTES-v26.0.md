# PCL2.dl.swap v26.0 — 正式版

> **This is the official release of the v26.0 line** (the tenth Alpha, published as `v26.0`).
> v26.0 turns PCL2.dl.swap from a set of patches into a **module platform for PCL**: installable
> `.swapaddon` packages, a scope model (delivery shape + host version), per-module logs and crash
> isolation, bytecode hooks with real modes and chains, a readable API over the whole launcher
> surface, and both delivery shapes (ExLoader injection and a static disk patch) running the same
> runtime.

![模块列表：作用域、日志、启停](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.0/shot-manager.png)

![Swap Settings：宿主信息与查毒策略](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.0/shot-settings.png)

---

## v26.0 里有什么（Alpha 1 → Alpha 10）

| Alpha | 主题 |
|---|---|
| Alpha 1–3 | 注入形状落地（ExLoader shim + payload）、磁盘补丁形状、下载页恢复、静默 OTA 拦截、云下载查毒沙箱、设置页侧栏（Swap Addon / Swap Settings） |
| Alpha 4 | 事件总线、真实可用的模块启停与持久化、真机自检（`PCL2SWAP_SELFTEST=1`） |
| Alpha 5 | 模块作用域钩子（Prefix / Postfix / Replace）、每目标一条钩子链、钩子计数与命中展示、`PclAddonPack` 打包器、示例模块 |
| Alpha 6 | 原生外观页面构建器（`Api.Ui.Card/Button/…`）、模块设置构件（CheckBox / Combo + `AddonSettings`）、设置子页崩溃类根因收口 + 页面度量对齐官方网格、补丁形状运行时 bootstrap |
| Alpha 7 | **模块管理器**：安装 / 更新 / 卸载全在界面里（无需重启）、`shapes` 作用域、延迟删除、manifest 数组解析修复 |
| Alpha 8 | **加载器观察面**：`Watch` / `WatchPreviewFinish`（宿主真实事件，非轮询）、`CreateCombo` / `ListTasks` / `ListAll`；修复宿主目录旧同名 DLL 遮蔽 payload 的现场问题 |
| Alpha 9 | **每模块日志** + **崩溃隔离**（抛错的 handler 自动停用、计数上屏） |
| Alpha 10 | **宿主版本作用域**（`hostVersions`）+ 安装前兼容预检、`Host.PclVersion`、打包器作用域参数 |

## Alpha 10 的收尾内容

- `manifest.json` 新增 `hostVersions`：每项 OR，支持 `"*"`、精确版本与 `">=2.99.0"` / `"<3.0"` /
  `"=2.99.0.1"`（`-beta`、`+413` 之类的后缀先归一化）；留空 = 任意宿主。
- 不在作用域内的模块**不启动**、状态「宿主版本不适用」，不算失败；安装时先做预检并把不匹配写进日志。
- `Api.Host.PclVersion` 暴露宿主版本；`PclAddonPack` 支持 `--shapes` / `--host-versions` / `--permissions`；
  模块行的作用域文本换行完整显示。

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha10.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha10.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha10.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
e243bc141496604cfd7140ce1f41f9fdb8d858cf2c2aed8f59214c6acad9759e  Pcl2Swap.Shim.x64.dll
e47181f3caf74ef05d96eebffcae59c26a4a48d152efb71ab274015954884ac7  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → **Swap Addon**：三个内置模块 + 已安装模块，开关可用且跨重启保持；每行有「详情 / 启用禁用 / 日志」，
   第三方模块还有红色「卸载」。
3. 设置 → Swap Addon → 模块管理 → **安装模块…**：选一个 `.swapaddon`，列表立刻出现该模块，**不需要重启 PCL**。
4. 可选：设 `PCL2SWAP_SELFTEST=1` 启动，读 `%TEMP%\PCL2Swap\v26.0-Alpha10\pcl2swap.log` 里的
   `[selftest] 完成：ok=28 fail=0`。

## Known Limitations

- 卸载是**行为级**的：gate 留在宿主入口处并直通原方法，物理跳转随进程结束消失。
- 结构类修复（原版游戏页重驱 / 下载侧栏恢复）是启动时的一次性修改，关闭模块要重启才完全回退。
- 模块与宿主同进程共享 CLR：按定义是受信任代码；`permissions` 是声明与展示，不是沙箱边界。
- `hostVersions` 比较的是宿主程序集版本串，不是 PCL 的 build number（需要精确到 build 时用 `PclVersion` 自行判断）。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
