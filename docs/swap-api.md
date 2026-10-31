# PCL2 Swap Addon 指南（API 1.10，v26.3 起）

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
  "hostVersions": [">=2.99.0 <3.0"],
  "permissions": ["ui.pages", "hooks.dialogs"],
  "files": [{ "path": "MyAddon.dll", "sha256": "…" }],
  "defaultEnabled": false
}
```

- `entry` 是类型全名；加载器会在包内所有 dll 中寻找实现 `ISwapAddon` 的那个类型。
- `apiVersion` 支持 `*`、`=x.y`、`>=x.y`、`<x.y`（空格分隔可组合）。
- `shapes` / `hostVersions` 是模块**作用域**：前者限定交付形状（`injected` / `patched` / `*`），
  后者限定支持的 PCL 版本。`hostVersions` 的每一项互相 OR：`"*"`、精确版本（`"2.99.0.0"`）或
  比较式（`">=2.99.0"`、`"<3.0"`、`"=2.99.0.1"`）；留空表示任意宿主（Alpha 9 之前的行为）。
  不在作用域内的模块**不会启动**，状态显示「作用域不适用 / 宿主版本不适用」，不会被标记为失败；
  安装时平台会先把不匹配写进日志提醒（仍然允许安装 —— 你可能是在为另一个 PCL 版本做准备）。
- `permissions` 是**请求清单**：非空时启用严格门控，用户可在模块详情里逐项授予/撤销。新模块
  默认获得其请求的全部权限，旧 `modules.json` 没有权限字段时也按此兼容；显式撤销立即生效。
- `files` 是**负载清单**（每个文件的 SHA-256），由打包器自动写入；`signature.json` 的 RSA-SHA256
  签名覆盖 `manifest.json` 的原始字节，因此签名同时覆盖清单与全部负载哈希。

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
Copy-Item MyAddon.swapaddon "$env:APPDATA\PCL2Swap\addons\"
```

更省事的做法：PCL → 设置 → **Swap Addon** → **模块管理 → 安装模块…**，选包即装（见 §11）。

一个可直接运行的参考实现：`src/PCL2Swap.SampleAddon`（同时被 harness 的 `--addontest` 用作端到端验证）。

---

## 4. 当前限制（1.0）

1. **卸载是行为级的**：`Attach` 的句柄被释放、或模块被停用/卸载后，gate 让调用直通原方法，
   但物理跳转补丁要等进程结束才消失。实例 handler、引用类型参数、Prefix/Postfix/Replace 都已支持。
2. **同进程、同权限**：Addon 与宿主共享 CLR 与地址空间；请把 Addon 当作受信任代码分发。
3. **停用立即生效，卸载彻底清理**：停用让钩子类拦截立即放行（结构类修复如原版游戏页要在重启后
   才完全回退）；卸载会注销页面、删模块目录与包（入口 DLL 被占用时留到下次启动清理）。
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
| `Loader` | 任务栏进度（`TaskbarProgress` / `RefreshTaskbar` / `TaskbarAdd`）、LoaderBase 的 `State` / `Progress` / `IsRunning` / `Start` / `Cancel` / `Failed`、`GetName` / `ProgressWeight` / `GetError`、`Watch` / `WatchPreviewFinish`、`CreateCombo` / `ListTasks` / `ListAll` |

未覆盖的成员仍可通过 `ISwapApi.Surface`（全量清单）+ `ISwapApi.Reflect` 直达。

### 10.1 任务与进度（Alpha 8 起）

`Api.Pcl.Loader` 现在能观察宿主的加载器，而不是只能看任务栏的百分比：

| 成员 | 语义 |
|---|---|
| `ListAll(onlyRunning)` | 宿主任务栏上的加载器（`ModLoader.LoaderTaskbar`），`onlyRunning` 只留 `Loading` 的 |
| `ListTasks(loader, onlyRunning)` | 把一个加载器（组合）摊平成任务列表：组合走它自己的 `GetLoaderList`，普通任务走宿主静态 `LoaderCombo.GetLoaderList` 递归 |
| `GetName` / `GetProgressWeight` / `SetProgressWeight` / `GetError` | 读取/设置宿主加载器字段 |
| `Watch(loader, onProgress, onStateChanged)` | 订阅宿主自己的 `ProgressChanged` 与 `OnStateChangedUi` 事件（**不是轮询**）；两个回调会先用当前值各触发一次，加载器进入 Finished/Failed/Canceled 时自动解除订阅；返回的句柄 `Dispose` 也能提前解除 |
| `WatchPreviewFinish(loader, onLoaded)` | 订阅 `PreviewFinish`（宿主把加载器标记完成之前） |
| `CreateCombo(name, loaders, inputType)` | 用宿主自己的 `LoaderCombo<TInput>` 组合多个加载器，任务栏进度、取消与聚合进度都保持宿主的实现 |

宿主事件的参数是值类型（`LoadState` 枚举、`double`），模块侧只写 `Action`，平台内部用一个编译好的适配委托把参数装箱转交（每个订阅一份，随取消订阅释放）。

