<p style="font-size:28px;text-align:center;"><b>PCL2.dl.swap ChangeLog</b></p>

> Records every released change set, newest first.

> [!NOTE]
> The artifact name carries both versions: `pcl2swap-{PCL2Ver.}-{Ver.}.dll`.

---
## 2026.10.28-05:45:00 Alpha 5: hook chains are visible in the UI

GitCommitHash: tag v26.0-Alpha5（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha5.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs        + CountFor/HitsFor（按模块统计）
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs 模块行显示钩子数/命中数 + 钩子链卡片 + 刷新按钮
.\docs\swap-api.md                                 模块页面观测说明
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|钩子可观测性|Swap Addon 页面每个模块行显示「钩子 N（命中 M）」，并新增「钩子链（模块作用域）」卡片列出 目标方法 · 模块 · 模式 · 命中/跳过次数；「刷新」按钮重建页面取实时值。真机验证：示例模块显示 `钩子 1（命中 1）`，卡片列出 `ModBase.ToUnixTimestampUtc · test.sample · Prefix：命中 1 次`|2026.10.28-05:45:00|PCL2.dl.swap

---
## 2026.10.28-05:35:00 Alpha 5: hook chains, handler shape validation, addon packer, demo module

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs           每个目标一条链（一次 patch，多 handler 加入）
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs       引擎层拒绝二次 patch（实测崩溃点）
.\src\PclDlSwap.Payload\Swap\HostApi.cs              handler 形状挂载前校验 + 走链式 Attach
.\src\PclDlSwap.Payload\Swap\AddonManager.cs         SetEnabled 先翻转状态再跑生命周期
.\src\PCL2Swap.SampleAddon\SampleAddon.cs            演示模块：事件 + Prefix 钩子 + 实时页面 + Reflect
.\tools\PclAddonPack\                                new（dnlib 静态校验 + 打包 .swapaddon）
.\docs\swap-api.md                                   §1 打包器用法 + §8.1 链式语义
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|钩子链|同一方法被第二个钩子（或第二个模块）命中时不再二次 patch：第一个钩子建立链与唯一 gate，后续注册加入链；每次调用依次跑 Prefix →（首个 Replace 或原方法）→ Postfix；禁用/释放只是把 handler 移出链。修复了「多钩子导致栈损坏」的真实崩溃|2026.10.28-05:35:00|PCL2.dl.swap
2|handler 形状校验|挂载前校验参数个数与类型（实例目标含接收者；值类型必须一致，引用类型可放宽到基类/接口），不匹配时直接抛带中文说明的 ArgumentException。此前错误会在无关调用路径上以 TargetParameterCountException 形式炸出|2026.10.28-05:35:00|PCL2.dl.swap
3|PclAddonPack|新增打包器：dnlib 静态校验 entry 实现 ISwapAddon（不执行代码）、自动排除 SDK dll、生成 manifest + zip 并打印 SHA-256|2026.10.28-05:35:00|PCL2.dl.swap
4|示例模块|SampleAddon 升级为全接口演示：事件订阅、Prefix 钩子（多候选目标回退）、带 DispatcherTimer 实时计数的设置页、通过 Reflect 调用宿主方法、数据持久化。真机验证：侧栏出现第三方页面，点击按钮后 `Prefix 钩子命中：4 次`，页面实时刷新|2026.10.28-05:35:00|PCL2.dl.swap

---
## 2026.10.28-05:25:00 Alpha 5: hook modes (Prefix / Postfix / Replace) are real

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs            mode 语义 + 计数快照
.\src\PclDlSwap.Payload\Swap\HostApi.cs                AttachScoped/AttachAddonHook 携带 mode
.\src\PclDlSwap.Payload\Swap\AddonManager.cs           ScopedHooks 透传 mode
.\src\PclDlSwap.Payload\Swap\SelfTest.cs               探针改用 Replace 显式声明
.\src\PclDlSwap.ExLoaderHarness\Program.cs             Prefix/Postfix/实例委托/引用参数断言
.\docs\swap-api.md                                     §8.1 补三种 mode 的语义表
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|HookMode|`IHooks.Attach` 的 mode 此前只被记录、不生效；现在三种模式都由平台实现：`Replace` 由 handler 独占（返回值即结果）；`Prefix` 先 handler 后原方法、返回原值；`Postfix` 先原方法后 handler、返回原值。Addon 不需要自己调用原方法，前后观察不再要求替换实现|2026.10.28-05:25:00|PCL2.dl.swap
2|Harness|新增断言：prefix 顺序 `before:7 → original:7` 且返回原值、postfix 顺序 `original:7 → after:7` 且返回原值、实例委托可挂载、引用类型参数（`string`）可传递、释放句柄后宿主方法体恢复|2026.10.28-05:25:00|PCL2.dl.swap
3|真机|自检保持 `ok=27 fail=0`（ScopedHook 探针显式使用 Replace），PCL 日志 0 异常|2026.10.28-05:25:00|PCL2.dl.swap

