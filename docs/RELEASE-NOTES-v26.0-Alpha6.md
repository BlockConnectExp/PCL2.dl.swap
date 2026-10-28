# PCL2.dl.swap v26.0-Alpha 6

> Sixth public Pre-Release: the settings sub-page crash class is closed at the root, the swap's own
> pages now sit on the same measured grid as PCL's shipped settings pages, and the **disk-patch
> shape finally runs the full runtime** (module platform, Swap Addon / Swap Settings, event bus,
> about-page branding) instead of being a static IL rewrite only.

---

## Highlights since Alpha 5

1. **「未知的设置子页面种类：101」的根因收口**。这条报错只有一个成因：宿主
   `PageSetupLeft.PageGet` 的钩子没有生效，而 Swap 把自己的 PageID（100/101）写进了宿主字段，
   之后 `FormMain.PageChangeActual` 再用这个 id 去问宿主，就撞上宿主的 `default:` 分支。现在：
   - PageGet 钩子没装上 → **不再写 PageID**，重进设置页退回宿主自己的子页，不会崩；
   - PageCheck 钩子没装上 → **不插入侧栏项**（插了必崩）；
   - PageGet 收到任何宿主枚举之外的 id → 兜底成 `Default`，不再让宿主抛异常；
   - 视图工厂在非 STA 线程被调用时改走宿主 Dispatcher（WPF 控件必须 STA）。
2. **页面度量与官方设置页对齐**。之前 Swap Settings 的值列落在 x≈527，官方在 x≈389 ——
   根因是一整页共享的 `SharedSizeGroup` 被最长的标签（`VirusTotal / MetaDefender Cloud`）撑开。
   现在改用固定的 110px 标签列 + 24px 间距，卡片内边距改成官方 `20/40/18` + 18px 尾距，
   注释行距 18px，按钮间距 12px，下拉框高度 35px。
3. **Swap Addon 列表重排**：9px 上下留白、72px 右对齐状态列（启用态用主题色）、35px 原生按钮；
   「钩子链」从挤成一行的 `·` 文本改成「目标方法 / 模块 · 模式 · 命中」两行块。
4. **磁盘补丁形状真正可用**：`PclDlSwap.Loader.exe` 现在在 `ModMain..cctor` 末尾注入
   `HookEntryPoints.Bootstrap("disk-patch")`，SwapHost 以 `Patched` 形状启动；静态 IL 改写负责
   OTA / 更新权限 / 下载体，运行时负责模块平台与 IL 表达不了的 UI 功能。Loader 会把
   payload 与依赖复制到补丁 exe 自己的目录（CLR 按 app base 解析新 AssemblyRef）。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| 构建 | payload / patcher / loader 全绿；偏移清单 RVA 校验 `RESULT: PASS` |
| 注入形状真机（2.99.0 同源副本） | 侧栏 3 项、Swap Addon / Swap Settings 正常；离开设置再回来由 `PageGet 命中 Swap 页` 恢复；模块开关立即生效并写入 `modules.json` |
| 补丁形状真机（`PclDlSwap.Loader.exe`） | 日志出现 `SwapHost 启动（形状 Patched）` + 「磁盘补丁装配完成」；两页与开关同样正常；重进设置页不报未知子页 |
| harness | `--loaderhooktest` PASS、`--scantest` PASS、`--pagetest` PASS（复现并守住 101 场景）、`--addontest` PASS |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha6.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha6.zip` | source snapshot of this release |

SHA-256:

```
ae77c2eae04f6aac98957c57b849c3f14b4220e47b6812dcdffede7dcfb4e980  Pcl2Swap.Shim.x64.dll
22bd9dbf2d50734ebd6be01c9e4d5979a0cb8f3a29b92df380b054aabec96677  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → **Swap Addon**：三个内置模块 + 已安装模块，开关可用且跨重启保持
   （`%APPDATA%\PCL2Swap\modules.json`），每行显示钩子数/命中数，卡片列出钩子链。
3. 设置 → **Swap Settings**：标签列与官方设置页同一条网格线（x≈389）。
4. 离开设置页、再点顶部「设置」回来 —— 应该回到 Swap 页而不是弹出「未知的设置子页面种类」。
5. 补丁形状（可选，不需要 ExLoader）：把 dist 里的 `PclDlSwap.Loader.exe` 放到 PCL 目录，
   运行 `PclDlSwap.Loader.exe "Plain Craft Launcher 2.exe"`，它会生成
   `PclDlSwap.Patched.exe` 并启动；日志里应出现 `形状 Patched`。
6. 可选：`PCL2SWAP_SELFTEST=1`，重启后读 `%TEMP%\PCL2Swap\v26.0-Alpha6\pcl2swap.log`
   里的 `[selftest] 完成：ok=N fail=0`。

## Known Limitations

- 真正的“卸载”是**行为级**的：gate 保持在入口处并直通原方法，物理跳转随进程结束而消失。
- 结构类修复（原版游戏页重驱 / 下载侧栏恢复）是启动时的一次性修改，关闭模块要重启才完全回退。
- PageGet 钩子不可用时，重进设置页会退回宿主自己的子页（这是刻意的降级，不写未知 PageID）。
- 模块与宿主同进程共享 CLR：按定义是受信任代码。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
