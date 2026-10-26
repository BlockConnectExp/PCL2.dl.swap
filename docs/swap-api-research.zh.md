# PCL2 Swap API / Addon 平台 · 阶段 0 研究

> 2026-10-27 · 基于 PCL 2.99.0.0 的 REA（反编译级证据）+ 架构设计草案。
> 目标：把 Swap 从"一堆硬编码功能"演进为"字节码钩子 + 可读化 API + Addon 平台"，
> 并在 设置 侧栏加入 **Swap Addon** 与 **Swap Settings** 两个页面。
> 默认 Addon：**Download MC**、**OTA Protector**、**Cancel Button**。

---

## 0. 范围与不变量

- 目标宿主：官方 PCL 2.99.0.0（MVID `926d447e-daff-4169-8c64-adc3d37d5f78`）；2.99.0.1 为
  同源构建（`5613aa62-…`），偏移清单已覆盖。
- 两种交付形状都要（Owner 要求）：
  - **注入形状**：ExLoader shim（已生产可用的 v2 托管 detour 引擎）。
  - **Patch 形状**：磁盘 IL 改写 + Loader（现有静态 4 项改写）。
- 不变量：**不修改官方原版 exe**（Patch 只写副本）；**不新增 AssemblyRef**（Patch 形状的硬约束）；
  注入形状的 detour 只跳**托管方法**（GC 模式安全，见 `docs/av-root-cause.md`）。

---

## 1. 现状盘点（两个形状各自能做什么）

### 1.1 注入形状（ExLoader shim，`dist/exloader/`）

- `Pcl2Swap.Shim.x64.dll`（native x64）：解包 payload + 预载 capstone → 在 PCL 进程内用
  `ICLRRuntimeHost::ExecuteInDefaultAppDomain` 调 `PclDlSwap.Payload.Host.ExLoaderEntry.Activate`。
- `PclDlSwap.Payload`：v2 detour 引擎（`X64Detour` + `OriginalFactory` IL 副本），已挂：
  `ModSecret.UpdateStart`、`ModNet.NetDownloadByLoader` ×2、`NetDownloadByClient`、
  `PageDownloadInstall.LoaderInit`、`ModMain.MyMsgBox`、`PageOtherLeft.TryFeedback`。
- 现有 UI 能力（可复用为 API 基础）：UI 线程 marshal（`PclUi.OnUiThread`）、成员读写
  （字段/属性双查）、原生 MyMsgBox/Hint 调用、轮询式页面修复。

### 1.2 Patch 形状（Loader，`dist/`）

- `PclDlSwap.Loader.exe` → `IlPatcher` 把**副本**改写为 `PclDlSwap.Patched.exe` 后启动：
  1. `PageDownloadInstall.LoaderInit` 恢复真实 `DlClientListLoader`；
  2. 2.99.0.1 侧栏折叠语句删除；
  3. `ModSecret.UpdateStart` 前缀插入显式确认（保留原 IL 在后）；
  4. `HasUpdatePermission` 权限门。
- **重要事实（已核实）**：Patch 形状**没有运行时组件**——`PCL2SWAP_HOME` 无任何读取方；
  `COR_PROFILER`/`COR_ENABLE_PROFILING` 从未设置；`PclDlSwap.Profiler.dll` 是 x86 而 PCL 是 x64。
  也就是说：云下载查毒、关于页品牌行、反馈分流、悲报取消按钮这些**运行时功能在 Patch 形状中不存在**。
- **结论**：Patch 形状要获得运行时能力，**不需要 CLR profiler**——只需一个"入口"把 payload
  加载起来（见 §6.2，静态 IL 插入 bootstrap 调用）。

---

## 2. REA 证据（PCL 2.99.0，反编译 + IL 核对）

### 2.1 设置页左栏 `PCL.PageSetupLeft`（`MyPageLeft` 子类）

字段（dnlib 实测）：
```
bool IsLoad; bool IsPageSwitched; FormMain/PageSubType PageID;
StackPanel _PanItem (属性 PanItem);
MyListItem _ItemLaunch/_ItemLink/_ItemUI/_ItemSystem (属性 ItemLaunch/…);
```

- `PageCheck(MyListItem sender, EventArgs e)`：`Tag != null` 时
  `PageChange((PageSubType)Math.Round(Val(sender.Tag)))`。