---
## 2026.10.28-05:10:00 Alpha 5: module-scoped hooks that follow the toggle

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs            new（gate 生成 + 注册表 + 直通/命中计数）
.\src\PclDlSwap.Payload\Swap\HostApi.cs               HookBusImpl.AttachScoped + HostApi.AttachAddonHook + ScopedToken
.\src\PclDlSwap.Payload\Swap\AddonManager.cs          ScopedApi 传入 addonId；Hooks 换成 ScopedHooks
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs       安全阀：IL 体过小拒绝挂钩（实测崩溃点）
.\src\PclDlSwap.Payload\Swap\SelfTest.cs              + ScopedHook 真机探针（附着→禁用→再启用→释放）
.\docs\swap-api.md                                    §8.1 模块作用域钩子
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块作用域钩子|Addon 通过 `Api.Hooks.Attach` 挂钩时，平台生成 ABI 同形的 gate：模块启用→走 Addon handler；模块禁用→直通宿主原方法；释放句柄→该钩子立即直通。实例委托也可用（不再限制静态方法）。这是 LSPosed「模块开关即生效」的核心语义，且不需要热卸载物理跳转|2026.10.28-05:10:00|PCL2.dl.swap
2|DetourInstaller 安全阀|实测崩过一次：目标方法 JIT 体只有 11 字节，而 x64 跳转补丁要覆写 14 字节，trampoline 会落进 padding/相邻方法导致栈损坏。现在 IL 体小于阈值直接拒绝安装并抛 NotSupportedException 并记日志|2026.10.28-05:10:00|PCL2.dl.swap
3|SelfTest|新增真机 ScopedHook 探针（仅测试模式 + 已装示例 Addon）：挂 `ModBase.ToUnixTimestampUtc`（值类型参数，验证 gate ABI），依次验证 命中/禁用直通/再启用命中，并打印 handler 命中数|2026.10.28-05:10:00|PCL2.dl.swap

