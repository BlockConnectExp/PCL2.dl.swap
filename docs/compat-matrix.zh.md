# PCL2 兼容矩阵 / Compatibility Matrix

> Summary: which official PCL2 builds have actually been exercised for the injected shape, and
> with what evidence. Update this table whenever a new host build is validated.

---

## 1 已验证宿主

| PCL2 版本 | 构建号 | MVID | SHA-256（前 16） | 偏移清单 id | 注入自检 | UI 回归 | 备注 |
|---|---|---|---|---|---|---|---|
| 2.14.0.0 | 411 | `e6e9028d-…` | `5582952ded3b4435` | `v2140-rel411` | 历史基线 | — | 早期提取基线 |
| 2.14.0.0 快照 | 412 | `2a08e6c9-…` | `77fbc26a13abf725` | `v2140-snap412` | 历史基线 | — | — |
| 2.99.0.0 | 413 | `926d447e-daff-4169-8c64-adc3d37d5f78` | `6b6e1260cb5a1d6a` | `v299-rel413` | `ok=30 fail=0` | 通过 | 主支持线 |
| **2.99.0.1** | **415** | `5613aa62-1c0b-400f-9223-ece05dc28bef` | `0db34e19d9eeb6d4` | `v2990_1-rel415` | `ok=66 fail=0` | 通过 | Alpha8 英文恢复页/疑因模块复验 |

「注入自检」= 注入后 `PCL2SWAP_SELFTEST=1` 在宿主 UI 线程运行的只读自检，结果落在
`%TEMP%\PCL2Swap\<版本>\pcl2swap.log`。

## 2 功能矩阵（2.99.0.1 实测）

| 功能 | 2.99.0.0 | 2.99.0.1 | 证据 |
|---|---|---|---|
| 下载页「原版游戏」侧栏恢复 | ✅ | ✅ | 侧栏项出现且可切换，`PageDownloadInstall.LoaderInit` 重驱成功 |
| 下载页可用（非「关闭下载」桩） | ✅ | ✅ | 最新版本卡片/正式版/预览版等分区正常 |
| 静默更新拦截 | ✅ | ✅ | `ModSecret.UpdateStart` detour 安装成功 |
| 云下载沙箱查毒 | ✅ | ✅ | `NetDownloadByLoader` 两个重载 detour 安装成功 |
| 关于页 Swap 品牌卡 | ✅ | ✅ | 「PCL2 Swap By BlockConnect · v26.2-Alpha 8」卡片显示在关于与鸣谢顶部 |
| 反馈分流（PCL2 / Swap / 取消） | ✅ | ✅ | 三个按钮完整显示，选择后才跳转对应渠道 |
| 设置子页 Swap Addon / Swap Settings | ✅ | ✅ | 子页可打开，无「未知的设置子页面种类：101」崩溃 |
| 内置 Addon（Download MC / OTA Protector / Cancel Button） | ✅ | ✅ | 三张卡片均为「已启用」 |
| 英文支持（运行时翻译） | ✅ | ✅ | 导航/按钮/设置卡片/启动日志/Swap 页面英文化，语言可切回中文 |
| 英文扩展（下载/登录/联机） | 历史基线 | ✅ | 2.99.0.1 Alpha6：354 条词典；Settings/Download/About/More 真机截图，顶层对话框与菜单 Header 纳入 |
| 更多页槽位（PageOtherLeft） | ✅ | ✅ | 自检页 `Swap Selftest` 插入更多侧栏并切换到 PageID 200 |
| 权限严格门控（opt-in） | ✅ | ✅ | 声明 permissions 的模块未授权访问被拦截；无声明模块保持兼容模式 |
| SDK 1.3 后端/前端 | ✅（harness） | ✅ | Navigation / Threading / Environment / Profiles / Net.Request 与 6 个新控件；真机 `ok=63` |
| 启动恢复/安全模式 | ✅（harness） | ✅ | 三次未稳定启动后全部 Addon `skipped-safe-mode`；恢复卡片可见；清旗标后下次启动恢复 |

## 3 2.99.0.1 特殊适配（历史坑位）

2.99.0.1（build 415）相对 2.99.0 有四处行为差异，均已落地修复：

1. **下载页侧栏折叠**：`PageDownloadLeft..ctor` 会把「原版游戏」项隐藏并把默认子页切到
   Mod 列表。`DownloadPageRestore` 通过驱动 PCL 自身的 `Checked → PageChange → PageGet`
   导航恢复，而不直接写 `PageID`（直接写会跳过 `PageGet`，页面对象为空，下一次点击
   `ItemInstall_Click` 抛 `NullReferenceException`）。
2. **设置子页 101**：`PageSetupLeft.PageGet` 未挂钩时会报「未知的设置子页面种类：101」。
   现在只在 PageGet 就绪后写入口，未知 id 有兜底。
3. **关于页/反馈分流偏移差异**：按 MVID 命中清单 `v2990_1-rel415`，无需按版本分支硬编码。
4. **跨线程 UI 插入**：`PclUi.OnUiThread` 改用 WPF 的真实 `Invoke` 重载，否则 2.99.0.1
   上会抛「调用线程无法访问此对象」。

## 4 复验方法

```powershell
# 1) 注入器（推荐）：启动并注入，注入器会自动做日志验证
.\tools\Pcl2Swap.Injector\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Pcl2Swap.Injector.exe `
  --cli inject --exe ".\test\e2e-29901-verify\Pcl2SwapV.exe"

# 2) 手工：设置自检环境变量后注入
$env:PCL2SWAP_SELFTEST = '1'
.\tools\PclInject\bin\Release\net48\PclInject.exe --pid <pid> --dll .\dist\exloader\Pcl2Swap.Shim.x64.dll
```

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