- `PageChange(PageSubType ID)`：`if (PageID == ID) return;` →
  `AniControlEnabled++; IsPageSwitched = true;` → **switch 分发**（4 个 case，直接 new 页面 +
  `PageChangeRun(...)`）→ `PageID = ID`；**default 抛异常**（被 catch 写日志）。
- `PageGet(PageSubType ID = -1)`：同样的 4 个 case（Default→FrmSetupLaunch、
  DownloadMod→FrmSetupUI、SetupLink→FrmSetupLink、DownloadPack→FrmSetupSystem）；
  **default 抛异常**。
- `PageChangeRun(MyPageRight Target)`（private static）：`AniStop("FrmMain PageChangeRight")`
  → 清 Child → `FrmMain.PageRight = Target` → 旧页 `PageOnExit()` → 130ms 后
  `PageOnForceExit()` + 挂载 + 淡入 → 新页 `PageOnEnter()`。

**推论**：新增子页只需 **detour `PageChange`**（我们的 ID 走自建流程、其余转发原方法）
并在 `PanItem` 里插入两个 `MyListItem`（`Tag` 用未占用枚举值，如 `(PageSubType)100/101`）。
由于 `PageChange`/`PageGet` 的 switch 会抛异常，**不能只写 PageID 字段**（2.99.0.1 侧栏
崩溃事件即此类教训，见 CHANGELOG Alpha 2）。

### 2.2 页面基类 `PCL.MyPageRight`（`AdornerDecorator` 子类，可实例化）

- **构造函数只做 3 件事**：`PageUuid = GetUuid(); _PageState = Empty; DisabledPageAnimControls = new List();`
  ——**没有创建任何 UI 框架** → `new MyPageRight()` 就是一个空载体。
- `PageOnEnter()`（Empty 分支）：`PageLoader == null` → `PageState = ContentEnter` →
  `TriggerEnterAnimation(PanAlways, PanContent ?? Child)`；`PageOnExit`/`PageOnForceExit`
  同样以 `PanContent ?? Child` 为动画目标，`PageOnForceExit` 在 `PageLoader == null` 时只
  `Child.Visibility = Collapsed`。
- **推论**：`var page = new MyPageRight(); page.Child = ourRootControl;` 即可作为完整子页，
  由 PCL 的页面机制接管动画/生命周期。**不需要 Reflection.Emit**。

### 2.3 `MyListItem`（左栏项）

- WPF 控件（FrameworkElement），有 `Title/Info/Logo/Tag/Checked/Type(CheckType)/Uuid` 等。
- `Checked` setter → `SetChecked(value, user=false, anime=…)`；`value=true` 时触发
  `Check` 事件（`PageCheck` 即挂在这里）；单选互斥由 `Type == CheckType.RadioBox` 分支处理。
- 现有代码已证明可 `Activator.CreateInstance` + 反射设 `Title/Info/Logo`（AboutBranding 行）。

### 2.4 弹窗 / 提示 API 面（Cancel Button 的 hook 目标）

`PCL.ModMain` 实测方法：
```
MyMsgBox(string caption, string title, string button1, string button2, string button3,
         bool isWarn, bool highLight, bool forceWait, Action b1, Action b2, Action b3) : int
MyMsgBoxInput(string, string, string, Collection<Validate>, string, string, string, bool) : string
MyMsgBoxSelect(IEnumerable<IMyRadio>, string, string, string, bool) : int?
Hint(string, HintType, bool)
MyMsgBoxTick() / HintTick() / HideAllHint()
```
- 现有 `AnnouncementGuard` 已 hook `MyMsgBox`；Cancel Button 需要覆盖 **三个 MsgBox 家族**。
- "给任何本来没有取消按钮的弹窗加取消"的判据（提案）：`button2 == ""` 且
  (`forceWait == true` 或 `isWarn == true`)，或由 Addon 的规则表决定（见 §5.3）。

### 2.5 枚举与页面注册表（节选）