---
## 2026.10.28-04:45:00 Alpha 5: Loader family, and the toggle explains its own scope

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\IPclAccess.cs                      + IPclLoaderAccess
.\src\PclDlSwap.Payload\Swap\PclAccess.cs             + LoaderAccess
.\src\PclDlSwap.Payload\Swap\AddonManager.cs          开关日志注明“结构类修复需重启回退”
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs   页面说明补同一条
.\src\PclDlSwap.Payload\Host\SwapLog.cs + Shim.cpp + Loader 版本号升 v26.0-Alpha 5
.\docs\swap-api.md + RELEASE-NOTES-v26.0-Alpha4.md    生效范围说明
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|Loader 族|新增 `IPclAccess.Loader`：任务栏进度（`LoaderTaskbarProgressGet` / `Refresh` / `TaskbarAdd`）、任意 LoaderBase 的 `State` / `Progress` / `IsRunning` / `Start(input, forceRestart)` / `Cancel` / `Failed`。harness `--pcltest` 扩到 30 项断言全绿|2026.10.28-04:45:00|PCL2.dl.swap
2|开关生效范围|回应 owner 实测：禁用 Download MC 后同一次运行里“原版游戏”仍在——因为页面/侧栏修复是启动时对宿主对象的一次性修改。现在开关日志与 Swap Addon 页面都写明：钩子类拦截立即生效，结构类修复需重启 PCL 完全回退；真机验证（modules.json 关闭 + 重启）下载页确实回到宿主原生的“不再提供 MC 下载功能”|2026.10.28-04:45:00|PCL2.dl.swap
3|版本|工作构建升到 `v26.0-Alpha 5`（`%TEMP%\PCL2Swap\v26.0-Alpha5\`），与已发布的 Alpha 4 资产区分开|2026.10.28-04:45:00|PCL2.dl.swap

---
## 2026.10.28-04:20:00 Alpha 4 repack 5/6: the hook event bus is live

GitCommitHash: tag v26.0-Alpha4（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha4.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\HostApi.cs            三个 Raise* 变为真实投递 + 订阅者逐个隔离
.\src\PclDlSwap.Payload\Swap\SwapHost.cs           + RaiseDialogOpening/UpdateRequested/DownloadCompleted
.\src\PclDlSwap.Payload\Features\AnnouncementGuard.cs  MyMsgBox 钩子广播 DialogOpening（含 CancelAdded）
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs      UpdateStart 广播 UpdateRequested；三个下载钩子广播 DownloadCompleted
.\src\PCL2Swap.SampleAddon\SampleAddon.cs          订阅三个事件并计数/记日志（参考实现）
.\src\PclDlSwap.Payload\Swap\SelfTest.cs           + 测试模式下驱动一次静默 UpdateStart 的事件探针
.\docs\swap-api.md                                 §7-§10：模块状态、事件总线、自检、访问族一览
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|事件总线|`IHooks.DialogOpening / UpdateRequested / DownloadCompleted` 从死代码变为真实投递：宿主每次弹窗、每次更新请求、每次下载任务交付后都会广播；每个订阅者单独 try/catch，坏 Addon 不会影响其他订阅者或宿主|2026.10.28-04:20:00|PCL2.dl.swap
2|SampleAddon|示例 Addon 订阅三个事件并在日志中记录（`OnEnable` 订阅 / `OnDisable` 退订），既是打包参考也是 harness 与真机的断言目标；`--addontest` 扩到 21 项|2026.10.28-04:20:00|PCL2.dl.swap
3|SelfTest|新增事件探针：测试模式下自检调用一次静默 `UpdateStart`，把 `UpdateRequested` 投递链路在真实宿主上跑通（测试模式不触网、不落盘）。真机实测 `ok=25 fail=0`，且打包的示例 Addon 记录到“收到更新请求：silent=True，地址数=0”|2026.10.28-04:20:00|PCL2.dl.swap
4|文档|`docs/swap-api.md` 补 §7 模块状态与启停（modules.json、开关语义、OnDisable/OnUnload）、§8 事件总线（触发点与参数表）、§9 自检与本地真机调试、§10 类型化访问族一览；Addon 安装目录更正为 `%APPDATA%\PCL2Swap\addons`|2026.10.28-04:20:00|PCL2.dl.swap

---
## 2026.10.28-04:05:00 Alpha 4 repack 4: module state that means something + real-host self-check

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\SwapPaths.cs                        + DataRoot (%APPDATA%, PCL2SWAP_DATA 可覆盖) / ModulesFile
.\src\PclDlSwap.Payload\Swap\AddonManager.cs           + 模块状态读写、IsEnabled/SetEnabled、禁用跳过
.\src\PclDlSwap.Payload\Swap\SelfTest.cs               new（PCL2SWAP_SELFTEST=1 只读自检）
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs    + 每行启用/禁用按钮、状态文案、原生卡片
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs           + InvalidatePage（开关后重建并重进当前页）
.\src\PclDlSwap.Payload\Features\* + Host\CoreInstaller.cs  三处钩子按模块开关放行/拦截
.\docs\local-e2e.zh.md                                 new（无需 ExLoader 的真机 E2E 方法）
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块启停|开关不再只是显示：`modules.json` 持久化模块状态（默认 `%APPDATA%\PCL2Swap`，可用 `PCL2SWAP_DATA` 覆盖），禁用的模块在启动阶段直接跳过，内置功能的钩子按开关放行/拦截（Download MC 的 LoaderInit 与两个看门线程、OTA 的 UpdateStart、Cancel Button 的 MyMsgBox）。Swap Addon 页面每行带「启用/禁用」按钮，点击后立即重建并重进页面|2026.10.28-04:05:00|PCL2.dl.swap
2|SwapPaths|持久数据根与临时根分离：Addon 安装目录、每 Addon 配置、模块状态放 `%APPDATA%\PCL2Swap`（不再随 %TEMP% 被清掉）；日志与 payload 解包仍在 %TEMP%|2026.10.28-04:05:00|PCL2.dl.swap
3|SelfTest|`PCL2SWAP_SELFTEST=1` 时启动 5 秒后在宿主 UI 线程跑只读自检：Host/Surface/Reflect/Secret/Animation/Settings/Base/Instance/Download/Launch 加上模块状态逐项记日志（不发起下载、不触发更新、不启动游戏）。真机 2.99.0 实测 `ok=24 fail=0`|2026.10.28-04:05:00|PCL2.dl.swap
4|本地 E2E|新增 `docs/local-e2e.zh.md`：`tools/PclInject` 注入 + 测试副本改名 `PCL_SingletonMutex` 绕过单实例（可与 owner 的 PCL 并行）+ 隔离 TEMP/PCL2SWAP_DATA + 坐标表与判定标准|2026.10.28-04:05:00|PCL2.dl.swap
5|Harness|`--addontest` 扩到 17 项断言：模块状态可持久化、禁用即翻转运行时门、再启用恢复，Addon 存储落在持久根（测试用 PCL2SWAP_DATA 隔离）|2026.10.28-04:05:00|PCL2.dl.swap

---
## 2026.10.28-03:05:00 Alpha 4 repack 3: settings sub-page resolution + native card pages

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs            + PageGet detour (host sub-page resolution)
.\src\PclDlSwap.Payload\Swap\Views\SwapUi.cs            new (MyCard / MyButton / theme-brush builders)
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs     rewritten on the native card layout
.\src\PclDlSwap.Payload\Swap\Views\SwapSettingsView.cs  rewritten on the native card layout
.\src\PclDlSwap.ExLoaderHarness\Program.cs              + PCL.PageSetupLeft stub and --pagetest
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PageRegistry|修复“切换主要页面失败（ID Setup）：未知的设置子页面种类：101”：宿主的 FormMain.PageChangeActual 会用残留的 PageID 调 PageSetupLeft.PageGet()，而 PageGet 的 switch 对 100/101 直接抛异常。现在对 PageGet 也装 detour：命中 Swap id 返回缓存页实例，真实 id 仍走宿主原实现（枚举参数以 int32 传递，回填原实现时按签名装箱）|2026.10.28-03:05:00|PCL2.dl.swap
2|SwapUi / SwapAddonView / SwapSettingsView|两个 Swap 页面改用 PCL 原生外观：MyCard 卡片（标题行 + 25/40/25/25 内边距，与 Pages/PageSetup/PageSetupLaunch.xaml 一致）、MyButton 按钮（35 高、ColorType）、ColorBrush1 主题画笔、行式信息布局；宿主控件缺失时自动退回普通 WPF 布局|2026.10.28-03:05:00|PCL2.dl.swap
3|Harness|新增 `--pagetest`：在真实 JIT 的 PCL.PageSetupLeft 桩上验证 PageCheck 放行/接管、PageGet(100/101) 返回缓存页、真实 id 仍走原实现（同时覆盖 enum→int 钩子 ABI）|2026.10.28-03:05:00|PCL2.dl.swap
4|IPclAccess（Instance / Download / Launch）|新增三个强类型族：`Instance`（选中目录/选中版本、GetMcFoolName、按宿主缓存枚举实例，并暴露 McInstance 的 Name/Version/PathIndie/Modable/GetJsonPath/VersionDisplayName 与 ModLaunch.GetNativesFolder）、`Download`（PreferMojang 开关、DlClientListGet、Assets/Library/LauncherOrMeta/Mod 源、DlModRequest 走宿主签名请求）、`Launch`（McLaunchStart、McLoginName、McLoginAble）。harness 断言 21/21 通过|2026.10.28-03:20:00|PCL2.dl.swap

---
## 2026.10.28-02:05:00 Alpha 4: Swap API base (surface index + typed PCL families) and the settings-sidebar crash fix

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\IPclAccess.cs                        + Settings / Base families (Instance / Download / Launch / Loader next)
.\src\PclDlSwap.Payload\Swap\SurfaceIndex.cs            new (embedded pcl-surface-2.99.0 / 2.99.0.1)
.\src\PclDlSwap.Payload\Swap\PclAccess.cs               new (Secret / Net / Animation / Settings / Base)
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs         + entry-byte hex dump (前/后 16 字节)
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs + redrive watcher for a pre-existing download page
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs            FIX: sidebar rows must disable IsScaleAnimationEnabled
.\tools\PclInject\                                      new (local LoadLibraryW injector for E2E without ExLoader)
.\data\pcl-surface-2.99.0.json                          new (308 types / 2450 methods)
.\data\pcl-surface-2.99.0.1.json                        new
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PageRegistry|修复“设置板块加载完很快报错然后崩溃”：反射创建的 `MyListItem` 默认 `IsScaleAnimationEnabled=true`，而 `MyListItem.RefreshColor` 在该开关为真时把 `RenderTransform` 强转成 `ScaleTransform`；`MyPageLeft.TriggerShowAnimation` 恰好给被动画的行设置 `TranslateTransform`，于是首次悬停（或动画收尾的 RefreshColor 回调，此时指针可能已经停在新行上）抛 `InvalidCastException`，被 PCL 当成未知错误后退出。现在插入前按 XAML 行为关闭该开关|2026.10.28-02:05:00|PCL2.dl.swap
2|DownloadPageRestore|注入前若“原版游戏”页已经存在（报错页），看门线程会用真实装载器重驱一次，不再保持报错|2026.10.28-01:00:00|PCL2.dl.swap
3|SurfaceIndex / PclAccess|`ISwapApi.Surface` 暴露全量可调用面索引；`ISwapApi.Pcl` 暴露 Secret / Net / Animation / Settings / Base 五个强类型族，调用直达宿主方法|2026.10.28-01:25:00|PCL2.dl.swap
4|DetourInstaller|每次安装 detour 记录目标入口前 16 字节（安装前/后），字段级证据随日志落地|2026.10.28-01:25:00|PCL2.dl.swap
5|tools/PclInject|新增本地注入器：`--exe/--pid` + `--dll`，用 LoadLibraryW 装载 shim，使注入形状无需 ExLoader 也能端到端复现|2026.10.28-02:05:00|PCL2.dl.swap
6|PclUi.SetMember|修复“点击 Swap 页没有反应”：`SetMember` 对 `Type` 目标不支持静态成员（改成在 `RuntimeType` 上查找 `AniControlEnabled`），抛 `MissingMemberException` 并中断整次页面切换。现在与 `GetMember`/`Invoke` 一致地处理静态成员；`PageRegistry` 的动画门控加减同时降级为“失败不阻断切换”|2026.10.28-02:08:00|PCL2.dl.swap

### 证据（本次修复）
```
字段现场（owner 01:40 运行，%TEMP%\PCL2Swap\v26.0-Alpha4\pcl2swap.log 末尾）：
  01:40:37.975 [page] PageCheck 钩子进入（tag=(null)，sender=MyListItem，线程 1）
  01:40:38.154 Swap 页面：已在设置侧栏插入 2 个条目。
  01:40:38.214 [page] PageCheck 钩子进入（tag=0，sender=MyListItem，线程 1）   ← 日志到此为止