**踩坑记录（Alpha 8 修）**：如果宿主目录里留着上一次磁盘补丁形状复制过去的 `PCL2Swap.Api.dll`，CLR 会先绑它——注入形状内嵌的同名程序集就被跳过，结果是 `MissingMethodException`（"找不到方法 ListAll"）。现在 shim 入口会先把解包出来的 `PCL2Swap.Api` / `Gee.External.Capstone` / `MonoMod.Utils` / `Mono.Cecil` 预载定版；Loader 复制运行时文件时也改成按内容哈希比较，不再看时间戳。

## 11. 模块管理（Alpha 7 起）

### 11.1 包信任链（v26.1-Alpha 1 起）

平台不强制签名，但会给每个包一个**可验证的身份**：

| 状态 | 含义 | 安装策略 |
|---|---|---|
| `Trusted` | 签名密钥在 `<数据根>\trusted-keys\*.xml` 里，且清单里每个文件的 SHA-256 都对得上 | 放行 |
| `Unsigned` | 没有 `signature.json`（老包）；有清单就照样校验文件 | 放行 |
| `UnknownKey` | 有签名，但密钥不在受信任集合里 —— 签名本身不构成信任 | 放行但明确提示 |
| `InvalidSignature` | 受信任密钥的签名校验失败 | **拒绝安装** |
| `TamperedFiles` | 签名有效但包内文件与清单不符（缺失、哈希不符、或出现未列入清单的 dll） | **拒绝安装**；启动扫描时也会把这种模块标为失败且不启用 |

给模块签名（作者侧）：

```powershell
# 1) 生成密钥对（只做一次；私钥自己保管，公钥给用户）
.\tools\PclAddonPack\bin\Release\net48\PclAddonPack.exe --gen-key .\keys\my.key.xml

# 2) 打包并签名（自动写入 files 清单与 signature.json）
.\tools\PclAddonPack\bin\Release\net48\PclAddonPack.exe --project .\bin --id demo --name Demo `
    --version 1.0.0 --entry Demo.Addon --sign .\keys\my.key.xml
