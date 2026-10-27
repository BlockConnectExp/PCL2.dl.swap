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

把包放进 `%TEMP%\PCL2Swap\addons\` 并重启 PCL（或等平台下一次扫描），
它就会出现在 设置 → **Swap Addon** 页面。

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

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
