# PCL2 Swap 注入器 / Injector

> Summary: the WinUI 3 injector that ships with every release and replaces hand-run ExLoader
> steps for people who just want to try Swap on an official PCL2 build.

---

## 1 它是什么

ExLoader 需要手动上传 `.dat`、手动选目标，且无法自动化。注入器把同一件事做成一个桌面应用：

- 列出当前 **可用的注入包**（GitHub Releases）与它们的 Release Note；
- **自动探测**运行中的 PCL2 进程，并按偏移清单判读版本是否受支持；
- 允许 **手动选择** PCL2 可执行文件，走「启动并注入」；
- 注入失败时 **一键用 gh 提交 Issue**（附目标指纹、失败阶段与 Swap 日志）。

它和 ExLoader 形态**共用同一个 shim**：注入器只是把
`Pcl2Swap.Shim.x64.dll` 通过 `LoadLibraryW` 送进目标进程，没有第二套补丁逻辑。

## 2 下载与运行

每个 Release 附带 `PCL2.dl.swap-Injector-<版本>.zip`，解压后双击
`Pcl2Swap.Injector.exe` 即可。压缩包是 **self-contained**：

- 内含 .NET 运行时与 Windows App SDK，用户不需要预装；
- 内含 `shim\`（与当版 ExLoader 包一致）和 `data\offset-manifest.json`；
- 断网也能对「随附版本」注入；其余版本按需从对应 Release 下载 `Pcl2Swap.Shim.x64.*`。

## 3 使用流程

### 3.1 注入已运行的 PCL2

1. 打开 PCL2，等主界面出现（CLR 未就绪时注入会失败）；
2. 打开注入器，点「刷新」；
3. 在左侧「目标」里选中带 **✓ 支持** 的进程；
4. 在右侧「注入包」里选择版本（默认最新）；
5. 点「注入到运行中的进程」。

成功后 PCL2 的 `设置` 侧栏会出现 `Swap Addon` 与 `Swap Settings`。

### 3.2 启动并注入

1. 点「选择 PCL2 可执行文件并启动注入…」；
2. 选择官方 PCL2 的 `Plain Craft Launcher 2.exe`；
3. 注入器会启动它、等窗口出现、再注入；
4. 底部按钮变为「启动并注入」，点击执行。

> 注入器不会修改 PCL2 文件本身，也不需要 PCL2 关闭。

### 3.3 兼容性判读

目标卡片上的状态来自 `data/offset-manifest.json`（与 payload 内嵌清单同源）：

| 状态 | 含义 |
|---|---|
| ✓ 支持：2.99.0.1（清单 v2990_1-rel415） | MVID/SHA-256 命中已验证过的宿主 |
| ⚠ 不在偏移清单中 | 可以尝试注入，但钩子按名称解析，结果可能不稳定 |
| ⚠ 无法读取 | 路径/哈希/MVID 读取失败，通常是权限问题 |

以管理员身份运行的 PCL2 需要以管理员身份运行注入器，否则 `OpenProcess` 会失败。

## 4 命令行模式

注入器也提供无窗口模式，便于脚本与 CI：

```powershell
# 列出偏移清单目标、运行中的进程、Releases、随附注入包
Pcl2Swap.Injector.exe --cli list

# 注入已运行进程
Pcl2Swap.Injector.exe --cli inject --pid 1234

# 启动并注入
Pcl2Swap.Injector.exe --cli inject --exe "C:\PCL2\Plain Craft Launcher 2.exe"

# 指定注入包版本（从 Release 下载或使用缓存）
Pcl2Swap.Injector.exe --cli inject --pid 1234 --release v26.1-Alpha3
```

输出同时写入 `%TEMP%\pcl2swap-cli-last.txt`（WinExe 没有自己的控制台，从脚本调用时以该文件为准）。
退出码：`0` 成功，`1` 参数/环境错误，`2` 注入失败。

## 5 失败时自动提交 Issue

注入失败会弹出对话框：

- **提交 Issue**：调用 `gh issue create` 到 `BlockConnectExp/PCL2.dl.swap`；
- **复制详情**：把完整诊断复制到剪贴板；
- 失败时诊断仍会保存到 `文档\PCL2Swap\reports\`。

诊断包含：注入器版本、系统版本、目标路径/版本/SHA-256/MVID、偏移清单匹配结果、
失败阶段、执行步骤与 Swap 日志尾部。请先确认 `gh auth status` 已登录。

## 6 与 ExLoader 的关系

| 通道 | 适用场景 | 维护方式 |
|---|---|---|
| ExLoader | 官方上架的卡片形态，用户自行上传 `.dat` | 每版手工更新（owner 执行） |
| 注入器 | 想直接用官方 PCL2 的用户、需要脚本化验证 | 随 Release 自动附带 |

两个通道的注入结果一致：同一个 shim、同一份 payload、同一套 Addon。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
