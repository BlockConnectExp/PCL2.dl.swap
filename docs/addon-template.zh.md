# Addon 模板 / Demo Addon

> Summary: the smallest complete, buildable PCL2 Swap addon. Copy `examples/demo-addon`, rename
> the id, and you have a module with two page slots, widgets, persisted data, strict permissions,
> bilingual feature scopes, versioned config, cross-module services and a live scope-event consumer.

---

## 1 目录结构

```
examples/demo-addon/
├── DemoAddon.csproj     net48 + WPF，引用 PCL2Swap.Api.dll（Private=false）
├── DemoAddon.cs         ISwapAddon 生命周期 + 设置页 + 更多页 + 控件
├── build.ps1            dotnet build + PclAddonPack → demo.sample.swapaddon
└── README.md            使用说明（含 Hook 示例）
```

## 2 构建

```powershell
# 先构建一次 SDK（或指向已有的 PCL2Swap.Api.dll）
dotnet build src\PCL2Swap.Api\PCL2Swap.Api.csproj -c Release

cd examples\demo-addon
.\build.ps1
# -> demo.sample.swapaddon（未签名，安装时显示 Unsigned）
```

`build.ps1 -ApiPath <path>` 可指定其它 SDK 构建。

## 3 模板覆盖的 API

| 能力 | 用法 |
|---|---|
| 设置页槽位 | `api.Ui.RegisterSettingsPage("settings", …)` |
| 更多页槽位 | `api.Ui.RegisterMorePage("more", …)` |
| 原生卡片/行 | `api.Ui.Card` / `InfoRow` |
| 控件 | `api.Ui.Slider` / `TextInput` / `Link` / `Badge` / `Expander` / `Tabs` / `Button(s)` / `Note` |
| 双语 | `api.I18n.T("中文", "English")` |
| 持久化 | `api.Data.Get/Set/Save` |
| 可靠配置 | `api.Config.SchemaVersion/Keys/Changed/Remove/Migrate`；旧扁平 JSON 自动作为 schema 1 读取 |
| 后端 | `api.Pcl.Clipboard.SetText` / `api.Pcl.Shell.OpenFolder` |
| 权限 | manifest 请求 `["pages","files","clipboard","messaging","services"]`，用户可在模块详情逐项授予/撤销 |
| 模块目录 | `api.Modules.Modules` / `Find` / `Changed` |
| 签名钩子 | `api.Hooks.FindTargets` + `SwapMethodTarget` 精确选择重载 |
| 诊断时间线 | `api.Diagnostics.Recent/Export/Changed` + `api.Ui.Timeline` |
| 模块消息 | `api.Messaging.Subscribe/Publish`（`messaging` 权限） |
| 模块服务 | `api.Services.Register/TryGet/Get/Changed`（`services` 权限） |
| 私有文件 | `api.Files.Read/Write/List/Delete`（`files` 权限，路径限制在模块根目录） |
| 数据前端 | `api.Ui.Table` / `EmptyState` / `CodeBlock` / `Scroll` |
| 功能作用域 | `api.Scopes.Scopes/Enabled/IsEnabled/Changed`；manifest `scopes[]` 支持 `nameEn` / `descriptionEn` |
| 作用域 Hook | `api.Hooks.Attach(target, hookId, scopeId, handler, mode)`，关闭 scope 即直通原方法 |

## 4 安装与验证

```powershell
# UI：Swap Addon → 安装模块… → 选择 demo.sample.swapaddon
# 或脚本（真机 e2e）：
$env:PCL2SWAP_E2E_INSTALL = "<repo>\examples\demo-addon\demo.sample.swapaddon"
$env:PCL2SWAP_SELFTEST = '1'
.\dist\injector\Pcl2Swap.Injector.exe --cli inject --exe ".\test\e2e-29901-verify\Pcl2SwapV.exe"
```

预期：设置侧栏出现「示例设置 / Demo Settings」，更多侧栏出现「示例页面 / Demo Page」；
模块详情出现 `demo.core` / `demo.advanced` 两个功能作用域；关闭 `demo.advanced` 后高级前端
不再构建，关闭 `demo.core` 后消息订阅停止且页面进入说明状态。重启后 Launch count 与 scope
选择均保持不变。

## 5 已验证的生命周期（v26.2）

| 阶段 | 证据 |
|---|---|
| 安装 | `Demo addon loaded (launch #1)`，状态 enabled，两类页面出现 |
| 同版本更新 | `Addon 更新：demo.sample 1.0.0 -> 1.0.0`，launch #2，无 IOException |
| 热更新目录 | 旧 DLL 被 CLR 锁定时自动改用 `1.0.0-2` / `1.0.0-3`，旧目录排队延后删除 |
| 卸载 | `[e2e] 卸载结果：True`，设置页与更多页分别注销（PageID 102 / 200） |
| 重启清理 | `待删除模块文件已清理完成`，`addons` 目录只剩三个内置模块 |

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