```

用户侧只需要把作者的 `<key>.pub.xml` 复制到 `%APPDATA%\PCL2Swap\trusted-keys\`（命名随意）。
安装预览与模块详情都会显示「签名状态」，包括密钥 id 与文件校验结果。

另外两个版本级提醒（写在安装预览里，不阻塞）：

- **降级安装**：包内版本低于已安装版本时提示 `旧 → 新`（防止误用旧包）。
- **同版本换内容**：同一 id+版本但负载清单摘要与已安装的不一致时提示（重打包/换包会露出来）。

设置 → Swap Addon → **模块管理** 提供安装 / 卸载 / 打开目录 / 刷新；列表里每行都有
`启用|禁用`，第三方模块额外有 `卸载`（红色）。行为：

| 动作 | 语义 |
|---|---|
| **安装模块…** | 选一个 `.swapaddon`（Windows 文件对话框）→ **先弹预览确认**（名称 / id / API 范围 / 入口 / 形状与宿主版本作用域 / 权限声明 / 包大小与 SHA-256，作用域不符时当场提示"安装后不会启用"）→ 确认后校验 manifest → 归档到 `<数据根>\addons\<id>-<版本>.swapaddon` → 解包到 `<数据根>\addons\<id>\<版本>\` → 立即 `OnLoad/OnEnable`、注册页面并重建设置侧栏。**不需要重启 PCL。** |
| **更新** | 安装同 id 的新版本 = 先停旧版、撤下它的页面，再装新版；模块自己的 `config.json` 保留。 |
| **卸载** | `OnDisable`/`OnUnload` → 注销它注册的设置页（侧栏重建，若用户正在那一页会切回宿主默认页）→ 删除模块目录（含 `config.json`）与归档包。源包只在被复制进模块目录后才删，用户自己 Downloads 里的原文件保留。 |
| **延迟删除** | 入口 DLL 已被 CLR 加载、文件被占用时，路径写进 `<数据根>\addons\.pending\*.txt`，下次启动先清理再扫描模块。 |
| **作用域（shapes + hostVersions）** | `shapes` 声明适用的交付形状（`injected` / `patched` / `*`），`hostVersions` 声明支持的 PCL 版本（`["*"]`、精确版本或 `">=2.99.0"` 这类比较，多项 OR）。任一不匹配 → 状态「作用域不适用 / 宿主版本不适用」，既不启动也不报失败；安装时平台会把不匹配原因写进日志（仍然允许安装，方便为其它 PCL 版本备包）。 |
| **日志** | 每行都有「日志」按钮：打开该模块自己的日志文件 `%APPDATA%\PCL2Swap\logs\modules\<id>.log`（256 KB 轮转一次，保留 `.1` 备份）。模块通过 `Api.Log/LogWarning/LogError` 写的一切、以及平台为它挂的每个钩子，都会记在这里；全局日志里同一行会带 `[<模块id>]` 前缀。 |
| **崩溃隔离** | 模块 handler 抛异常时会**自动停用那个 handler**（不重试、不重复炸同一条宿主路径），宿主调用回退到原方法/下一个 Replace handler；失败次数记在模块行上（红色「钩子抛错 N 次」），细节写进模块日志。 |
| **详情视图** | 每行「详情」按钮展开该模块的管理页：状态 / id / 版本 / **安装时间** / **来源包** / **包 SHA-256** / 模块目录 / 配置文件 / API 范围 / 入口 / 作者 / 形状与宿主版本作用域 / 权限声明 / 该模块的钩子数与命中抛错 / 钩子链 / 模块日志尾部，以及 `禁用|启用`、`重载`、`打开日志`、`打开配置目录`、`收起`、`卸载`。来源与哈希在安装时记录（启动时发现的模块用包文件本身作为来源与时间）。 |
| **重载** | 非内置模块的 `重载` = `OnDisable` → 平台清理该模块遗留的钩子注册 → `OnLoad/OnEnable`。模块自己 dispose 令牌是最干净的方式；忘记 dispose 时由平台的清理兜底（否则重载会让 Prefix handler 叠加执行）。 |

manifest 的 `shapes` / `permissions` 现在会被真正解析（Alpha 6 之前数组被 `Convert.ToString`
吞掉，永远是空），模块行会显示 `作者 · API 版本范围 · 形状 · 权限声明`（最多列三个权限名）。
`permissions` 现在同时承担“请求”和“用户授权”两层语义：模块与宿主仍同进程，`Reflect` /
`Surface` 本来就是逃生口，平台不假装能沙箱化同进程代码；但 hooks/pages/files/clipboard/
network/logs/secret/launch/settings/instance 这些有副作用的入口会按用户授予状态实时门控。

真机 e2e 也能不开对话框地驱动这两条路径（`docs/local-e2e.zh.md`）：

```powershell
$env:PCL2SWAP_E2E_INSTALL   = "D:\包\demo.sample.swapaddon"   # 启动时安装（走页面同一条代码路径）
$env:PCL2SWAP_E2E_UNINSTALL = "test.sample"                   # 可选：紧接着卸载
$env:PCL2SWAP_E2E_PREVIEW   = "D:\包\demo.sample.swapaddon"   # 可选：9 秒后弹出安装预览对话框（脚本可点按钮验证）
```

## 12. API 1.1（v26.1-Alpha 5 起）：双语与更宽的前后端

`Host.ApiVersion` 现在是 `1.1.0`。本次只**增加**成员，1.0 的 addon 不需要改动，
`apiVersion: ">=1.0 <2.0"` 继续有效。

### 12.1 `ISwapApi.I18n`（新）

```csharp
string en = api.I18n.T("宿主信息");            // English 时返回 Host information
string v  = api.I18n.T("中文", "English");     // 显式双语，不查词典
api.I18n.Register("同步完成", "Sync finished");// 运行时扩词典
api.I18n.SetLanguage(SwapLanguage.English);    // Auto / Chinese / English
bool isEn = api.I18n.IsEnglish;
```

完整说明见 `docs/i18n.zh.md`。

### 12.2 `IPclAccess.Ui` / `IPclAccess.Logs`（新后端）

```csharp
string page = api.Pcl.Ui.CurrentPage;        // 当前页面类型名
object win  = api.Pcl.Ui.MainWindow;         // WPF Window（未创建时 null）
object left = api.Pcl.Ui.FindPage("FrmSetupLeft");
int hits    = api.Pcl.Ui.Translate(win);     // 对宿主子树翻译，返回替换数
int back    = api.Pcl.Ui.Restore(win);       // 回退

string path = api.Pcl.Logs.LogPath;          // PCL\Log1.txt
string tail = api.Pcl.Logs.Tail(120);        // 最近 120 行
```

`Logs` 只读、无副作用；`Ui.Translate/Restore` 与内置英文层使用同一词典与幂等规则。

### 12.3 `IUi` 新前端控件（4 个）

```csharp
api.Ui.Slider("音量", 0, 100, 1, 50, value => api.Log("volume=" + value));
api.Ui.TextInput("路径", "C:\\demo", text => api.Data.Set("path", text));
api.Ui.Link("打开主页", "https://github.com/BlockConnectExp/PCL2.dlswap");
api.Ui.Separator();
```

- `Slider`：标签 + 滑杆 + 数值读数，使用 PCL 设置页的行网格；
- `TextInput`：优先宿主 `MyTextBox`，退回标准 WPF `TextBox`；
- `Link`：下划线链接，走宿主 `OpenWebsite`；
- `Separator`：主题色 1px 分隔线。

所有控件返回 `FrameworkElement`，可直接塞进 `Card`/`Text` 组成的页面。

## 13. API 1.2（v26.1-Alpha 6 起）：更多页槽位与 Shell/Clipboard

`Host.ApiVersion` 现在是 `1.2.0`（只增加成员，1.0/1.1 addon 不受影响）。

### 13.1 `IUi.RegisterMorePage`（第二类页面槽位）

```csharp
api.Ui.RegisterMorePage("demo.more", "Demo Page", () =>
    api.Ui.Card("Demo",
        api.Ui.Text("This page lives in the More sidebar.")));
