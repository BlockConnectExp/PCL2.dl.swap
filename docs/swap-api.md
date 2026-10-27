# PCL2 Swap Addon 指南（API 1.0，Alpha 3 起）

> 面向 Addon 作者。平台设计背景见 `docs/swap-api-research.zh.md`。
> 交付形状（ExLoader 注入 / 磁盘修补）对 Addon **透明**：目标都是同一套 SwapHost 运行时。

---

## 1. 快速开始

Addon 是一个 **`.swapaddon`** 包（zip 容器）：

```
my-addon.swapaddon
├─ manifest.json            必需
├─ MyAddon.dll              必需（net48，引用 PCL2Swap.Api.dll）
├─ icon.png                 可选
└─ assets/…                 可选
```

把包放进 **`%APPDATA%\PCL2Swap\addons\`**（持久目录；可用环境变量 `PCL2SWAP_DATA`
整体改到别处，测试/多实例时很有用）并重启 PCL，它就会出现在 设置 → **Swap Addon** 页面。

### 用打包器生成 `.swapaddon`

```powershell
.\tools\PclAddonPack\bin\Release\net48\PclAddonPack.exe `
    --project .\src\YourAddon\bin\Release\net48 `
    --id com.example.my-addon --name "My Addon" --version 1.0.0 `
    --entry MyAddon.MainAddon --description "One line summary." `
    --out .\dist\my-addon.swapaddon
```

打包器会用 dnlib **静态校验** `--entry` 类型确实实现了 `PCL2Swap.Api.ISwapAddon`
（不会执行你的代码），自动跳过 `PCL2Swap.Api.dll`（SDK 由宿主提供，随包分发会造成类型标识冲突），
并打印包内条目与 SHA-256。参考实现见 `src/PCL2Swap.SampleAddon`：它订阅事件总线、
挂 Prefix 钩子、注册带实时计数的设置页、用 `Reflect` 调用宿主方法并持久化数据——
一个模块能用到的东西它都用了一遍。

### manifest.json

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
  "shapes": ["injected", "patched"],
  "permissions": ["ui.pages", "hooks.dialogs"],
  "defaultEnabled": false
}
```

- `entry` 是类型全名；加载器会在包内所有 dll 中寻找实现 `ISwapAddon` 的那个类型。
- `apiVersion` 支持 `*`、`=x.y`、`>=x.y`、`<x.y`（空格分隔可组合）。
- `permissions` 目前是**声明式**（用于展示与审计），运行时不做硬隔离：Addon 与宿主同权限。

---

## 2. 编写 Addon

```csharp
using PCL2Swap.Api;

public sealed class MainAddon : ISwapAddon
{
    private SwapContext _context;

    public void OnLoad(SwapContext context)
    {
        _context = context;
        context.Api.Log("hello from my addon");

        // 自定义设置页（出现在 设置 侧栏的下方）
        context.Api.Ui.RegisterSettingsPage("com.example.my-addon.page", "My Addon",
            () => new System.Windows.Controls.TextBlock { Text = "Hello!" });

        // 读取宿主成员（可读化反射桥）
        var modMain = context.Api.Reflect.HostType("PCL.ModMain");
        context.Api.Log("ModMain resolved: " + (modMain != null));

        // 私有配置（持久化到 addons/<id>/config.json）
        var count = context.Api.Data.Get("runCount", "0");
        context.Api.Data.Set("runCount", (int.Parse(count) + 1).ToString());
        context.Api.Data.Save();
    }

    public void OnEnable() { }
    public void OnDisable() { }
    public void OnUnload() { }
}
```

### 可用的 API 面（1.0）

| 区域 | 成员 | 说明 |
|---|---|---|
| `Host` | `SwapVersion` / `ApiVersion` / `Shape` / `ProcessId` / `LogDirectory` / `AddonDirectory` / `PclAssembly` | 宿主事实 |
| `Hooks` | `Attach(类型, 方法, id, 静态委托, HookMode)` / `IsHooked` + 三个语义事件 | 基于 v2 托管 detour；**1.0 仅支持静态 handler** |
| `Ui` | `RegisterSettingsPage` / `Hint` / `MsgBox` | 页面自动加入 设置 侧栏 |
| `Data` | `Get/Set/Save` | 每 Addon 独立存储 |
| `Reflect` | `HostType` / `GetMember` / `SetMember` / `Call` | 任意宿主成员逃生通道（`GetMember/SetMember` 兼容字段与属性） |
| 日志 | `Log/LogWarning/LogError` | 写入 `%TEMP%\PCL2Swap\<版本>\pcl2swap.log` |

### Hook 语义事件（由内置 Addon 触发）