PCL 自身日志（...\正式版 2.99.0\PCL\Log1.txt）：
  01:40:39.944 E 程序出现未知错误：无法将类型为"System.Windows.Media.TranslateTransform"的对象
                       强制转换为类型"System.Windows.Media.ScaleTransform"。
                  在 PCL.MyListItem.RefreshColor
  01:40:42.847 I 程序已退出，返回值：Exception

本地复现（tools/PclInject 注入 dist\exloader\Pcl2Swap.Shim.x64.dll 到 test\2.99.0\Plain Craft Launcher 2.exe，
该 exe 与现场 2.99.0 构建 SHA-256 完全一致 6B6E1260...）：
  修复前：同样在插入后悬停即抛 InvalidCastException（现场日志一致）
  修复后：设置页正常显示 Swap Addon / Swap Settings；在侧栏整列反复悬停 ≥10 秒，
         进程存活，PCL Log1.txt 中 "程序出现未知错误|InvalidCastException" 计数 = 0

本地 E2E（第二处修复后，同一注入形状）：
  点击 Swap Addon   → [page] 调用宿主的 PageChangeRun → 同步部分完成（id=100）→ 页面渲染 3 个内置 Addon
  点击 Swap Settings → 同步部分完成（id=101）→ 页面渲染宿主版本/形状/API/日志目录与查毒策略
  两次切换后进程均存活，PCL 日志 0 异常