```

- 与 `RegisterSettingsPage` 完全对称：`ScopedUi` 会给 id 加模块前缀，卸载时自动注销；
- 实现 detour `PCL.PageOtherLeft.PageCheck/PageGet`，复用宿主 `PageChangeRun`；
- 模块页 id 固定 **200+ 段**，与设置页 100+ 不冲突；
- 侧栏项单实例（watcher 只补缺，不重复插入）。

### 13.2 `IPclAccess.Shell` / `IPclAccess.Clipboard`

```csharp
api.Pcl.Shell.OpenFolder(api.Host.AddonDirectory);   // ModBase.OpenExplorer
api.Pcl.Shell.OpenFile(logPath);                     // shell execute

string clip = api.Pcl.Clipboard.GetText();           // ModBase.ClipboardGetText
bool ok = api.Pcl.Clipboard.SetText("copied");       // ModBase.ClipboardSet(text, false)
```

两者优先使用 PCL 自己的实现，失败时才回退到系统 API；`SetText` 在两条路径都失败时返回 false。

## 14. 权限严格门控（v26.1-Alpha 7 起）

`manifest.permissions` 从「展示」升级为「可执行」：

- **声明了 permissions（非空）→ 严格模式**：未声明的 gated 访问抛
  `UnauthorizedAccessException`，并写入该模块日志；
- **未声明/空 → 兼容模式**：保持旧行为（全部允许）；
- `<数据根>\security.json` 写 `{"forceStrict":true}` 可强制全部模块严格。
- 新安装模块默认获得全部请求权限；模块详情中的复选框可逐项撤销或恢复；
- 撤销 `hooks` 会立即让已有 handler 变为直通原方法，恢复后无需重挂即可重新生效；
- 授权状态写入 `modules.json` 的 `permissions` 数组，与 `enabled` 独立保存。

| 权限名 | 门控点 |
|---|---|
| `hooks` | `IHooks.Attach` |
| `pages` | `Ui.RegisterSettingsPage` / `RegisterMorePage` |
| `files` | `Pcl.Shell.OpenFolder/OpenFile` |
| `clipboard` | `Pcl.Clipboard.GetText/SetText` |
| `network` | `Pcl.Net.*` |
| `logs` | `Pcl.Logs.LogPath/Tail` |
| `secret` | `Pcl.Secret.*` |
| `launch` | `Pcl.Launch.*` |
| `settings` | `Pcl.Settings.*` |
| `instance` | `Pcl.Instance.*` |
| `services` | `api.Services.*` |

声明 `"*"` 表示全部允许。Loader/Animation/Base/Download/Ui/I18n/Data/Log 不门控。

**别名兼容（v26.2-Alpha 1 起）**：第三方模块常见的命名会自动归一化——去掉 `host.` 前缀后，
`hooks.attach`→`hooks`、`ui.pages`→`pages`、`settings.read/write`→`settings`、
`clipboard.read/write`→`clipboard`、`files.read/write`→`files`、`network.download`→`network`、
`logs.read`→`logs`、`update`→`secret`。

**反射不门控**：`Reflect` / `Surface` 不再要求 `reflection` 权限。模块与宿主同进程，无法真正
沙箱化反射；权限只约束有副作用的能力（hooks/pages/files/clipboard/network/logs/secret/launch/
settings/instance）。

示例 manifest：

```json
{
  "schema": 1,
  "id": "demo.module",
  "permissions": ["hooks", "pages", "files"]
}
```

该模块若调用 `api.Pcl.Clipboard.GetText()` 将被拒绝；在模块管理页会显示「严格」标记。
模块详情中的权限授予区会显示请求/授予/拒绝状态；撤销会立即影响 gated API 与已安装 hook。

## 15. API 1.3（v26.2-Alpha 6 起）：更多后端与前端

Alpha 6 时 `Host.ApiVersion` 为 **1.3.0**。本次只增加成员，API 1.0/1.1/1.2 的 addon 继续有效。

### 15.1 导航、线程、环境、账户/皮肤后端

```csharp
// 页面路由走 PCL 自己的 PageChange / PageBack，保留页面栈和动画
api.Pcl.Navigation.Go("Setup", "SetupUI");
string root = api.Pcl.Navigation.CurrentRootPage;       // Setup
string sub  = api.Pcl.Navigation.CurrentSubPage;        // SetupUI
string page = api.Pcl.Navigation.CurrentPage;           // PageSetupUI
if (api.Pcl.Navigation.CanGoBack) api.Pcl.Navigation.Back();

// 强制在宿主 UI 线程执行，或从后台线程安全回到 UI 线程
api.Pcl.Threading.Invoke(() => api.Ui.Hint("updated", SwapHintType.Success));
var value = api.Pcl.Threading.Invoke(() => api.Pcl.Instance.FolderSelected);
using (var timer = api.Pcl.Threading.Repeat(TimeSpan.FromSeconds(2), RefreshDashboard)) { }