`DownloadCompleted`、`DialogOpening`、`UpdateRequested`——
它们把"下载完成 / 弹窗即将出现 / 更新请求到达"翻译成稳定事件，避免每个 Addon 自己挂钩底层方法。

---

## 3. 打包

```powershell
# 1) 编译（net48，引用 PCL2Swap.Api.dll）
dotnet build MyAddon.csproj -c Release

# 2) 打包
Compress-Archive -Path .\bin\Release\net48\MyAddon.dll, .\manifest.json -DestinationPath MyAddon.swapaddon

# 3) 安装
Copy-Item MyAddon.swapaddon "$env:TEMP\PCL2Swap\addons\"
```

一个可直接运行的参考实现：`src/PCL2Swap.SampleAddon`（同时被 harness 的 `--addontest` 用作端到端验证）。

---

## 4. 当前限制（1.0）

1. **Hook 仅支持静态 handler 且不可热卸载**——实例委托包装与 detour 卸载在路线图上；
   因此 `Attach` 返回的 `IDisposable` 现在只表达意图，不会真正还原目标方法。
2. **同进程、同权限**：Addon 与宿主共享 CLR 与地址空间；请把 Addon 当作受信任代码分发。
3. **启用/停用开关是展示性的**：Swap Addon 页如实列出状态与错误，但停用不会回滚已安装的 hook。
4. 每个 Addon 的 dll 从自己的解包目录加载，静态状态彼此隔离（这一点是特性而不是缺陷）。

---

## 5. 内置 Addon

| Addon | 行为 | 宿主实现位置 |
|---|---|---|
| **Download MC** | 恢复原版游戏下载页（LoaderInit 重驱动 + 2.99.0.1 侧栏修复） | `DownloadPageRestore` |
| **OTA Protector** | 静默 OTA 显式确认；启动器文件下载后查毒（游戏内容不查） | `CoreInstaller` + `AnnouncementGuard` |
| **Cancel Button** | 给没有取消按钮的弹窗追加取消 | `AnnouncementGuard`（规划迁移） |

内置 Addon 的功能实现仍位于宿主核心（Alpha 3），P1 起逐个迁移进 Addon 类本身。

---

## 6. 全接口清单（surface dump）

`tools/PclSurface` 把任意 PCL 构建的**全部可调用成员**导出为 JSON（默认排除编译器生成类型，
加 `--include-generated` 可包含）：

```powershell
.\tools\PclSurface\bin\Release\net48\PclSurface.exe `
    "D:\PCL\Plain Craft Launcher 2.exe" "data\pcl-surface-2.99.0.json"
```

仓库已附 2.99.0 与 2.99.0.1 两份产物。每个类型包含方法/属性/字段的完整元数据：
名称、静态性、可见性（含 internal/private）、参数与返回类型、metadata token。

Addon 的用法：

1. 在清单里按名称/签名定位目标成员（含 PCL 未在 UI 暴露的内部能力）；
2. 通过 `ISwapApi.Reflect`（`HostType` / `GetMember` / `SetMember` / `Call`）直接调用——
   强类型门面没有覆盖的成员同样可以到达，这就是"全部接口可达"的机制保证。

---

## 7. 模块状态与启停（Alpha 4 repack 4 起）

### 6.1 原生外观页面（`Api.Ui` 构建器，Alpha 6 起）

模块页面不必从零拼 WPF：`IUi` 提供了一组构建器，内部就是 PCL 自己的 `MyCard` / `MyButton`
与主题画笔（宿主缺少这些控件时自动退回普通 WPF，页面不会渲染失败）——

```csharp
var page = new StackPanel { Margin = new Thickness(25, 25, 25, 10) };   // 与官方设置页同一边距
page.Children.Add(Api.Ui.Card("我的模块",
    Api.Ui.Text($"API {Api.Host.ApiVersion}", 12, secondary: true),
    Api.Ui.InfoRow("状态", "已启用"),
    Api.Ui.Note("说明文字。"),
    Api.Ui.Buttons(Api.Ui.Button("执行一次", DoSomething))));