```

---
## 2026.10.27-19:30:00 Alpha 3: Swap API platform - SDK, addon packages and settings pages

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\                                     new (SDK 1.0.0)
.\src\PCL2Swap.SampleAddon\                             new (reference addon)
.\src\PclDlSwap.Payload\Swap\HostApi.cs                 new
.\src\PclDlSwap.Payload\Swap\AddonManager.cs            new
.\src\PclDlSwap.Payload\Swap\AddonManifestReader.cs     new
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs            new
.\src\PclDlSwap.Payload\Swap\SwapHost.cs                new
.\src\PclDlSwap.Payload\Swap\BuiltInAddons.cs           new
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs     new
.\src\PclDlSwap.Payload\Swap\Views\SwapSettingsView.cs  new
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs           + SwapHost.Start (injected)
.\src\PclDlSwap.Payload\Host\Bootstrap.cs               + SwapHost.Start (patched, reserved)
.\src\PclDlSwap.Payload\Host\SwapLog.cs                 + Error(msg, ex)
.\src\Pcl2Swap.Shim\Shim.cpp                            + RCDATA 106 (PCL2Swap.Api.dll)
.\src\Pcl2Swap.Shim\build.ps1                           + api assembly input
.\src\PclDlSwap.ExLoaderHarness\Program.cs              + --addontest
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PCL2Swap.Api|New addon SDK (1.0.0): `ISwapAddon` lifecycle, `ISwapApi` facade (Host/Hooks/Ui/Data/Reflect), semantic event args, `AddonManifest` model, `SwapPaths` and an `apiVersion` range check. Addons compile against this stable contract; the payload implements it|2026.10.27-19:30:00|PCL2.dl.swap
2|SwapHost|The payload now hosts the platform: `HostApi` (readable wrappers over the reflection bridge, the v2 detour engine and SwapLog), `AddonManager` (.swapaddon unpack with zip-slip guard, manifest validation, `ISwapAddon` discovery, per-addon failure isolation) and three built-in addons - Download MC, OTA Protector, Cancel Button|2026.10.27-19:30:00|PCL2.dl.swap
3|PageRegistry|Adds 设置 sidebar pages "Swap Addon" and "Swap Settings": detours `PageSetupLeft.PageCheck`, inserts two `MyListItem` entries (Tag=100/101, RadioBox) and replays the official navigation semantics (`AniControlEnabled` / `IsPageSwitched` / `PageChangeRun` / `PageID`) because `PageChange`'s switch throws on unknown ids. Pages are plain `MyPageRight` instances whose `Child` is the view (measured: the constructor builds no UI and animations follow `PanContent ?? Child`)|2026.10.27-19:30:00|PCL2.dl.swap
4|Views|`SwapAddonView` lists built-in and installed addons with live status and errors; `SwapSettingsView` shows host facts and the scan policy verbatim|2026.10.27-19:30:00|PCL2.dl.swap
5|Shim|Embeds `PCL2Swap.Api.dll` as RCDATA 106 and extracts it next to the payload so the dependency resolver finds it; `TOOL_VERSION` -> v26.0-Alpha3|2026.10.27-19:30:00|PCL2.dl.swap
6|Harness|`--addontest` packs a real .swapaddon with the sample addon and asserts unpack -> manifest validation -> discovery -> OnLoad/OnEnable -> API version end to end. Full suite PASS: copytest, hooktest, uitest, scantest, loaderhooktest, addontest, manifest (2.99.0 + 2.99.0.1) and the 10/10 integration run|2026.10.27-19:30:00|PCL2.dl.swap

version: v26.0-Alpha 3

---
## 2026.10.26-22:40:00 Alpha 2 (release cut): download-path performance and sidebar crash fixes

> 版本号规则（Owner，2026-10-26）：Alpha 号随**发布**递增。Alpha 1 是唯一已发布版本，因此本次
> 对外构建收敛为 **v26.0-Alpha 2**；下方 Alpha 3-5 的条目属于未发布的内部迭代，全部并入本次
> Alpha 2 的变更集。

GitCommitHash: 1f1514a (release notes and hash recorded in the follow-up docs commit)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +57 -45
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +55 -8
.\src\PclDlSwap.Payload\Sandbox\DownloadSandbox.cs       +35 -0
.\src\PclDlSwap.Payload\Sandbox\ScannerSet.cs            +59 -8
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +77 -12
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +17 -8
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +105 -4
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|CoreInstaller/DownloadSandbox|Launcher-cloud downloads now run through PCL's own multi-source downloader; the scanner runs afterwards on the produced file and deletes it when a scanner objects. The previous flow re-downloaded every launcher-cloud file through a single-threaded WebClient inside the hook and spawned one MpCmdRun process per file - both halves of the reported performance loss|2026.10.26-22:40:00|PCL2.dl.swap
2|CoreInstaller|Scan policy (per Owner): only executable/archive extensions are scanned, and anything landing inside the Minecraft directory (game files, mods, resource packs, shaders) skips the scanner entirely, while launcher files fetched from PCL's servers keep it. JSON/PNG and friends pass through untouched|2026.10.26-22:40:00|PCL2.dl.swap
3|DownloadPageRestore|The 2.99.0.1 sidebar repair no longer writes the PageID field. It now drives PCL's own navigation (Checked -> PageChange -> PageGet) and only finishes after verifying ModMain.FrmDownloadInstall exists. The old field write skipped PageGet, left the page object null, and the next click died in PCL.PageDownloadLeft.ItemInstall_Click with a NullReferenceException (field logs 22:23:35 and 22:25:28)|2026.10.26-22:40:00|PCL2.dl.swap
4|ScannerSet/SwapLog/PclUi|MpCmdRun and clamscan lookups are cached, the log mirror disables itself after the first failure, the host logger is resolved once, and the dispatcher method table is cached (the pollers call OnUiThread every 300 ms until a repair lands)|2026.10.26-22:40:00|PCL2.dl.swap
5|Harness|New --scantest (extension + game-directory policy) and a click-safety regression that reproduces the field NullReferenceException path; integration suite is now 10/10|2026.10.26-22:40:00|PCL2.dl.swap

### Loader (disk-patch) shape: Alpha 2 rebuild (2026-10-27)

- The disk-patch shape is a pure static IL rewrite (download-page gate restore, 2.99.0.1 sidebar repair, silent-OTA confirmation prefix, update-permission gate). Its runtime profiler hook was never wired up, so the bundle is a version-alignment rebuild rather than a behaviour change.
- `HookEntryPoints.DownloadHook` (the loader-side hook used once profiling is wired) now rebuilds PCL's own `NetFile`/`LoaderDownload` pipeline through reflection - multi-source failover, progress sync into the loader and FileChecker validation - instead of the old single-threaded WebClient that also skipped the checker. The harness regression `--loaderhooktest` pins the call chain against a stub that mirrors PCL 2.99.0's IL.
- `ScanPolicy` (shared by both shapes) now owns the "game content is never scanned" rule; `CoreInstaller` keeps a thin wrapper so the existing `--scantest` still passes.
- `src\PclDlSwap.Profiler\build.cmd`: the trailing watermark line became `rem` (the bare `#` line made cmd exit non-zero after a successful build, so build.ps1 always reported "profiler failed").
- Bundle: `PCL2.dl.swap-Loader-v26.0-Alpha2.zip` (loader + patcher + payload + dnlib + MonoMod.Utils/Mono.Cecil + Capstone + `data/offset-manifest.json`).