// 只读环境事实
string host = api.Pcl.Environment.HostPath;
string data = api.Pcl.Environment.DataDirectory;
bool admin = api.Pcl.Environment.IsAdministrator;

// PCL 当前登录/皮肤对象，不把 token 或密码放进 SDK
string name = api.Pcl.Profiles.LoginName();
object login = api.Pcl.Profiles.LoginData();
object skin = api.Pcl.Profiles.SelectedSkin();
```

权限：`Navigation` 需要 `pages`，`Profiles` 需要 `launch`；`Threading` 和 `Environment` 是低副作用
只读/调度能力，不额外门控。`Pcl.Net` 仍需要 `network`。

### 15.2 宿主 HTTP 请求

`IPclNetAccess.Request/Get` 把 addon 的 HTTP 需求交给 PCL 自己的 `NetRequestByClientRetry`
完成重试、编码、Accept、二维 headers 和浏览器头处理；Swap 不另建网络客户端。

```csharp
string json = api.Pcl.Net.Get("https://example.com/api", requireJson: true);
string text = api.Pcl.Net.Request(
    "https://example.com/api", "POST", backupUrl: null, content: "{\"ok\":true}",
    contentType: "application/json", accept: "application/json",
    headers: new Dictionary<string, string> { ["X-Addon"] = "demo" });
```

失败时由宿主 helper 抛出；长期任务请用 `Threading` 回到 UI 线程更新控件。

### 15.3 新前端控件

```csharp
page.Children.Add(api.Ui.Card("更多控件",
    api.Ui.RadioGroup("模式", new[] { "简洁", "详细" }, 0, i => api.Log("mode=" + i)),
    api.Ui.NumberInput("线程数", 4, 1, 16, 1, v => api.Log("threads=" + v)),
    api.Ui.PasswordInput("Token", "", value => api.Data.Set("token", value)),
    api.Ui.MultilineTextInput("备注", "", 90, text => api.Data.Set("notes", text)),
    api.Ui.Image(@"C:\assets\banner.png", 320, 96)));

var progress = api.Ui.Progress("同步", 0, true);
page.Children.Add(progress.Element);
progress.Status = "Connecting...";
progress.Value = 0.42;
```

`Hint` 新增 `SwapHintType.Info/Success/Warning/Error` 重载，分别映射到 PCL 的 Blue/Green/Red
通知样式。`Image` 只读取本地文件，不下载远程图片；远程内容请先用 `Pcl.Net` 获取再交给 addon。

## 16. API 1.4（v26.2-Alpha 7 起）：启动恢复与安全模式

`Host.ApiVersion` 现在是 **1.4.0**。新增宿主事实：

```csharp
bool safe = api.Host.SafeMode;
string why = api.Host.SafeModeReason;
string recovery = api.Host.RecoveryDirectory;
```

宿主每次启动写入 `<数据根>\recovery\startup-journal.json`。连续两次在稳定窗口前异常退出会进入
安全模式；该会话跳过全部 Addon，但保留 Swap 页面和恢复入口。`Download MC`、`OTA Protector`
等内置模块也遵守这一隔离规则。

完整的恢复流程和环境变量见 `docs/recovery.zh.md`。

## 17. API 1.5（v26.2-Alpha 8 起）：恢复后端与 Banner 前端

`Host.ApiVersion` 现在是 **1.5.0**。新增成员都是只增不改，API 1.0-1.4 addon 保持源码兼容。

### 17.1 `ISwapApi.Recovery`（只读后端）

```csharp
bool safe = api.Recovery.SafeMode;
string reasonCode = api.Recovery.SafeModeReasonCode; // unstable-start / previous-safe-mode / forced-env
string phase = api.Recovery.LifecyclePhase;       // create / onLoad / onEnable / stable...
string started = api.Recovery.LastStartedModule;
string completed = api.Recovery.LastCompletedModule;
string suspect = api.Recovery.SuspectModule;
string suspectPhase = api.Recovery.SuspectPhase;   // create / onLoad / onEnable
string journal = api.Recovery.JournalPath;
string diagnostics = api.Recovery.Diagnostics();
```

`SuspectModule` 是下一次未稳定启动根据“最后开始生命周期的模块”得出的定位线索，不是平台
自动定罪。为安全起见，该接口只读；清除安全模式和禁用疑因模块只能由内置恢复 UI 执行，避免
普通 Addon 意外解除宿主保护。

### 17.2 `IUi.Banner`（语义状态横幅）

```csharp
page.Children.Add(api.Ui.Banner(
    "网络不可用",
    "请求会在连接恢复后重试。",
    SwapHintType.Warning));
