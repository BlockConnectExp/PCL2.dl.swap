# PCL2.dl.swap v26.0-Alpha 4

> Fourth public Pre-Release: the swap stops being "a patch with settings pages" and becomes a
> **module platform for PCL** - durable module state, a real event bus, a readable API over the
> whole launcher surface, and a self-check that tells you which host build still fits.

---

## Highlights

1. **设置板块的几个现场问题根治。**
   - “设置板块加载完很快报错然后崩溃”：反射创建的侧栏行默认开着悬停缩放动画，而 PCL 的左栏入场动画
     给这些行塞的是 `TranslateTransform`，`MyListItem.RefreshColor` 强转 `ScaleTransform` →
     `InvalidCastException`。现在按 XAML 行为关闭该开关。
   - “切换主要页面失败（ID Setup）：未知的设置子页面种类：101”：离开设置再回来时
     `FormMain.PageChangeActual` 会用残留 PageID 调 `PageSetupLeft.PageGet()`。现在 `PageGet`
     也有钩子：Swap 页返回缓存实例，真实 id 仍走宿主原实现（回到设置会停在上次的 Swap 页）。
2. **两个 Swap 页面改为 PCL 原生外观**：`MyCard` 卡片（与官方设置页相同的 25 / 25,40,25,25
   内边距）、`MyButton`、`ColorBrush` 主题画笔、行式信息布局。
3. **模块平台（LSPosed 式）**：`%APPDATA%\PCL2Swap\` 持久保存已安装 Addon、每 Addon 配置与
   `modules.json` 模块状态；禁用的模块启动即跳过，内置功能的钩子按开关放行/拦截；
   Swap Addon 页面每行带「启用 / 禁用」按钮，点击即时生效。
4. **事件总线**：`IHooks.DialogOpening` / `UpdateRequested` / `DownloadCompleted` 由宿主真实广播，
   每个订阅者单独 try/catch（坏 Addon 不影响其他模块与宿主）。
5. **可读化 API 全量覆盖**：`ISwapApi.Surface` 内嵌 2.99.0 / 2.99.0.1 的完整可调用面
   （308 类型 / 2450 方法 / 1117 属性）；`ISwapApi.Pcl` 提供 Secret / Net / Animation /
   Settings / Base / Instance / Download / Launch 八个强类型访问族；未覆盖成员仍可经
   `Surface` + `Reflect` 直达。
6. **兼容性自检**：`PCL2SWAP_SELFTEST=1` 时启动 5 秒后在宿主 UI 线程跑一遍只读探针
   （不下载、不更新、不启动游戏）。2.99.0 实测 `ok=25 fail=0`；换 PCL 版本先看这一行。
7. **本地真机 E2E 方法**：`docs/local-e2e.zh.md`（`tools/PclInject` 注入 + 测试副本改名
   `PCL_SingletonMutex` 绕过单实例，可与正在运行的 PCL 并行）。

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha4.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha4.zip` | source snapshot of this release |

SHA-256:

```
9c402f2fc9f7da2d92f6b8c2b3a73810b76b5b0cd16dd8bff168f2ef654d99ba  Pcl2Swap.Shim.x64.dll
81e6230f58595b297709e3a65b46941a1c1ae0a460372073052b41436f19475c  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置: the sidebar ends with **Swap Addon** and **Swap Settings**; hovering the entries no longer
   crashes the launcher.
3. Swap Addon: three built-ins (Download MC / OTA Protector / Cancel Button) with a working
   enable/disable button; the state survives a restart (written to `%APPDATA%\PCL2Swap\modules.json`).
4. Swap Settings: host facts, scan policy and the data/log directory shortcuts.
5. Regression: download page 原版游戏 redrive, About branding row, feedback routing, the
   cancellable update announcement.
6. Optional: set `PCL2SWAP_SELFTEST=1`, restart, and read `%TEMP%\PCL2Swap\v26.0-Alpha4\pcl2swap.log`
   for `[selftest] 完成：ok=25 fail=0`.

## Module packages

A `.swapaddon` is a zip with `manifest.json` + a net48 dll referencing `PCL2Swap.Api.dll`;
drop it into `%APPDATA%\PCL2Swap\addons\` (override the whole data root with `PCL2SWAP_DATA`).
The reference implementation lives in `src/PCL2Swap.SampleAddon`; the author guide is
`docs/swap-api.md` (module state, event bus, self-check, access families).

## Known Limitations

- Detours cannot be hot-unloaded yet: switching a module off gates its behaviour at runtime
  (hooks stay installed), so a *code* change still needs a launcher restart. Per-addon
  `IHooks.Attach` detach tokens are still intent-only.
- **Scope of a toggle**: hook-shaped behaviour (OTA interception, cancel button, download
  scanning, event subscriptions) starts or stops immediately. *Structural* repairs (原版游戏
  page redrive, download sidebar restore) are one-shot changes to live host objects made at
  start-up, so switching the module off keeps them until the launcher is restarted - the same
  model as Xposed/LSPosed. Verified on 2.99.0: with
  `{"swap.download-mc":{"enabled":false}}` in `modules.json`, the download page returns to the
  host's own "不再提供 MC 下载功能" state and both watcher threads exit immediately.
- The disk-patch shape (`PclDlSwap.Loader.exe`) remains static-only: the runtime features above
  belong to the injected shape until the static bootstrap lands.
- Addons share the host process and CLR: they are trusted code by definition.
- Compatibility is verified against 2.99.0 / 2.99.0.1; other builds should be judged by
  `PCL2SWAP_SELFTEST=1`.

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
