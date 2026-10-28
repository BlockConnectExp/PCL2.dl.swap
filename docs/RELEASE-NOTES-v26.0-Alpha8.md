# PCL2.dl.swap v26.0-Alpha 8

> Eighth public Pre-Release: modules can now *observe the launcher's own work* - the loader/task
> surface (`Watch`, `CreateCombo`, `ListTasks`, `ListAll`) rides the host's real events instead of
> polling - and a field-level binding bug that could make an injected build fail with
> `MissingMethodException` after an earlier disk-patch run is fixed.

---

## Highlights since Alpha 7

1. **加载器观察面（`Api.Pcl.Loader`）**
   - `Watch(loader, onProgress, onStateChanged)` 直接订阅宿主自己的 `ProgressChanged` 与
     `OnStateChangedUi` 事件（**不是轮询**）；两个回调先用当前值各触发一次，加载器进入
     Finished/Failed/Canceled 时自动解除订阅，句柄 `Dispose` 也能提前解除。
   - `WatchPreviewFinish(loader, onLoaded)` 订阅 `PreviewFinish`。
   - 宿主事件参数是值类型（`LoadState` 枚举、`double`），模块侧只写 `Action`：
     平台内部用一个编译好的适配委托把参数装箱转交，每个订阅一份、随取消订阅释放。
2. **组合与任务面**
   - `CreateCombo(name, loaders, inputType)` 用宿主自己的 `LoaderCombo<TInput>` 组合加载器，
     任务栏进度、取消与聚合进度都保持宿主实现。
   - `ListTasks(loader, onlyRunning)` 把一个加载器摊平成任务列表（组合走实例
     `GetLoaderList`，普通任务走宿主静态递归走查）。
   - `ListAll(onlyRunning)` 读宿主的任务栏注册表 `ModLoader.LoaderTaskbar`。
   - `GetName` / `GetProgressWeight` / `SetProgressWeight` / `GetError` 读取或设置宿主字段。
3. **依赖遮蔽修复（现场级）**：宿主目录里如果留着上一次**磁盘补丁形状**复制过去的
   `PCL2Swap.Api.dll`，CLR 会先绑它、跳过 shim 内嵌的同名程序集 —— 真机表现是
   `MissingMethodException：找不到方法 ListAll`。现在注入入口先把解包目录的
   `PCL2Swap.Api` / `Gee.External.Capstone` / `MonoMod.Utils` / `Mono.Cecil` 预载定版；
   Loader 复制运行时文件也改成按 **SHA-256 内容**比较（原来同大小/同长度的旧文件会被误判成
   “已就位”）。
4. **自检扩容**：`PCL2SWAP_SELFTEST=1` 现在多跑三项加载器探针（`ListAll`、真实加载器订阅、
   空 `LoaderCombo<object>` 自驱动），2.99.0 实测 `ok=28 fail=0`，日志里能看到真实事件流：
   `→ Waiting`、`→ Loading`、`PreviewFinish`、`→ Finished`。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| harness 全套 | `--pcltest`（新增 11 项加载器断言）/ `--addontest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| 真机 2.99.0 自检 | `ok=28 fail=0`；日志实录 `loader 状态事件：PCL2Swap 自检组合 → Waiting / → Loading / → Finished` 与 `loader PreviewFinish 事件`（全部来自宿主自己的事件） |
| 真机 2.99.0 注入 | 宿主目录**故意留着**旧 `PCL2Swap.Api.dll` 的情况下仍正常装配：日志先打印四条「依赖预载」再从新程序集运行（这正是修复前 `MissingMethodException` 的现场） |
| 补丁形状（Loader bundle，干净目录） | `SwapHost 启动（形状 Patched）` + `磁盘补丁装配完成`；运行时文件按内容哈希判断，不再误留旧副本 |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha8.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha8.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha8.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
64edc25ffecc19a2814a289c7c95f4d86360bca5f6ffe39999f6c6128354a14e  Pcl2Swap.Shim.x64.dll
44f5f3e0c71331c9452360fdfa1f421580ef0ba4c7b24afab92c4d6eaa9c9cd6  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. Optional: set `PCL2SWAP_SELFTEST=1` before starting, then read
   `%TEMP%\PCL2Swap\v26.0-Alpha8\pcl2swap.log` - expect `[selftest] 完成：ok=28 fail=0`
   plus the loader event lines.
3. For an addon: `Api.Pcl.Loader.ListAll()` 列出任务栏加载器；对其中一个调用
   `Watch(...)`，进度/状态回调会随宿主自己的事件到达；`CreateCombo(...)` 能组合出宿主形状的
   组合任务。
4. 如果你此前用过 Loader（磁盘补丁）形状：确认 PCL 目录里的 `PCL2Swap.Api.dll` 等运行时文件
   是新版的（Loader 现在会按内容哈希覆盖旧副本），否则注入形状会退回旧程序集。

## Known Limitations

- 观察面只覆盖宿主已有的加载器对象；平台不提供“创建一个任意后台任务”的入口（那属于宿主的能力）。
- 自检里的 `CreateCombo` 故意用**空**组合并立即结束：组合构造会把子加载器重新挂到组合下，
  对正在运行的宿主加载器做这件事会改变它们的父子关系。
- 卸载模块仍是行为级（gate 直通原方法），物理跳转随进程结束消失。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