```

`Banner` 用一个左侧语义色轨和一段正文表达页面级状态，不嵌套 Card。类型支持
`Info` / `Success` / `Warning` / `Error`；标题和正文由调用方自行国际化。

### 17.3 Swap 管理页英文补全

Alpha 8 把 Addon 管理页与恢复页中带运行时变量的句子改为在构建时显式选择中英文，不再依赖
宿主视觉树翻译轮询。覆盖安装/更新/卸载确认、严格权限、作用域、模块状态、详情、日志、疑因
模块和恢复动作。PCL 宿主自身的翻译仍由 `HostTranslator` 与 `I18n.Extra.cs` 继续渐进扩展。

## 18. API 1.6（v26.2-Alpha 9 起）：模块目录、用户授权与签名钩子

`Host.ApiVersion` 现在是 **1.6.0**。新增成员只增不改，API 1.0-1.5 addon 继续可用。

### 18.1 `ISwapApi.Modules`（只读模块目录）

```csharp
foreach (var module in api.Modules.Modules)
{
    api.Log($"{module.Id} {module.Status} hooks={module.HookCount}");
    foreach (var denied in module.DeniedPermissions)
        api.LogWarning($"{module.Id} denied {denied}");
}

SwapModuleInfo demo = api.Modules.Find("demo.sample");
api.Modules.Changed += (_, __) => RefreshDashboard();
```

快照包含模块身份、启停状态、作用域、信任链、请求/授予/拒绝权限和 hook 计数/命中/错误。
目录始终只读；授权修改仍由内置模块详情页执行。

### 18.2 签名钩子

旧 `Attach(declaringType, methodName, …)` 保留。遇到重载会记录警告并继续使用首个匹配；新代码
应先发现目标，再按完整签名精确挂载：

```csharp
var targets = api.Hooks.FindTargets("PCL.ModNet", "NetDownloadByClient");
var target = targets.Single(t => t.ParameterTypes.Count == 7);

using (api.Hooks.Attach(target, "demo.download", handler, HookMode.Prefix))
{
    // handler 必须是该目标 ABI 对应的 Delegate。
}
```

`SwapMethodTarget` 的 `ParameterTypes` 接受全名、短名或 C# 别名（`string` / `int` 等），并可
限定 `ReturnType` / `Static`。无匹配或多匹配会抛带全部候选的异常。`IsHooked(target)` 表示该
目标当前仍有活跃 handler；空 gate 不算已挂钩。

### 18.3 新前端控件

```csharp
page.Children.Add(api.Ui.Badge("READY", SwapHintType.Success));
page.Children.Add(api.Ui.Expander("Advanced", false, () =>
    api.Ui.Note("Lazily constructed content.")));
page.Children.Add(api.Ui.Tabs(
    new[] { "Overview", "Details" }, 0,
    index => api.Ui.Text(index == 0 ? "Overview" : "Details")));
```

`Expander` 内容按需构建；`Tabs` 使用原生 WPF `TabControl`；模块详情页现在直接展示可编辑的
权限授予区，撤销不会跳回页面顶部。

## 19. API 1.7（v26.2）：HookContext、诊断后端与时间线

`Host.ApiVersion` 为 **1.7.0**。新增成员仍然只增不改。

### 19.1 `SwapHookContext`

`Replace` handler 内可以通过 `SwapHookContext.Current` 读取本次调用的模块、hook id、模式、
目标签名、接收者和参数。需要调用原方法时使用 `InvokeOriginal()`；不传参数会复用当前参数，
传入数组则覆盖本次参数，并且不会递归进入当前 handler 链。

```csharp
using (api.Hooks.Attach(target, "demo.replace", (Func<int, int>)(value =>
       {
           var context = SwapHookContext.Current;
           var original = Convert.ToInt32(context.InvokeOriginal());
           return original + 10;
       }), HookMode.Replace))
{
}
```

Context 是 `ThreadStatic`，嵌套调用会恢复上一层，handler 返回后恢复为 `null`。

### 19.2 `ISwapApi.Diagnostics`

运行时日志同时进入最多 512 条的有界时间线：

```csharp
foreach (var item in api.Diagnostics.Recent(50))
    api.Log($"{item.TimestampUtc:O} {item.Severity} {item.ModuleId} {item.Message}");

string text = api.Diagnostics.Export(200);
api.Diagnostics.Changed += (_, __) => RefreshTimeline();
```

`ModuleId` 从模块作用域日志前缀解析；未归属模块的事件为空。`Changed` 只表示环形缓冲区新增
事件，不保证每次事件都保留到消费者读取。

### 19.3 `IUi.Timeline`

`Timeline(events, maxHeight)` 构建只读 WPF 事件流，按语义色显示时间、等级和模块 id。它不启动
定时器，调用方在 `Diagnostics.Changed` 或自己的刷新周期里重建/更新页面。

## 20. API 1.8（v26.2）：英文质量、模块协作与更多前后端

`Host.ApiVersion` 现在是 **1.8.0**。这是 v26.2 正式版收口版本，API 1.0-1.7 保持源码兼容。

### 20.1 英文层增强

- 内置词典扩展到 **507 条精确映射 + 60 条动态短语**；
- 磁盘覆盖支持 `<数据根>\i18n\en-US.json`，以及 `<数据根>\i18n\en-US\*.json` 多文件语言包；
- `RegisterMany(dictionary)` 原子式批量扩展，返回实际变化的条数；
- `Format(chineseFormat, args)` 先翻译再按当前 UI 文化格式化；
- `TryTranslate(chinese, out translated)` 做非破坏查询；
- `HostTranslator` 为每个控件/属性记录原文、英文值和最后写入状态。切回中文时只恢复仍处于
  本层写入值的控件；宿主已经重新生成的新文本不会被过期缓存覆盖。

```csharp
api.I18n.RegisterMany(new Dictionary<string, string>
{
    ["同步完成"] = "Sync completed",
    ["已处理 {0} 项"] = "Processed {0} items"
});