```
FormMain.PageType: Launch=0 Download=1 Link=2 Setup=3 Other=4 InstanceSelect=5 TaskManager=6
                   InstanceSetup=7 ResourceDetail=8 HelpDetail=9
FormMain.PageSubType（跨栏目复用）:
  Download:  Default=0 DownloadMod=2 DownloadPack=3 DownloadDataPack=4 DownloadResourcePack=5 DownloadShader=6
  Setup:     SetupLaunch=0 SetupLink=1 SetupUI=2 SetupSystem=3
  Other:     OtherHelp=0 OtherAbout=1 OtherTest=2
  Instance:  InstanceOverall=0 InstanceSetup=1 InstanceModify=2 InstanceMod=3 InstanceModDisabled=4 InstanceExport=5
```
- 未占用的值（如 100/101）**在运行时是合法的**（枚举可持有未定义值），但会命中
  `PageChange`/`PageGet` 的 `default:` 分支 → 必须由我们的 detour 先行接管。

---

## 3. Swap API 设计（提案）

### 3.1 分层

```
┌────────────────────────────── SwapHost (payload 内) ─────────────────────────────┐
│ AddonManager │ HookBus │ ApiProvider │ PageRegistry │ SettingsStore │ SwapLog    │
└───────────────▲───────────────────────────────▲───────────────────────────────┘
                │ 运行时实现（同一 AppDomain） │
        ┌───────┴────────┐              ┌───────┴────────┐
        │ 注入形状入口    │              │ Patch 形状入口  │
        │ shim → Activate│              │ 静态 IL bootstrap│
        └────────────────┘              └────────────────┘
                ▲                               ▲
                └────────── PCL2Swap.Api.dll（SDK，Addon 编译期引用）──────────┘
                                   ▲
                           第三方 Addon (.swapaddon)
```

### 3.2 可读化 API（"暴露所有可能的 API"的两层做法）

1. **强类型门面（SDK）**：`PCL2Swap.Api.dll`（net48）定义：
   - `ISwapAddon`（生命周期）、`SwapContext`（宿主上下文）、`ISwapApi`（门面接口）。
   - 命名空间规划：
     - `PCL2Swap.Api.Host`：版本、进程、形状（Injected/Patched）、路径（`%TEMP%\PCL2Swap\…`）。
     - `PCL2Swap.Api.Pcl.Secret` / `.Net` / `.Download` / `.Loader` / `.Profile` / `.Ui`：
       对宿主子系统的强类型包装（逐个从 REA 清单吸收）。
     - `PCL2Swap.Api.Events`：事件总线（见 §3.3）。
     - `PCL2Swap.Api.Hooks`：`Hook.Method(target, prefix/postfix/replace)` 等（内部落到 detour 引擎）。
     - `PCL2Swap.Api.Ui`：`RegisterSettingsPage` / `Hint` / `MsgBox` / `MyListItem` 工厂。
     - `PCL2Swap.Api.Data`：Addon 私有配置（`%TEMP%\PCL2Swap\addons\<id>\config.json`）、日志。
   - **版本化**：`ISwapApi.ApiVersion`（SemVer）；Addon manifest 声明 `apiVersion` 范围。
2. **反射桥（逃生通道）**：`ISwapApi.Reflection`：
   `HostType("PCL.ModSecret")`、`GetMember`、`SetMember`、`Call`、`HookMethod`——
   保证**任何**宿主成员都可达（强类型门面没覆盖的也不阻塞 Addon）。

### 3.3 Hook 总线（事件 + 中间件）

- 统一入口：`SwapApi.Hooks.Attach(target, handler, HookMode {Prefix, Postfix, Replace})`。
- 内部映射：
  - 注入形状 → v2 detour（托管方法体跳转 + IL 副本调用 original）。
  - Patch 形状 → 先由静态 bootstrap 把 SwapHost 载入，之后**同样走 v2 detour**
    （不需要 profiler；见 §6.2）。
- 面向 Addon 的**语义事件**（在 detour 之上再包一层）：
  `Download.Before/After/Blocked`、`Update.Started/SilentBlocked`、`Dialog.Opening`、
  `Page.Changing`、`App.Ready`。

### 3.4 两个设置页的实现路径（基于 §2 的 REA）

`SwapHost` 在 PCL 主窗体就绪后：
1. 找 `ModMain.FrmSetupLeft`（不存在则等出现；或 `FrmMain.PageChange(PageType.Setup)` 触发）；
2. 取 `PanItem`（StackPanel）→ 插入两项 `MyListItem`：
   `Title="Swap Addon"` / `"Swap Settings"`、`Type=RadioBox`、`Tag="100"/"101"`，
   并订阅 `Check` → `PageChange((PageSubType)100/101)`（或反射复用官方 `PageCheck`）；
