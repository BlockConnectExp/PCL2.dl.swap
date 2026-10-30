# PCL2.dl.swap v26.1-Alpha 6

> Sixth Alpha of the v26.1 line: modules can now own a page in the **更多 (More)** sidebar, the
> SDK gains **Shell** and **Clipboard** back-ends, and the English dictionary expands into the
> common settings/login vocabulary. API 1.2.

![A module page registered into the More sidebar](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha6/shot-more-page.png)

---

## What's new

### 1. 更多页槽位（`IUi.RegisterMorePage`）

设置页之外的第二类页面槽位。实现与设置侧栏同构：

- detour `PCL.PageOtherLeft.PageCheck` / `PageGet`，按 `Tag` 识别模块页；
- 复用宿主自己的 `PageChangeRun` 完成切换，`PageID` 写入只在我们的 `PageGet` 生效时进行；
- PageID 使用 **200+** 段，和设置页 100+ 完全不冲突；
- 侧栏项单实例插入（`EnsureSidebarItems`），宿主重建 `PanItem` 时自动补回；
- 卸载模块时 `MorePageRegistry.Unregister` 与设置页一起清理。

### 2. 新后端：`IPclAccess.Shell` / `IPclAccess.Clipboard`

```csharp
api.Pcl.Shell.OpenFolder(api.Host.LogDirectory);   // ModBase.OpenExplorer
api.Pcl.Shell.OpenFile("C:\\dump.log");            // shell execute
string text = api.Pcl.Clipboard.GetText();          // ModBase.ClipboardGetText
bool ok = api.Pcl.Clipboard.SetText("copied");      // ModBase.ClipboardSet(text, showHint=false)
```

两者都优先走 PCL 自己的实现，失败时才回退到系统 API。

### 3. 英文词典扩充

新增常规设置与登录词汇（常规/高级/主题/深色模式/浅色模式/正版登录/离线登录/用户名/密码/登录/
更多设置/启动器设置/游戏设置），并在 `docs/i18n.zh.md` 中记录扩展方式。

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest` / `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS（API 1.2.0） |
| 2.99.0.1 真机自检 | `ok=43 fail=0`（新增 Shell/Clipboard/Ui 断言与 `RegisterMorePage` 自检页） |
| 更多页槽位 | 侧栏出现 `Swap Selftest / PCL2 Swap`，点击后切换到 PageID 200 的自定义页面 |
| 插入稳定性 | 本次会话仅插入一次（修复了 watcher 每 500ms 重插的缺陷），切换后无重复条目 |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha6.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha6.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha6.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha6.dll` | 磁盘补丁产物 |

---

> 更多页槽位与设置页槽位都通过 `ScopedUi` 命名空间化，卸载时会自动撤销对应侧栏项。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