var text = api.I18n.Format("已处理 {0} 项", 12);
```

### 20.2 `ISwapApi.Files`（沙箱化文件后端）

每个模块拥有独立根目录，API 只接受相对路径，拒绝绝对路径和 `..` 越界：

```csharp
api.Files.WriteText("cache/index.json", json);
var jsonAgain = api.Files.ReadText("cache/index.json");
var files = api.Files.List("cache", "*.json", recursive: true);
api.Files.WriteBytes("assets/blob.bin", bytes);
api.Files.Delete("cache/index.json");
```

文本统一 UTF-8（无 BOM）。`Root` 暴露实际目录供诊断；严格模式需要 `files` 权限。该接口是同
进程便利层，不是安全沙箱，模块仍可自己调用 `System.IO`，权限治理只约束平台入口。

### 20.3 `ISwapApi.Messaging`（模块间消息）

`IAddonMessaging` 提供进程内 topic 发布/订阅：

```csharp
using (api.Messaging.Subscribe("demo.refresh", message =>
       api.Log($"from {message.Source}: {message.Payload}")))
{
    var delivered = api.Messaging.Publish("demo.refresh", new { value = 42 });
}

var topics = api.Messaging.Topics;
```

订阅支持 `"*"` 通配；发布 topic 不能是通配。payload 保留 CLR 对象引用，不序列化；需要跨模块
稳定契约时使用字符串、JSON 或两边共同引用的不可变 DTO。订阅者异常被隔离并写入来源模块日志，
不会阻断同 topic 的其他订阅者。严格模式需要 `messaging` 权限；模块禁用、重载或卸载时会自动
移除它留下的订阅。

### 20.4 新前端构件

```csharp
page.Children.Add(api.Ui.Table(
    new[] { "Module", "Status" },
    new IReadOnlyList<string>[]
    {
        new[] { "Demo", "Enabled" },
        new[] { "Theme", "Disabled" }
    },
    new[] { 2.0, 1.0 }));

page.Children.Add(api.Ui.EmptyState(
    "No addons installed",
    "Install a .swapaddon package to start.",
    api.Ui.Button("Open addon folder", OpenFolder)));

page.Children.Add(api.Ui.CodeBlock("{\"schema\":1,\"enabled\":true}", 180));
page.Children.Add(api.Ui.Scroll(new StackPanel { /* long page content */ }));
```

`Table` 使用只读 `DataGrid`，列宽按权重分配；`EmptyState` 是居中零内容状态；`CodeBlock` 使用
等宽字体和只读滚动区域；`Scroll` 用 PCL 的 `MyScrollViewer` 承载长页面，缺失时退回标准 WPF。
四者与现有 Card/Text/Note 组合使用，不嵌套 Card。

## 21. API 1.9（v26.3）：可编辑功能作用域

`Host.ApiVersion` 现在是 **1.9.0**。这一层把 PCL2 Swap 的 “LSPosed scope” 从包级兼容范围
扩展为模块内部、用户可即时切换的功能开关；API 1.0-1.8 保持源码兼容。

### 21.1 Manifest

```json
{
  "id": "demo.sample",
  "scopes": [
    {
      "id": "demo.core",
      "name": "基础功能",
      "nameEn": "Core features",
      "description": "页面主体与模块消息",
      "descriptionEn": "Page body and module messaging",
      "defaultEnabled": true
    },
    {
      "id": "demo.advanced",
      "name": "高级工具",
      "nameEn": "Advanced tools",
      "description": "可选的数据与诊断前端",
      "descriptionEn": "Optional data and diagnostic front ends",
      "defaultEnabled": false
    }
  ]
}
```

`id` 限定为字母、数字、点、下划线和连字符，最长 96 字符；重复 id（忽略大小写）会被忽略。
旧版字符串数组 `["demo.core"]` 仍受支持，等价于名称和 id 相同、默认开启。

`PclAddonPack` 可以重复传入 `--scope`：

```powershell
--scope "demo.core;基础功能;页面主体;true;Core features;Page body"
```

字段顺序为 `id;Name;Description;defaultEnabled;NameEn;DescriptionEn`。Windows PowerShell 5.1
读取无 BOM UTF-8 脚本时可能损坏中文，脚本中建议使用 ASCII，或改用 PowerShell 7 / UTF-8 BOM。

### 21.2 后端与 Hook 门控

```csharp
foreach (var scope in api.Scopes.Scopes)
    api.Log($"{scope.Id}: {scope.Enabled}");