3. detour `PageSetupLeft.PageChange`：
   - `id==100` → `PageChangeRun(new MyPageRight { Child = SwapAddonView })`、`PageID=100`；
   - `id==101` → 同上（SwapSettingsView）；
   - 其余 → 调 original（IL 副本）。
   （同时保留 `AniControlEnabled++/--`、`IsPageSwitched=true` 的语义，字段可反射读写。）
4. `SwapAddonView`：Addon 列表（名称/版本/开关/卸载/导入 .swapaddon）、运行状态；
   `SwapSettingsView`：全局配置（扫描引擎、沙箱开关、日志级别、API 版本、宿主形状）。
   UI 用代码构建的 WPF 控件（沿用 PCL 的资源键做配色，保证与宿主风格一致）。

---

## 4. Addon 包格式（提案）

### 4.1 包结构（zip，扩展名 `.swapaddon`）

```
my-addon.swapaddon
├─ manifest.json          # 必需
├─ MyAddon.dll            # 必需（net48，引用 PCL2Swap.Api.dll）
├─ icon.png               # 可选（Addon 列表图标）
└─ assets/…               # 可选（本地化、模板等，Addon 自行读取）
```

### 4.2 manifest.json（schema 草案 v1）

```json
{
  "schema": 1,
  "id": "com.example.my-addon",
  "name": "My Addon",
  "version": "1.0.0",
  "apiVersion": ">=1.0 <2.0",
  "entry": "MyAddon.MainAddon",
  "author": "Example",
  "description": "One line summary.",
  "targets": { "pcl": "2.99.0", "shapes": ["injected", "patched"] },
  "permissions": ["host.read", "ui.pages", "hooks.download", "hooks.dialogs"],
  "defaultEnabled": false
}
```

### 4.3 生命周期与权限