version: v26.0-Alpha 2

---
## 2026.10.26-21:45:00 Alpha 5: UI-thread marshalling, manifest merge and double-injection guards

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +92 -27
.\src\PclDlSwap.Payload\Host\OffsetManifest.cs           +111 -45
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +58 -0
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +2 -2
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs          +28 -0
.\src\PclDlSwap.Payload\Features\AboutBranding.cs        +44 -18
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +32 -8
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +158 -12
.\src\PclDlSwap.ExLoaderHarness\PclDlSwap.ExLoaderHarness.csproj   +6 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PclUi|OnUiThread now marshals through WPF's real overloads (`Invoke(Delegate, object[])` / `Invoke(Action)`). The previous code looked for a single-parameter `Invoke(Delegate)` that WPF does not have, silently ran the body on the polling thread and produced the field-log failure "调用线程无法访问此对象" for the About row (2.99.0 and 2.99.0.1) and the 2.99.0.1 sidebar repair|2026.10.26-21:45:00|PCL2.dl.swap
2|PclUi|The WPF Application type is resolved by walking the loaded assemblies. A named `Type.GetType("System.Windows.Application, PresentationFramework")` does not bind from the payload's LoadFrom context - measured by the new uitest, which now runs a real Dispatcher on an STA thread|2026.10.26-21:45:00|PCL2.dl.swap
3|OffsetManifest|The disk and embedded manifests are parsed and merged by target id (disk wins on conflict), and JSON arrays are read as `IEnumerable`. The previous disk-first single source made every retained build log "宿主不在偏移清单中" even though the MVIDs matched; both 2.99.0 and 2.99.0.1 now resolve to their manifest entries|2026.10.26-21:45:00|PCL2.dl.swap
4|AboutBranding/DownloadPageRestore|A failed insert/repair is retried up to 40 poll rounds (first failure warns, every tenth logs) instead of silencing the feature for the whole session; the 2.99.0.1 sidebar collapse now recovers after the page appears|2026.10.26-21:45:00|PCL2.dl.swap
5|CoreInstaller/DetourInstaller|`HostAlreadyPatched` / `IsHooked` probe the JIT entry (FF25 on x64, E9 on x86) before installing; a second injection into the same process (ExLoader firing twice, or a newer shim over a running PCL) is refused instead of stacking a second detour over an already-patched body|2026.10.26-21:45:00|PCL2.dl.swap
6|Harness|New `--uitest` (real WPF dispatcher; cross-thread and inline assertions) and `--manifest` (per-host MVID match against real PCL binaries; run once per host because .NET reuses same-identity assemblies inside one process). Integration suite stays 9/9|2026.10.26-21:45:00|PCL2.dl.swap
7|Shim|`TOOL_VERSION` -> v26.0-Alpha5: the extraction directory moves to `%TEMP%\PCL2Swap\v26.0-Alpha5` so a payload locked by an already-running PCL cannot block the new build (the Alpha 4 directory held a 129024-byte payload locked by PID 42888 while the new payload is 132608 bytes)|2026.10.26-21:45:00|PCL2.dl.swap

