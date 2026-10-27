# PCL2.dl.swap v26.0-Alpha 5

> Fifth public Pre-Release: the swap is now a **module platform for PCL** in the LSPosed sense -
> installable `.swapaddon` packages, per-module enable/disable with durable state, module-scoped
> bytecode hooks with real modes and chaining, a live event bus, a readable API over the whole
> launcher surface, and a self-check that reports which host build still fits.

---

## Highlights since Alpha 4

1. **模块作用域钩子**：`Api.Hooks.Attach(...)` 现在由平台包一层 gate。
   - 模块启用 → 调用模块 handler；模块关闭 → **立刻直通宿主原方法**（无需重启）；
     释放返回的句柄 → 该钩子从那一刻起直通。
   - 三种模式都真实生效：`Replace`（handler 独占调用）、`Prefix`（先观察后原方法、返回原值）、
     `Postfix`（先原方法后观察、返回原值）——模块不必自己够到原方法也能做前后观察。
   - 实例委托、引用类型与值类型参数都支持；handler 形状在**挂载时**校验并给出可读错误。
2. **每个方法只有一条钩子链**：第一个钩子负责装 gate，之后任何模块/模式的钩子加入同一条链
   （叠加两个跳转补丁会破坏执行——实测崩溃点，已在引擎层拒绝）。
3. **可观测性**：Swap Addon 页面按模块显示「钩子 N（命中 M）」，并有「钩子链（模块作用域）」卡片
   列出 目标方法 · 模块 · 模式 · 命中/跳过。
4. **事件总线**：`DialogOpening` / `UpdateRequested` / `DownloadCompleted` 由宿主真实广播，
   每个订阅者单独 try/catch，坏模块不影响其他模块与宿主。
5. **可读化 API**：`ISwapApi.Surface` 内嵌 2.99.0 / 2.99.0.1 全量调用面；`ISwapApi.Pcl` 提供
   Secret / Net / Animation / Settings / Base / Instance / Download / Launch / Loader 九个强类型族。
6. **作者工具链**：`tools/PclAddonPack` 一条命令产出 `.swapaddon`（dnlib 静态校验 entry、
   自动排除 SDK dll、打印 SHA-256）；`src/PCL2Swap.SampleAddon` 是覆盖事件 + 钩子 +
   实时设置页 + Reflect + 数据持久化的完整示例。
7. **可靠性与自检**：`PCL2SWAP_SELFTEST=1` 启动 5 秒后在宿主 UI 线程跑 27 项只读探针
   （2.99.0 实测 `ok=27 fail=0`）；`docs/local-e2e.zh.md` 记录了无需 ExLoader 的真机验证方法。

### 现场修复（Alpha 3/4 期间）

- 设置页侧栏行悬停崩溃（`InvalidCastException`：TranslateTransform → ScaleTransform）。
- 「切换主要页面失败（ID Setup）：未知的设置子页面种类：101」——`PageSetupLeft.PageGet` 现在也有钩子。
- 点击 Swap 页无反应（`PclUi.SetMember` 不支持静态成员）。
- 目标方法 IL 体过小（< 补丁长度）导致破坏相邻代码 —— 现在拒绝安装并抛异常。
- 同一方法二次挂钩导致栈损坏 —— 现在是钩子链。
- 模块开关语义：钩子类拦截立即生效；结构类修复需重启 PCL 完全回退（界面与日志都写明）。

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha5.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha5.zip` | source snapshot of this release |

SHA-256:

```
907d7c56435ac525fc94e1ec1957d011e22500498507f65c835e66e1a96e97d2  Pcl2Swap.Shim.x64.dll
331376e0f2e4cfd760da234697dfaa9f6fb064a8a7374d14d9f89bfa92145775  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → **Swap Addon**: three built-ins plus any installed module, each with a working
   enable/disable button (state survives a restart in `%APPDATA%\PCL2Swap\modules.json`),
   a hook counter per module and the 钩子链 card.
3. Drop a `.swapaddon` (build one with `tools/PclAddonPack`) into `%APPDATA%\PCL2Swap\addons\`
   and restart: it appears in the list, its settings page shows up in the 设置 sidebar, and its
   hooks follow the module toggle.
4. Swap Settings: host facts, scan policy and the data/log shortcuts.
5. Optional: set `PCL2SWAP_SELFTEST=1`, restart, read
   `%TEMP%\PCL2Swap\v26.0-Alpha5\pcl2swap.log` for `[selftest] 完成：ok=27 fail=0`.

## Known Limitations

- 真正的“卸载”是**行为级**的：gate 保持在入口处并直通原方法，物理跳转随进程结束而消失。
- 结构类修复（原版游戏页重驱 / 下载侧栏恢复）是启动时的一次性修改，关闭模块要重启才完全回退。
- 磁盘修补形状（`PclDlSwap.Loader.exe`）仍是纯静态改写；上述运行时能力属于注入形状，
  直到静态 bootstrap 落地。
- 模块与宿主同进程共享 CLR：按定义是受信任代码。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