Api.Ui.RegisterSettingsPage("my.page", "我的模块", () => page);
```

`Card / Text / InfoRow / Note / Buttons / Button` 全部返回 `FrameworkElement`；
按钮的 `colorType` 取 `"Normal" / "Highlight" / "Red"`。
在真机里它们与内置的 Swap Addon / Swap Settings 页面外观一致（示例模块用的就是这套）。
注意：页面工厂在宿主 UI 线程（STA）调用，不要在自己的后台线程里构建控件。

### 6.2 模块设置项（Alpha 6 起）

两组构件 + 一个小助手，就能写出「开箱即用、自动持久化」的模块设置：

```csharp
page.Children.Add(Api.Ui.Card("我的模块设置",
    Api.Ui.CheckBox("记录钩子命中", AddonSettings.GetBool(Api.Data, "my.flag", true), value =>
    {
        AddonSettings.SetBool(Api.Data, "my.flag", value);   // 写入 + 落盘
        Api.Log("设置已保存：" + value);
    }),
    Api.Ui.Combo("刷新频率", new[] { "1 秒", "2 秒", "5 秒" },
        AddonSettings.GetInt(Api.Data, "my.refresh", 0),
        index => { AddonSettings.SetInt(Api.Data, "my.refresh", index); ApplyInterval(index); })));