version: v26.0-Alpha 5

---
## 2026.10.26-08:35:00 Alpha 4: managed hook targets and IL-copied originals (AccessViolation fix)

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Hook\X64Detour.cs            rewritten (v2)
.\src\PclDlSwap.Payload\Hook\DetourInstaller.cs      rewritten (v2)
.\src\PclDlSwap.Payload\Host\OriginalFactory.cs      new (MonoMod IL copies)
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs        hooks -> managed methods
.\src\PclDlSwap.Payload\Features\AnnouncementGuard.cs    hook -> managed method
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  hook -> managed method
.\src\PclDlSwap.Payload\Features\FeedbackRouter.cs       hook -> managed method
.\src\PclDlSwap.Payload\Host\ExLoaderEntry.cs        generic dependency resolver
.\src\Pcl2Swap.Shim\Shim.cpp                         embeds MonoMod.Utils + Mono.Cecil
.\src\Pcl2Swap.Shim\build.ps1                        new resources, /Gs9999999
.\src\PclDlSwap.ExLoaderHarness\Program.cs           v2 tests (copytest/hooktest)
.\docs\av-root-cause.md                              new (root cause writeup)
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|X64Detour/DetourInstaller|The detour now jumps to a managed method body (PrepareMethod + GetFunctionPointer) instead of a Marshal.GetFunctionPointerForDelegate thunk. The thunk assumes it is entered from unmanaged code, and JIT callers trip the GC mode - field crashes proved this (PCL 2.99.0 crash at TryFeedback IL+13 `e.Handled=true`; 2.99.0.1 crash inside the MyImage worker)|2026.10.26-08:35:00|PCL2.dl.swap
2|OriginalFactory|"Original" now runs from a full IL copy of the target method generated with MonoMod.Utils' DynamicMethodDefinition and invoked through reflection - a genuine managed call, no relocated prologue. The stolen-prologue trampoline, Capstone disassembly and the relocation engine are gone|2026.10.26-08:35:00|PCL2.dl.swap
3|X64Detour|v2 only backs up the entry and writes the 14-byte absolute jump, with a short-method guard; the old RIP-relative relocation failures (and the crash while rejecting `mov eax,[rip+...]` prologues) cannot happen any more|2026.10.26-08:35:00|PCL2.dl.swap
4|Hooks (OTA/download/dialog/loader/feedback)|All hooks became static managed methods with the target's ABI (reference parameters as object, value parameters exact); their originals are reached through DetourInstaller.InvokeOriginal|2026.10.26-08:35:00|PCL2.dl.swap
5|Harness|New --copytest (IL copy equivalence) and --hooktest (managed hook + 2000 calls under GC pressure + original through the copy). The old trampoline-based engine/abi tests were retired. Integration suite: 9/9|2026.10.26-08:35:00|PCL2.dl.swap

version: v26.0-Alpha 4

---
## 2026.10.26-05:00:00 Alpha 3: property-aware member access, poll-based page repairs

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Host\PclUi.cs                    +60 -25
.\src\PclDlSwap.Payload\Features\AboutBranding.cs        +170 -150
.\src\PclDlSwap.Payload\Features\DownloadPageRestore.cs  +130 -75
.\src\PclDlSwap.Payload\Host\OffsetManifest.cs           +48 -0
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs            +23 -4
.\src\PclDlSwap.Payload\Host\SwapLog.cs                  +2 -2
.\src\Pcl2Swap.Shim\Shim.cpp                             +1 -1
.\src\PclDlSwap.ExLoaderHarness\Program.cs               +170 -70
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|PclUi|Member reads now fall back to properties. PCL exposes PanLoad/PanAllBack/LoadMinecraft/PanMain/ItemAboutPcl as VB.NET properties, and the previous field-only lookup returned null - the direct cause of the PageLoaderInit NullReferenceException and the silently skipped About row|2026.10.26-05:00:00|PCL2.dl.swap
2|DownloadPageRestore|LoaderInit now feeds live page controls to PageLoaderInit (the NRE path is gone), and the 2.99.0.1 sidebar repair became a poller over ModMain.FrmDownloadLeft instead of a constructor detour whose prologue cannot be relocated|2026.10.26-05:00:00|PCL2.dl.swap
3|DownloadPageRestore|Install-time gate probe: LoaderInit is only replaced when its IL carries the "关闭下载 / 不再提供" stub markers, so stock builds (2.12.8.1 and friends) stay on their own download path|2026.10.26-05:00:00|PCL2.dl.swap
4|AboutBranding|Rebuilt as a poller over ModMain.FrmOtherAbout with UI-thread insertion; the constructor detour is gone and the row is inserted a few hundred milliseconds after the page first appears|2026.10.26-05:00:00|PCL2.dl.swap
5|OffsetManifest|ResolveFor(host) matches the assembly that actually carries PCL instead of EntryAssembly (the injector can wrap the launcher), and the payload now logs the host path/version/MVID on every activation|2026.10.26-05:00:00|PCL2.dl.swap
6|SwapLog/Shim|Version bumped to v26.0-Alpha 3, extraction directory v26.0-Alpha3|2026.10.26-05:00:00|PCL2.dl.swap