if (api.Scopes.IsEnabled("demo.advanced"))
    RegisterAdvancedPage();

using (api.Hooks.Attach(
           "PCL.ModBase", "ToUnixTimestampUtc",
           "demo.hook", "demo.core",
           (Action<DateTime>)(_ => Observe()), HookMode.Prefix))
{
}
```

带 `scopeId` 的 `Hooks.Attach` 会在每次调用时读取实时状态。关闭 scope 后，注册仍在，但 handler
立即被跳过，调用直通原方法；重新开启后无需重新挂载即可恢复。未声明的 scope 在挂载时会抛出
`ArgumentException`，避免拼写错误悄悄变成永久直通。未传 `scopeId` 的旧重载保持原有行为。

用户选择写入 `modules.json` 的 `scopes` 数组。模块热更新按 id 继承用户选择；模块禁用、重载和
安全模式继续优先于 scope 门控。

### 21.3 前端与英文

`Swap Addon → 模块详情 → 功能作用域` 提供逐项复选框和实时启用计数。英文界面优先使用
`nameEn` / `descriptionEn`，缺失时回退到中文名称。`api.Scopes.Changed` 在用户修改任一 scope 后
触发；订阅者异常由宿主隔离。

```csharp
api.Scopes.Changed += (_, __) =>
    api.Ui.Hint(api.I18n.T("作用域已更新。", "Scopes updated."));
```

完整可运行模板见 `examples/demo-addon`；它会按 `demo.core` 启停消息后端，并按 `demo.advanced`
决定是否构建扩展表格和代码块前端。

## 22. API 1.10（v26.3）：可靠配置与模块服务

`Host.ApiVersion` 现在是 **1.10.0**。这一层补上模块长期运行需要的持久化与互操作能力，
仍然只增加成员，旧 Addon 继续按原有 API 运行。

### 22.1 `ISwapApi.Config`

`api.Config` 与 `api.Data` 指向同一个模块私有存储，但前者提供 schema、原子保存和事务迁移：

```csharp
var migrated = api.Config.Migrate(2, tx =>
{
    if (tx.Get("theme") == "dark")
        tx.Set("appearance", "dark");
    tx.Remove("theme");
});

api.Config.Set("lastRun", DateTime.UtcNow.ToString("O"));
api.Config.Save();
```

旧版扁平 `{"key":"value"}` 文件自动按 schema 1 读取。新文件使用
`{"_schema":2,"_updatedUtc":"...","_values":{...}}` 信封格式，先写临时文件再用
`File.Replace` 原子替换，并保留 `config.json.bak`。迁移回调只操作 staging 副本，成功落盘后
才一次性提交；回调抛错或写入失败时 schema 和旧值保持不变。

```csharp
api.Config.SchemaVersion; // 当前版本
api.Config.Keys;          // 排序后的键快照
api.Config.Changed += (_, __) => RefreshSettingsPage();
```

线程安全覆盖 `Get/Set/Remove/Save/Migrate`。`Changed` 在内存值或 schema 变化时触发；单纯
`Save()` 不重复触发。

### 22.2 `ISwapApi.Services`

模块可以把一个 CLR 对象发布为进程内服务，其他已授权模块按类型查询：

```csharp
public interface IThemeService
{
    void Refresh();
}

using (api.Services.Register(
           "blockconnect.theme.api", new ThemeService(), "1.0",
           "Theme refresh capability"))
{
    var ok = api.Services.TryGet<IThemeService>("blockconnect.theme.api", out var service);
    var strict = api.Services.Get("blockconnect.theme.api", typeof(IThemeService));
}
```

服务 id 推荐使用反向域名或稳定的命名空间，大小写不敏感且全局唯一；重复 id 会抛
`InvalidOperationException`。`TryGet` 同时校验运行时契约类型，类型不匹配返回 `false`，
`Get` 返回包含提供者与实际类型的可读错误。

严格模式使用 `services` 权限；撤销权限会立刻阻断注册、查询和事件订阅。注册 token 释放、
模块禁用、重载、异常退出清理和卸载都会移除该模块的服务：

```csharp
api.Services.Changed += (_, args) =>
    api.Log($"{args.Kind}: {args.Service.ServiceId} from {args.Service.Provider}");
```

服务对象本身不自动 `Dispose`，所有权仍属于发布模块；平台只撤销注册并切断发现。

### 22.3 前端与目录

模块详情会显示配置 schema、键数量、已发布服务表和每个服务的契约类型、版本与说明。
`IModuleCatalog.Find()` 同时返回 `ConfigSchemaVersion`、`ConfigKeyCount` 和 `Services`，
便于管理页和诊断模块不读取私有路径也能展示治理状态。

公共模板已经同时使用这两个后端，并用 `demo.core` 控制服务的注册/撤销。

> 平台只保证 Swap 注册表和配置写入的线程安全，不把同进程 CLR 对象变成安全沙箱。模块仍不能
> 访问其他模块的私有目录；服务通信应使用明确接口、不可变 DTO 或 JSON 字符串。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