```

- `CheckBox` 用宿主的 `PCL.MyCheckBox`（其 `Change` 事件会回读控件状态），`Combo` 用 `PCL.MyComboBox`
  + `MyComboBoxItem`；宿主缺少这些控件时自动退回标准 WPF，回调语义不变（下拉框回传索引）。
- `AddonSettings.GetBool/SetBool/GetInt/SetInt/GetDouble/SetDouble/GetString/SetString`
  是针对 `Api.Data` 的类型化读写，每次写入都会 `Save()` 到模块自己的 `config.json`。
- 真机验证（2.99.0）：勾选框与下拉框都能改、立即生效，`config.json` 同时出现
  `demo.countHook=false`、`demo.refreshIndex=1`，日志打印「示例模块设置：刷新频率 = 2 秒」。

- 开关状态写在 **`%APPDATA%\PCL2Swap\modules.json`**：`{"<addon id>":{"enabled":true}}`，
  重启后保持；未知 id 视为启用（配置损坏不会误关功能）。
- 被禁用的模块在启动阶段**直接跳过**（不调用 `OnLoad`/`OnEnable`），
  平台自带功能的钩子也会按内置模块的开关放行/拦截。
- 设置 → Swap Addon 每行都有「启用 / 禁用」按钮：点击即时写入状态、重建页面，
  **无需重启**（钩子句柄保留但按开关放行）。
- Swap Addon 页面还会显示每个模块的**钩子数与命中次数**，以及一张「钩子链（模块作用域）」
  卡片：哪条链挂在哪个宿主方法上、链上有哪些模块、命中和跳过各多少次（点「刷新」重建页面取值）。
- `OnDisable`/`OnUnload` 会被调用；请在 `OnEnable` 里订阅事件、在 `OnDisable` 里退订。
- **生效范围（重要）**：钩子类行为（OTA 拦截、弹窗取消按钮、下载查毒、事件订阅）**立即**随开关生效；
  而**结构类修复**（原版游戏页重驱、下载侧栏恢复）是启动时对宿主对象的一次性修改——关闭模块后
  当前进程里的页面/侧栏保持已修复状态，**重启 PCL 才会完全回退**（与 Xposed/LSPosed 的
  “重启生效”语义一致）。真机验证：`modules.json` 置 `{"swap.download-mc":{"enabled":false}}`
  后重启，下载页回到宿主原生的“不再提供 MC 下载功能”，两个看门线程直接退出。

## 8. 事件总线（IHooks 的三个语义事件）

平台自己的管线会把事件广播给所有已启用模块，每个订阅者单独 try/catch（一个模块抛错不影响其他模块与宿主）：

| 事件 | 触发点 | 参数 |
|---|---|---|
| `DialogOpening` | 宿主每次打开弹窗（`ModMain.MyMsgBox` 家族，经公告守卫钩子） | `Caption/Title/IsWarn/ForceWait/Button2/CancelAdded` |
| `UpdateRequested` | `ModSecret.UpdateStart` 被调用时（含静默 OTA） | `Silent/Urls` |
| `DownloadCompleted` | 平台的下载钩子把请求交给宿主下载器之后 | `Urls/LocalPath/IsPclCloud` |

```csharp
public void OnEnable()
{
    Api.Hooks.DialogOpening += (s, e) => Api.Log($"弹窗：{e.Title}（forceWait={e.ForceWait}）");
    Api.Hooks.UpdateRequested += (s, e) => Api.Log($"更新请求：silent={e.Silent}");
}
```

参考实现见 `src/PCL2Swap.SampleAddon`（同时被 `--addontest` 用作断言目标）。

## 9. 兼容性自检与本地真机调试

### 8.1 模块作用域钩子（`IHooks.Attach`）

`Api.Hooks.Attach(declaringType, methodName, hookId, handler, mode)` 注册的是**模块作用域**钩子：
平台为每个钩子生成一个 gate，行为跟随模块开关——

- 模块启用：调用你的 handler，其返回值就是目标方法的返回值；
- 模块关闭：gate 直通宿主原方法（等价于从未挂钩），**无需重启**；
- 释放返回的 `IDisposable`：该钩子立刻变为直通（不撕物理跳转，但行为已还原）。

handler 可以是实例委托（不再要求静态方法）；参数形状与宿主一致（引用类型可写 `object`，
值类型必须精确类型），返回值类型需与目标相同。harness `--addontest` 用
`PCL.HookTarget.Ping` 断言了「命中 → 禁用直通 → 再启用命中 → 释放直通」四态。

`mode` 决定 handler 与宿主原方法的关系（handler 不必自己调用原方法，平台会代劳）：

| mode | 行为 | handler 返回值 |
|---|---|---|
| `Replace`（默认） | 只调用 handler，宿主原方法**不执行** | 作为目标方法的返回值 |
| `Prefix` | 先调用 handler，再由平台调用宿主原方法并把**原返回值**返回给调用方 | 忽略（观察/记录/抛异常拦截） |
| `Postfix` | 先执行宿主原方法，再调用 handler，返回**原返回值** | 忽略（观察结果之后的副作用） |

想要「改结果」用 `Replace`；想要「前后观察不改变行为」用 `Prefix`/`Postfix`——
这也是最常见的两种模块写法。harness 断言了三种模式的调用顺序与返回值
（`before:7 → original:7`、`original:7 → after:7`），以及实例委托 + 引用类型参数。

**安全阀**：IL 体过小的方法（JIT 后比跳转补丁还短）会被拒绝安装并抛 `NotSupportedException`，
而不是破坏相邻代码 —— 这是实测踩到过的崩溃点（11 字节方法 vs 14 字节补丁）。

**多模块/多钩子 = 一条链**：同一个宿主方法只会被 patch 一次。第一个钩子建立「钩子链」并装上
唯一的 gate，之后任何模块/任何模式的钩子都**加入这条链**（引擎层也会拒绝二次 patch，
因为叠加两个跳转补丁会破坏执行——同样是实测结论）。每次调用按固定顺序执行：

1. 依次调用仍然生效的 `Prefix` handler；
2. 若有生效的 `Replace` handler，由**第一个**接管返回值，否则调用宿主原方法；
3. 依次调用仍然生效的 `Postfix` handler。

模块被禁用或句柄被释放时，该 handler 只是从链上移除；链上不再有任何 handler 时调用直通原方法。

## 9. 兼容性自检与本地真机调试

- `PCL2SWAP_SELFTEST=1`：启动 5 秒后在宿主 UI 线程跑一遍**只读**探针
  （Host / Surface / Reflect / Secret / Animation / Settings / Base / Instance /
  Download / Launch + 模块清单 + 事件总线），结果全部写入交换日志；
  2.99.0 实测 `ok=25 fail=0`。换 PCL 版本时先看这行就知道哪些接口漂移了。
- 不需要 ExLoader 就能跑真机注入：见 `docs/local-e2e.zh.md`
  （`tools/PclInject` + 测试副本改名 `PCL_SingletonMutex` 绕过单实例 + 隔离 TEMP/`PCL2SWAP_DATA`）。

## 10. 类型化访问族一览（IPclAccess）

| 族 | 覆盖 |
|---|---|
| `Secret` | 更新入口（`UpdateStart` / `HasUpdatePermission` / `UpdateCheckByButton`） |
| `Net` | `NetDownloadByLoader`（多源）/ `NetDownloadByClient`（单源） |
| `Animation` | `Animation` 门控、`AniStart` / `AniStop` |
| `Settings` | `Get` / `Set` / `HasSaved` / `Reset` / `GetDefault`（自动兼容泛型与非泛型重载） |
| `Base` | `GetUuid` 与 INI 读写/删除 |
| `Instance` | 实例目录与选中项、`GetMcFoolName`、按宿主缓存枚举实例、`McInstance` 成员、`ModLaunch.GetNativesFolder` |
| `Download` | 源顺序开关、`DlClientListGet`、Assets/Library/LauncherOrMeta/Mod 源、`DlModRequest` |
| `Launch` | `McLaunchStart`、`McLoginName`、`McLoginAble` |
| `Loader` | 任务栏进度（`TaskbarProgress` / `RefreshTaskbar` / `TaskbarAdd`）、LoaderBase 的 `State` / `Progress` / `IsRunning` / `Start` / `Cancel` / `Failed` |

未覆盖的成员仍可通过 `ISwapApi.Surface`（全量清单）+ `ISwapApi.Reflect` 直达。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