version: v26.0-Alpha 3

---
## 2026.10.25-14:10:00 Restore the vanilla download entry hidden by 2.99.0.1

GitCommitHash: 438ecbe (base)

ChangedFiles:
```
.\src\PclDlSwap.Patcher\IlPatcher.cs             +135 -0
.\data\offset-manifest.json                      +53 -29
.\tools\AddTarget\Program.cs                     +1 -0
.\tools\VerifyOffsets\Program.cs                 +15 -6
.\docs\REA-analysis.md                           +24 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|src/PclDlSwap.Patcher/IlPatcher.cs|New rewrite: remove the 2.99.0.1-only PageDownloadLeft constructor statements that collapse the "原版游戏" entry, switch the default sub-page to DownloadMod and check the Mod entry|2026.10.25-14:10:00|PCL2.dl.swap
2|data/offset-manifest.json|Every retained build now tracks PCL.PageDownloadLeft..ctor (token + RVA), derived from the samples by tools/AddTarget|2026.10.25-14:10:00|PCL2.dl.swap
3|tools/VerifyOffsets|Constructor manifest keys (Type..ctor) split correctly, and the RVA comparison now reads the real `rva` field instead of the never-present `bodyFileOffset`|2026.10.25-14:10:00|PCL2.dl.swap
4|docs/REA-analysis.md|Recorded the 2.99.0.1 sidebar gate, the verified four rewrites and the runtime evidence|2026.10.25-14:10:00|PCL2.dl.swap

version: v26.0-Alpha 2 (in development)

---

## 2026.10.25-01:50:00 Support PCL 2.99.0.1 (offset-only adaptation)

GitCommitHash: 60d6819 (base)

ChangedFiles:
```
.\data\offset-manifest.json                     +60 -75
.\data\methods-v2990-1-rel415.tsv           +6343 -0
.\data\methods-v2128-snap401.tsv                -0 -6341
.\docs\REA-analysis.md                           +40 -12
.\tools\AddTarget\**                              +130 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|data/offset-manifest.json|Added the 2.99.0.1 (VersionCode 415) target: sha256/MVID plus six method tokens and RVAs|2026.10.25-01:50:00|PCL2.dl.swap
2|data/offset-manifest.json|Retired the oldest entry (2.12.8.1 build 401) to honour the five-version retention policy|2026.10.25-01:50:00|PCL2.dl.swap
3|tools/AddTarget|New helper that derives sha256/MVID/tokens/RVAs from a sample so a new official build is a one-command adaption|2026.10.25-01:50:00|PCL2.dl.swap
4|docs/REA-analysis.md|Recorded the 2.99.0.1 assessment: three tracked methods byte-identical, tokens unchanged, RVAs shifted 42-244 bytes|2026.10.25-01:50:00|PCL2.dl.swap

version: v26.0-Alpha 1 (asset-only follow-up)

---

## 2026.10.25-00:30:00 Repository rebuild and first Pre-Release

GitCommitHash: 9c66ae7

ChangedFiles:
```
.\README.md                                  +120 -0
.\docs\REA-analysis.md                       +260 -0
.\docs\REA-analysis.zh.md                    +261 -0
.\docs\architecture.md                        +60 -0
.\docs\versioning.md                          +60 -0
.\data\offset-manifest.json                   +140 -0
.\src\PclDlSwap.Patcher\IlPatcher.cs         +330 -0
.\src\PclDlSwap.Payload\**                   +900 -0
.\src\PclDlSwap.Loader\Program.cs            +150 -0
.\scripts\build.ps1                          +100 -0
.\tools\VerifyOffsets\Program.cs             +120 -0
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|src/PclDlSwap.Patcher|Disk IL patch engine: restore the download loader, gate silent OTA behind an explicit prompt, deny auto-update permission|2026.10.25-00:30:00|PCL2.dl.swap
2|src/PclDlSwap.Payload|Sandbox staging, multi-engine扫描 gate, landing watchdog, Mark-of-the-Web, logging|2026.10.25-00:30:00|PCL2.dl.swap
3|src/PclDlSwap.Loader|Patch a copy of the target, launch it, optional -watch supervision|2026.10.25-00:30:00|PCL2.dl.swap
4|data/offset-manifest.json|Six methods across four official builds with sha256/MVID/token/RVA, verified PASS|2026.10.25-00:30:00|PCL2.dl.swap
5|docs/REA-analysis.*|Full static analysis, the 2.99.0 gate location, OTA chain, measured patch routes|2026.10.25-00:30:00|PCL2.dl.swap
6|scripts/build.ps1|One-command build and dist assembly with hash listing|2026.10.25-00:30:00|PCL2.dl.swap

version: v26.0-Alpha 1

---

## 2026.10.24-23:00:00 Initial implementation

GitCommitHash: (pre-rebuild history, not retained)

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|(research)|Reverse engineered four official builds; located the single 2.99.0 download gate|2026.10.24-21:00:00|PCL2.dl.swap
2|(research)|Measured and rejected four runtime patch routes; adopted disk IL patching|2026.10.24-23:00:00|PCL2.dl.swap

version: v26.0-Alpha 1 (development)

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->