```csharp
public interface ISwapAddon {
    void OnLoad(SwapContext ctx);   // 注册页面/事件/配置 schema
    void OnEnable();
    void OnDisable();
    void OnUnload();
}
```
- 加载：解包到 `%TEMP%\PCL2Swap\addons\<id>\<version>\` → `Assembly.LoadFrom` → 反射 entry。
- 隔离：.NET Framework 4.8 无 ALC，同 AppDomain 加载；**权限为声明式**（用于 UI 展示与
  hook 审计，不作为硬边界）——文档中明确"Addon 与宿主同权限"。
- 失败策略：`OnLoad` 异常 → 标记 failed、禁用该 Addon、不影响宿主与其它 Addon。

### 4.4 版本与兼容

- `apiVersion` 用区间表达（`>=1.0 <2.0`）；宿主启动时校验，不满足则禁用并提示。
- 宿主 API 只做**加法演进**；破坏性变更升 major 并保留旧接口 shim。

---

## 5. 默认三 Addon（把现有功能迁移进来）

### 5.1 Download MC（原版游戏下载恢复）

- hook 点：`PageDownloadInstall.LoaderInit`（恢复真实 `DlClientListLoader`）；2.99.0.1 侧栏修复
  作为该 Addon 的页面级补救（现有 `DownloadPageRestore` 逻辑直接搬入）。
- 状态：已在 Alpha 2 验证（含 2.99.0.1 侧栏崩溃修复）。

### 5.2 OTA Protector（OTA 显式 / 沙箱 / 测毒）

- hook 点：`ModSecret.UpdateStart`（静默→显式确认）、`ModNet.NetDownloadByLoader`/
  `NetDownloadByClient`（启动器文件的事后查毒：扩展名 + 非游戏目录，命中则删除并中止）。
- 现有实现即 `AnnouncementGuard`（部分）+ `CoreInstaller` 的下载门；迁移为 Addon 后由
  `SwapApi.Hooks` 注册同样的 handler。

### 5.3 Cancel Button（给没有取消按钮的弹窗补取消）

- hook 点：`ModMain.MyMsgBox` / `MyMsgBoxInput` / `MyMsgBoxSelect`。
- 规则（提案，待确认）：`button2` 为空且 (`forceWait==true` 或 `isWarn==true`) →
  追加"取消"按钮（返回取消语义），并记录到日志；`isWarn` 弹窗若来自云公告，走原生取消路径
  （现有 `AnnouncementGuard` 的判定逻辑迁入本 Addon）。

---

## 6. 两种形状的统一

### 6.1 注入形状

shim（已交付）→ `ExLoaderEntry.Activate` → `SwapHost.Start(Injected)` → 装 detour、建页面、
加载 Addon。改动集中在 payload 内部重构（不改变 shim 协议）。

### 6.2 Patch 形状（弃用 profiler 方案，改静态 bootstrap）

- 结论：**不需要 CLR profiler**。IlPatcher 在改写时向宿主**早期的静态构造**（候选：
  `PCL.ModMain..cctor`）插入一段**自带 try/catch 的 bootstrap 调用**：
  反射 `Assembly.LoadFrom(%PCL2SWAP_HOME%\PclDlSwap.Payload.dll)` →
  `HookEntryPoints.Bootstrap("disk-patch")` → SwapHost 起、随后一切与注入形状相同。
- IL 约束：只用已引用程序集（`System.Reflection`/`System.IO`/`Environment` 均在既有引用内），
  **不新增 AssemblyRef**；失败静默（吞异常），保证原版行为不受影响。
- 收益：Patch 形状一次性获得与注入形状**相同的运行时能力**（三个默认 Addon + Swap 页面），
  并且不再依赖 x86 profiler。

### 6.3 待验证项（实现前必须做的实验）

1. `ModMain..cctor` 的注入时机是否早于 UI 初始化（若不合适，退到 `FormMain..ctor` 前段）。
2. `PageSetupLeft.PageChange` detour 在"页面尚未创建"时的调用路径（与 2.99.0.1 侧栏修复同样
   的早期调用风险），需要与 `PageChangeRun` 的空引用防护一并处理。
3. `MyListItem.Type = RadioBox` 的反射赋值在 2.99.0.0 与 2.99.0.1 上的一致性。

---

## 7. 路线图与风险

| 阶段 | 交付 | 关键风险 |
|---|---|---|
| P0 | `PCL2Swap.Api.dll` 骨架 + SwapHost 重构 + 两个设置页（注入形状） | 页面 detour 与宿主早期状态竞争 |
| P1 | 三默认 Addon 迁移到 Addon 形态（功能等价回归） | 弹窗规则误伤（Cancel Button 判据） |
| P2 | Addon 打包/加载/管理 UI + Patch 形状 bootstrap 接线 | 静态 IL 插入的兼容性（跨构建） |
| P3 | API 文档、示例 Addon、权限/审计细化 | API 面过早冻结 |

## 8. 待 Owner 决策

1. **Cancel Button 判据**：`forceWait/isWarn` 组合是否足够？还是维护一张"云公告特征表"？
2. **Addon 权限模型**：保持"声明式 + 同权限"（简单、实用），还是要做更强隔离？（后者成本高）
3. **API 面规模**：P0 只暴露现有三功能所需（下载/更新/弹窗/页面），还是同时铺开
   `ModProfile/ModLaunch` 等更大面？（"暴露所有可能的 API"的落地粒度）
4. **Patch 形状接线**：确认按 §6.2 的"静态 bootstrap + 运行时 detour"路线（放弃 profiler）。
5. **Swap Settings 中的扫描引擎**：Defender 之外是否默认启用其它引擎（目前全部可选项为空）。

## 附录 A：本次 REA 的核对记录

- 样本：`C:\Users\Sails\Downloads\正式版 2.99.0\Plain Craft Launcher 2.exe`
  （sha256 `6b6e1260…`，MVID `926d447e-…`）。
- 核对方式：dnlib 读取方法签名/字段/枚举 + ILSpy 反编译源码（`_scratch/pcl2-dlswap/source-v299`）
  + 逐条 IL 指令核对（`NetDownloadByLoader` 的 `LoaderDownload`/`WaitForExit` 调用形状）。
- 与实现互证：Alpha 2 注入形状实测日志（侧栏恢复、关于页、反馈分流、下载门）与本文各节一致。

## 附录 B：术语

- **SwapHost**：payload 内的运行时宿主（API、Hook 总线、Addon 管理、页面注册）。
- **形状（shape）**：交付形态——注入（ExLoader shim）与修补（Loader + 静态 IL）。
- **可读化**：把宿主内部成员包装成稳定命名的强类型 API（+反射桥保底）。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
