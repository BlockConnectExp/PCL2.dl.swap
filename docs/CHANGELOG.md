<p style="font-size:28px;text-align:center;"><b>PCL2.dl.swap ChangeLog</b></p>

> Records every released change set, newest first.

> [!NOTE]
> The artifact name carries both versions: `pcl2swap-{PCL2Ver.}-{Ver.}.dll`.

---
## 2026.10.31-06:00:00 v26.2-Alpha 1: 模块仓库与权限兼容修复

GitCommitHash: (unreleased)

ChangedFiles:
```
.\data\addons-index.json                          新文件：模块仓库索引（首条 PCL2Themes）
.\src\PclDlSwap.Payload\Swap\AddonRepository.cs   新文件：索引解析/下载/校验/安装
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs  模块仓库卡片与一键安装
.\src\PclDlSwap.Payload\Swap\AddonManager.cs      权限别名归一化；reflection 不再门控
.\docs\addon-repository.zh.md                     仓库格式与发布流程
.\docs\swap-api.md                                权限别名与反射说明
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块仓库|`data/addons-index.json` + 平台加载（默认 raw.githubusercontent / 失败回落内置索引）+ Swap Addon 页「Module repository」卡片（Install/Reinstall/Project/Refresh/Open index）|2026.10.31-06:00:00|PCL2.dl.swap
2|安装链路|一键安装：后台下载 → SHA-256 校验（不匹配删除）→ 正常安装器（信任链/权限/作用域照常）→ 刷新页面|2026.10.31-06:00:00|PCL2.dl.swap
3|权限别名|`host.hooks.attach`/`host.ui.pages`/`host.settings.write` 等归一化为平台权限名|2026.10.31-06:00:00|PCL2.dl.swap
4|反射门控|`Reflect`/`Surface` 不再要求 `reflection` 权限（同进程不可沙箱化，权限只约束副作用）|2026.10.31-06:00:00|PCL2.dl.swap
5|验证|索引解析/内置索引 ok；PCL2Themes 一键安装成功并完整运行（钩子/主题/backdrop/music，侧栏页面出现）；`ok=48 fail=0`|2026.10.31-06:00:00|PCL2.dl.swap

---
## 2026.10.31-05:20:00 v26.1: 正式版（Alpha 1-10 汇总）

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\I18n.cs                    版本号全角括号在英文模式下归一化
.\src\PclDlSwap.ExLoaderHarness\Program.cs             新增 zip-slip 防御断言
.\docs\RELEASE-NOTES-v26.1.md                          正式版汇总说明
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|zip-slip 防御|harness 新增断言：包内 `../evil.txt` 不会写出到模块目录之外（Unpack 的越界路径检查已被覆盖测试）|2026.10.31-05:20:00|PCL2.dl.swap
2|英文版本行|英文模式下把数字全角括号 `（415.50）` 归一化为 `(415.50)`，其余中文标点不动|2026.10.31-05:20:00|PCL2.dl.swap
3|正式发布|版本号升 v26.1（非 Alpha），Release 作为正式版发布；汇总 Alpha 1-10 能力与验证矩阵|2026.10.31-05:20:00|PCL2.dl.swap
4|验证|harness 六套件 PASS（含 zip-slip）；2.99.0.0/2.99.0.1 真机 `ok=45 fail=0`；Swap Settings 显示 Host version v26.1|2026.10.31-05:20:00|PCL2.dl.swap

---
## 2026.10.31-05:00:00 v26.1-Alpha 9: Addon 模板工程与生命周期修复

GitCommitHash: (unreleased)

ChangedFiles:
```
.\examples\demo-addon\                     新目录：可编译的完整示例 Addon
.\docs\addon-template.zh.md                模板使用文档
.\src\PclDlSwap.Payload\Swap\AddonManager.cs  更新暂存/序号目录修复
.\src\PclDlSwap.Payload\Swap\SwapHost.cs    E2E 卸载钩子独立化
.\src\PclDlSwap.Payload\Swap\SelfTest.cs    临时探针目录清理
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模板工程|`examples/demo-addon`：net48+WPF、参考 SDK、`build.ps1` 打包；覆盖两类页面/控件/双语/持久化/Clipboard/Shell/严格权限|2026.10.31-05:00:00|PCL2.dl.swap
2|更新修复|同版本更新时新归档被 Retire 删除 → 先复制到 `.installing` 暂存，卸载后落正式归档名|2026.10.31-05:00:00|PCL2.dl.swap
3|热更新修复|旧 DLL 被 CLR 锁定导致解包冲突 → 自动使用 `1.0.0-2/-3` 目录，旧目录排队延后删除|2026.10.31-05:00:00|PCL2.dl.swap
4|E2E 修复|`PCL2SWAP_E2E_UNINSTALL` 独立于安装分支执行，并刷新设置/更多两个侧栏|2026.10.31-05:00:00|PCL2.dl.swap
5|自检清理|权限探针创建的自检目录在 finally 中删除|2026.10.31-05:00:00|PCL2.dl.swap
6|验证|安装 launch #1 → 同版本更新 launch #2（目录 1.0.0-2，无异常）→ 卸载结果 True（102/200 注销）→ 重启清理后只剩内置模块；每轮 ok=45 fail=0|2026.10.31-05:00:00|PCL2.dl.swap

---
## 2026.10.31-04:30:00 v26.1-Alpha 8: 英文覆盖深化（帮助/关于/下载页）

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\I18n.cs   新增约 40 条词条与短语（帮助/关于/下载/常规设置/登录）
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|帮助页英文|Guides 三卡（Minecraft guide / Modpack guide / Resource install guide）及描述、Search help|2026.10.31-04:30:00|PCL2.dl.swap
2|关于页英文|开发者/协力开发者说明、Check for updates、Sponsor PCL!、Special thanks 鸣谢行与按钮（Sponsor mirror / Open wiki / MCIM homepage）|2026.10.31-04:30:00|PCL2.dl.swap
3|动态短语|最新正式版/预览版「发布于」行、Swap 卡描述（Version … · Download restore…）、当前版本行、BMCLAPI/MCIM 鸣谢行|2026.10.31-04:30:00|PCL2.dl.swap
4|常规词汇|常规/高级/主题/深色/浅色/正版登录/离线登录/用户名/密码/登录/更多设置/启动器设置/游戏设置|2026.10.31-04:30:00|PCL2.dl.swap
5|验证|2.99.0.1 真机：Help/About 页面英文化截图；I18n.Roundtrip 保持通过|2026.10.31-04:30:00|PCL2.dl.swap

---
## 2026.10.31-03:55:00 v26.1-Alpha 7: 权限 opt-in 严格门控

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\AddonManager.cs   权限策略 / ScopedPclAccess / 门控点
.\src\PclDlSwap.Payload\Swap\SelfTest.cs       3 条权限断言
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs  严格模式标注
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|权限策略|manifest.permissions 非空 → 严格模式；空 → 兼容模式；`<数据根>\security.json` 的 `"forceStrict":true` 可全局强制。未授权访问抛 UnauthorizedAccessException 并写入模块日志|2026.10.31-03:55:00|PCL2.dl.swap
2|门控点|`hooks`（Attach）、`pages`（两类页面）、`reflection`（Reflect/Surface）、`files`、`clipboard`、`network`、`logs`、`secret`、`launch`、`settings`、`instance`；Loader/Animation/Base/Download/Ui/I18n/Data/Log 透传|2026.10.31-03:55:00|PCL2.dl.swap
3|UI|模块行「（严格）」、详情「严格门控/兼容模式」、安装预览严格提示|2026.10.31-03:55:00|PCL2.dl.swap
4|验证|harness 六套件 PASS（无权限模块兼容回归）；真机 `ok=45 fail=0`；宽松=True、严格判定正确、Scoped 拦截=blocked|2026.10.31-03:55:00|PCL2.dl.swap

---
## 2026.10.31-03:20:00 v26.1-Alpha 6: 更多页槽位 + Shell/Clipboard 后端（API 1.2）

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\MorePageRegistry.cs     新文件：更多页槽位（PageOtherLeft detour + 侧栏）
.\src\PCL2Swap.Api\ISwapApi.cs                       IUi.RegisterMorePage
.\src\PCL2Swap.Api\IPclAccess.cs                     IPclShellAccess / IPclClipboardAccess
.\src\PclDlSwap.Payload\Swap\HostApi.cs              ApiVersion 1.2.0 + RegisterMorePage
.\src\PclDlSwap.Payload\Swap\PclAccess.cs            Shell/Clipboard 实现
.\src\PclDlSwap.Payload\Swap\AddonManager.cs         卸载时同步清理更多页
.\src\PclDlSwap.Payload\Swap\SelfTest.cs             4 条 API 1.2 断言 + 可选更多页自检
.\src\PclDlSwap.Payload\Swap\I18n.cs                 常规/高级/主题/登录等词条
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|更多页槽位|`IUi.RegisterMorePage`：detour `PageOtherLeft.PageCheck/PageGet`、复用宿主 `PageChangeRun`、PageID 200+ 段、单实例插入（`EnsureSidebarItems` 防重复）、卸载时与设置页一并清理|2026.10.31-03:20:00|PCL2.dl.swap
2|Shell/Clipboard 后端|`IPclAccess.Shell.OpenFolder/OpenFile`（ModBase.OpenExplorer 优先）、`IPclAccess.Clipboard.GetText/SetText`（ModBase.ClipboardGetText/Set 优先，WPF 回退）|2026.10.31-03:20:00|PCL2.dl.swap
3|英文扩充|新增常规设置与登录词汇（常规/高级/主题/深浅色/正版/离线登录/用户名/密码/登录等）|2026.10.31-03:20:00|PCL2.dl.swap
4|缺陷修复|修复更多页 watcher 每 500ms 重复插入侧栏项的问题（改为只补缺并检测宿主重建）|2026.10.31-03:20:00|PCL2.dl.swap
5|验证|harness 六套件 PASS（1.2.0）；2.99.0.1 真机 `ok=43 fail=0`；更多页 `Swap Selftest` 出现并可切换到 PageID 200 页面，本次会话仅插入一次|2026.10.31-03:20:00|PCL2.dl.swap

---
## 2026.10.31-02:45:00 v26.1-Alpha 5: PCL2 英文支持 + SDK API 1.1

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\II18n.cs                         新文件：SwapLanguage / II18n
.\src\PCL2Swap.Api\IPclAccess.cs                    新增 IPclUiAccess / IPclLogAccess
.\src\PCL2Swap.Api\ISwapApi.cs                      ISwapApi.I18n + IUi 四个新控件
.\src\PclDlSwap.Payload\Swap\I18n.cs                新文件：词典、短语表、语言持久化
.\src\PclDlSwap.Payload\Swap\HostTranslator.cs      新文件：WPF 视觉树运行时翻译
.\src\PclDlSwap.Payload\Swap\Views\SwapSettingsView.cs  语言卡与双语文案
.\src\PclDlSwap.Payload\Swap\Views\SwapUi.cs        Slider/TextInput/Link/Separator + 英文标签列宽
.\src\PclDlSwap.Payload\Swap\PclAccess.cs           Ui/Logs 实现
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs        侧栏 Info 双语
.\src\PclDlSwap.Payload\Swap\SelfTest.cs            8 条新断言
.\docs\i18n.zh.md                                   英文支持文档
.\docs\swap-api.md                                  §12 API 1.1
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|英文支持|运行时翻译层：语言设置（Auto/中文/English，存 `<数据根>\ui.json`）、约 110 条内置词典、磁盘覆盖 `<数据根>\i18n\en-US.json`、动态短语表；`HostTranslator` 翻译 TextBlock/Run、只读 TextBox、RichTextBox、Content/Header/Title/ToolTip 与 `Text`/`Info` 属性，切回中文可逆|2026.10.31-02:45:00|PCL2.dl.swap
2|Swap 页面双语|Swap Settings 新增 Language 卡（切换 + Translate now + Restore original Chinese）；页面、宿主信息、查毒策略、目录卡片与侧栏 Info 全部双语；英文时标签列宽 110→150 避免截断|2026.10.31-02:45:00|PCL2.dl.swap
3|SDK API 1.1|新增 `ISwapApi.I18n`（T/Register/SetLanguage/IsEnglish）、`IPclAccess.Ui`（CurrentPage/MainWindow/FindPage/Translate/Restore）、`IPclAccess.Logs`（LogPath/Tail）与 `IUi` 四个控件（Slider/TextInput/Link/Separator）；仅加成员，1.0 addon 兼容|2026.10.31-02:45:00|PCL2.dl.swap
4|验证|harness 六套件 PASS（示例 addon 读到 1.1.0）；2.99.0.1 真机 `ok=38 fail=0`；I18n 往返 `EN→Host information / ZH→宿主信息`；主界面、设置页、Swap Settings、侧栏 Info、启动日志行均实测英文|2026.10.31-02:45:00|PCL2.dl.swap

---
## 2026.10.31-01:30:00 v26.1-Alpha 4: 注入器瘦身与 CLI 失败诊断

GitCommitHash: (unreleased)

ChangedFiles:
```
.\tools\Pcl2Swap.Injector\Pcl2Swap.Injector.csproj  改用最小 WASDK 子包（WinUI + Runtime）
.\tools\Pcl2Swap.Injector\Cli.cs                    注入失败时输出/保存诊断
.\scripts\build.ps1                                 新增 -DistDir
.\src\Pcl2Swap.Shim\build.ps1                       新增 -OutDir
.\scripts\build-injector.ps1                        新增 -DistDir
.\src\Pcl2Swap.Shim\Shim.cpp                        版本号升 v26.1-Alpha 4
.\src\PclDlSwap.Loader\Program.cs                   版本号升 v26.1-Alpha 4
.\src\PclDlSwap.Payload\Host\SwapLog.cs             版本号升 v26.1-Alpha 4
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|注入器瘦身|不再引用 `Microsoft.WindowsAppSDK` 元包，只引用 `Microsoft.WindowsAppSDK.WinUI` + `.Runtime`，移除 onnxruntime/DirectML/semantic-index 等约 62 MB AI 组件；解压输出 225.6→163.5 MB，发布 zip 90.7→66.0 MB|2026.10.31-01:30:00|PCL2.dl.swap
2|CLI 失败诊断|`--cli inject` 失败时打印完整诊断段落并保存到 `文档\PCL2Swap\reports\`，与 GUI 失败对话框共用 `Diagnostics` 生成逻辑；退出码保持 0/1/2|2026.10.31-01:30:00|PCL2.dl.swap
3|并行构建|三个构建脚本新增输出目录参数（`-DistDir` / `-OutDir`），并行会话的注入实例锁住 `dist\exloader` 时不再互相破坏；`.gitignore` 忽略 `dist-*/`|2026.10.31-01:30:00|PCL2.dl.swap
4|验证|harness 六套件全 PASS；2.99.0.0 与 2.99.0.1 真机启动并注入均 `ok=30 fail=0`（日志 v26.1-Alpha4）；瘦身后 GUI/CLI 正常；CLI 无效 PID 失败诊断 `exit=2` 且报告落盘|2026.10.31-01:30:00|PCL2.dl.swap

---
## 2026.10.31-02:20:00 v26.1-Alpha 3: WinUI 注入器 + 2.99.0.1 全量回归

GitCommitHash: (unreleased)

ChangedFiles:
```
.\tools\Pcl2Swap.Injector\                         新项目：WinUI 3 注入器（GUI + CLI）
.\scripts\build-injector.ps1                       注入器打包脚本（self-contained + shim 种子）
.\docs\injector.zh.md                              注入器使用说明
.\docs\compat-matrix.zh.md                         宿主兼容矩阵（2.99.0 / 2.99.0.1）
.\docs\versioning.md                               Release Checklist 增加注入器条目
.\src\Pcl2Swap.Shim\Shim.cpp                       版本号升 v26.1-Alpha 3
.\src\PclDlSwap.Loader\Program.cs                  版本号升 v26.1-Alpha 3
.\src\PclDlSwap.Payload\Host\SwapLog.cs            版本号升 v26.1-Alpha 3
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|注入器（目标）|自动枚举运行中的 PCL2，读取文件版本/MVID/SHA-256 并按 `offset-manifest.json` 判定兼容性；支持手动选择可执行文件走「启动并注入」|2026.10.31-02:20:00|PCL2.dl.swap
2|注入器（注入包）|通过 `gh api` 拉取 Releases 与 Release Note（轻量 markdown 渲染含图片）；随附 shim 优先、离线可用，其它版本按需下载并缓存到 `%LOCALAPPDATA%\PCL2Swap\Injector\shims\<tag>`|2026.10.31-02:20:00|PCL2.dl.swap
3|注入器（注入）|复用 shim 的 `LoadLibraryW` 注入；启动路径等待主窗口出现后再注入以规避 CLR 未就绪；注入后轮询 `%TEMP%\PCL2Swap\*\pcl2swap.log` 验证「宿主匹配」|2026.10.31-02:20:00|PCL2.dl.swap
4|注入器（失败）|失败对话框支持「提交 Issue」（`gh issue create`，附目标指纹/失败阶段/日志尾部）与「复制详情」；gh 不可用时保存到 `文档\PCL2Swap\reports\`|2026.10.31-02:20:00|PCL2.dl.swap
5|注入器（CLI）|`--cli list` / `--cli inject --pid/--exe [--release/--shim] [--wait]`；退出码 0/1/2，输出 tee 到 `%TEMP%\pcl2swap-cli-last.txt`|2026.10.31-02:20:00|PCL2.dl.swap
6|注入器（打包）|`scripts\build-injector.ps1` 产出 `PCL2.dl.swap-Injector-<ver>.zip`，内含 self-contained 运行时、`shim\`（与 ExLoader 同源）和 `data\offset-manifest.json`|2026.10.31-02:20:00|PCL2.dl.swap
7|兼容矩阵|`docs\compat-matrix.zh.md` 记录 2.99.0.0（413）/ 2.99.0.1（415）的 MVID、验证状态与 2.99.0.1 四处历史坑位的修复方式|2026.10.31-02:20:00|PCL2.dl.swap
8|验证|2.99.0.1 真机：注入自检 `ok=30 fail=0`；下载页侧栏恢复/关于页品牌卡/反馈分流/Swap Addon 与 Swap Settings 子页全部通过；注入器「运行中注入」与「启动并注入」两条路径均成功并完成日志验证|2026.10.31-02:20:00|PCL2.dl.swap

---
## 2026.10.29-03:35:00 v26.1-Alpha 2: 包信任链（签名 + 负载哈希 + 版本级告警）

GitCommitHash: (unreleased)

ChangedFiles:
```
.\src\PCL2Swap.Api\AddonManifest.cs                  新增 AddonFileEntry 与 manifest.Files
.\src\PclDlSwap.Payload\Swap\PackageTrust.cs         新文件：信任链校验（签名、受信任密钥、文件清单）
.\src\PclDlSwap.Payload\Swap\AddonManifestReader.cs  files 清单解析
.\src\PclDlSwap.Payload\Swap\AddonManager.cs         Review/信任策略、负载摘要、降级与换包告警、启动时校验
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs  预览带信任状态；详情新增「签名状态」行
.\src\PclDlSwap.Payload\Swap\SwapHost.cs             e2e 预览钩子改用 Review（带信任）
.\tools\PclAddonPack\Program.cs                      --gen-key / --sign、files 清单、signature.json
.\src\PclDlSwap.ExLoaderHarness\Program.cs           9 项信任链断言
.\docs\swap-api.md                                   manifest files + §11.1 信任链
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader    版本号升 v26.1-Alpha 2
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|包信任链|`signature.json` 的 RSA-SHA256 签名覆盖 `manifest.json` 原始字节，清单里带每个负载文件的 SHA-256；受信任公钥放在 `<数据根>\trusted-keys\*.xml`，keyId 由公钥 XML 的 SHA-256 前 16 位派生（打包器与宿主算法一致，不需要额外 id 注册表）|2026.10.29-03:35:00|PCL2.dl.swap
2|信任策略|`Trusted`（受信任密钥 + 文件全对）与 `Unsigned`（老包，有清单仍校验）/`UnknownKey`（签名但密钥不受信任）放行但提示；`InvalidSignature` 与 `TamperedFiles`（缺文件、哈希不符、**出现未列入清单的 dll**）**拒绝安装**；启动扫描时同一校验会把被篡改的已装模块标为失败且不启用|2026.10.29-03:35:00|PCL2.dl.swap
3|版本级告警|安装预览新增：降级安装（已装 X → 包内 Y）提示；同一 id+版本但负载清单摘要不一致时提示「包内容与已安装的不一致」（用负载摘要而不是 zip 文件哈希，避免重新打包就误报）|2026.10.29-03:35:00|PCL2.dl.swap
4|打包器|`--gen-key <私钥.xml>` 生成 RSA-2048 密钥对（同时输出 `.pub.xml` 与 keyId）；`--sign <私钥.xml>` 自动写入 files 清单并签名，输出里打印签名状态|2026.10.29-03:35:00|PCL2.dl.swap
5|界面|安装预览新增「信任状态」行；模块详情新增「签名状态」行（含密钥 id 与文件校验结果）|2026.10.29-03:35:00|PCL2.dl.swap
6|验证|harness 新增 9 项断言（老包 Unsigned、受信任签名 Trusted、安装记录负载摘要、未知密钥 UnknownKey 且不阻塞、篡改负载 TamperedFiles、篡改包拒绝安装、降级告警、同版本换内容告警、预览打印信任状态）全部 PASS，六套件全绿；真机 2.99.0：用 `--gen-key`/`--sign` 生成的包在把公钥放进 `trusted-keys` 后，预览显示「已签名（密钥 2487732295f39f81），文件校验通过（1 个）」，安装后详情页同样显示，模块正常启用|2026.10.29-03:35:00|PCL2.dl.swap

---
## 2026.10.28-21:45:00 v26.1-Alpha 1: 模块详情视图 + 钩子注册清理

GitCommitHash: tag v26.1-Alpha1（发布说明见 docs/RELEASE-NOTES-v26.1-Alpha1.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\AddonManager.cs         审计字段（来源/哈希/时间）、Selected/LogTail/GetModuleConfigPath/Reload
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs           RemoveAll：清理一个模块的全部钩子注册
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs  详情卡片 + 每行「详情」按钮 + 紧凑动作条
.\src\PclDlSwap.ExLoaderHarness\Program.cs           9 项新断言（审计、日志尾部、配置路径、选中态、重载、注册清理）
.\docs\swap-api.md                                   §11 详情视图与重载语义
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader     版本号升 v26.0-Alpha 12
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块详情视图|每行的「详情」按钮展开该模块的管理页：状态/id/版本/**安装时间**/**来源包**/**包 SHA-256**/模块目录/配置文件/API 范围/入口/作者/形状与宿主作用域/权限声明/钩子数（命中、抛错）/钩子链/模块日志尾部，以及 `禁用\|启用`、`重载`、`打开日志`、`打开配置目录`、`收起`、`卸载` 六个动作；安装时记录来源与哈希，启动时发现的模块以包文件为来源与时间|2026.10.28-21:45:00|PCL2.dl.swap
2|钩子注册清理（缺陷修复）|卸载/更新后模块的钩子注册原本留在链上——**未知 addon id 被当成"已启用"**，于是已卸载模块的 handler 会继续执行；`ScopedHook.RemoveAll` 现在在 Retire 与 Reload 时清空该模块的全部注册（重载同时避免上一代 Prefix handler 叠加执行）|2026.10.28-21:45:00|PCL2.dl.swap
3|重载动作|非内置模块可一键 `重载`：OnDisable → 平台清理该模块遗留注册 → OnLoad/OnEnable；内置模块拒绝重载并提示用开关|2026.10.28-21:45:00|PCL2.dl.swap
4|界面细节|详情动作条按钮改用 100px 紧凑宽度（默认 140 会撑出卡片）；开关/卸载仍走原生确认框|2026.10.28-21:45:00|PCL2.dl.swap
5|验证|harness 新增 9 项断言（审计三元组、日志尾部、配置路径、选中态记忆、重载成功且仍启用、重载清掉上一代注册、内置模块拒绝重载、卸载后注册归零等）全部 PASS，六个套件全绿；真机 2.99.0：详情页完整渲染（作用域/权限/钩子链/日志尾部/六按钮同一行），点「重载」日志实录 `Addon 重载：demo.scope` → `模块钩子已移除（链路已空）` → `加入既有钩子链` → `Addon 已启用`|2026.10.28-21:45:00|PCL2.dl.swap

---
## 2026.10.28-19:40:00 v26.1-Alpha 1: 安装前预览（信任审查）

GitCommitHash: tag v26.1-Alpha1（与上一条同属 v26.1-Alpha 1；原先的 v26.0-Alpha11 预发布已并入本次发布）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\AddonManager.cs          Inspect / DescribeForReview / ShortHash / 作用域判定按 manifest 重载
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs   安装流程改为「选包 → 预览确认 → 安装/更新」
.\src\PclDlSwap.Payload\Swap\SwapHost.cs              e2e 预览钩子（PCL2SWAP_E2E_PREVIEW）
.\src\PclDlSwap.ExLoaderHarness\Program.cs            4 项预览断言（可检视、不落盘、含 API/哈希、含权限）
.\docs\swap-api.md                                    §11 安装语义 + e2e 环境变量
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader     版本号升 v26.0-Alpha 11
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|安装前预览|`AddonManager.Inspect` 在不落盘的前提下读包（同样的可读校验错误）；`DescribeForReview` 生成信任审查文本：名称/id/版本/API 范围/入口/作者/说明/形状作用域/宿主版本作用域/权限声明/包大小/SHA-256，作用域不符时追加「安装后不会启用」；已有同 id 时标题与文案切换为「更新（旧 → 新）」|2026.10.28-19:40:00|PCL2.dl.swap
2|确认流程|「安装模块…」改为 选包 → 宿主原生 MyMsgBox 预览（按钮「安装」/「更新」与「取消」）→ 确认后才归档与解包；取消路径写日志（`安装模块已取消（预览阶段）`）|2026.10.28-19:40:00|PCL2.dl.swap
3|e2e 支撑|新增 `PCL2SWAP_E2E_PREVIEW=<包>`：启动 9 秒后在宿主 UI 线程弹出同一条预览对话框，脚本可点按钮并核对返回码（1=确认，2=取消），用于真机验证预览 UI|2026.10.28-19:40:00|PCL2.dl.swap
4|验证|harness：新增「可预览未安装的包」「预览不触碰模块目录」「预览含 API 范围与 SHA-256」「预览含权限声明」4 项断言，六套件全绿；真机 2.99.0：预览对话框原生渲染，完整列出 `形状作用域 injected、patched / 宿主版本 >=2.99.0 / 权限 host.hooks.attach、host.settings.write` 与包 SHA-256，点「取消」日志记录 `安装预览返回：2`|2026.10.28-19:40:00|PCL2.dl.swap

---
## 2026.10.28-18:55:00 Alpha 10 → 正式版 v26.0: 宿主版本作用域 + 安装前兼容预检

GitCommitHash: tag v26.0（正式发布，发布说明见 docs/RELEASE-NOTES-v26.0.md；预发布标签 v26.0-Alpha10 保留溯源）

ChangedFiles:
```
.\src\PCL2Swap.Api\AddonManifest.cs                 新增 hostVersions
.\src\PCL2Swap.Api\ISwapApi.cs                      IHostInfo.PclVersion
.\src\PclDlSwap.Payload\Swap\AddonManifestReader.cs hostVersions 解析（含 hostVersion 单数、单字符串简写）
.\src\PclDlSwap.Payload\Swap\HostApi.cs             PclVersion 读宿主 ProductVersion/FileVersion
.\src\PclDlSwap.Payload\Swap\AddonManager.cs        作用域统一判定 + HostVersionMatches + 安装预检
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs 作用域行显示宿主版本 + 换行不截断
.\src\PclDlSwap.Payload\Swap\SelfTest.cs            Host.PclVersion 与作用域判定探针
.\src\PclDlSwap.ExLoaderHarness\Program.cs          6 项新断言（PclVersion、匹配/拒绝/通配、跳过不失败）
.\tools\PclAddonPack\Program.cs                     --shapes / --host-versions / --permissions
.\docs\swap-api.md                                  manifest 作用域说明 + §11
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader    版本号升 v26.0-Alpha 10
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|宿主版本作用域|manifest 新增 `hostVersions`（LSPosed 的「作用域」在单应用下的对应物）：每项互相 OR，支持 `"*"`、精确版本 `"2.99.0.0"` 与比较式 `">=2.99.0"` / `"<3.0"` / `"=2.99.0.1"`，可带 `-beta`/`+413` 后缀（归一化后再比较）；留空=任意宿主（Alpha 9 行为）。不匹配时模块**不启动**、状态「宿主版本不适用」，不记为失败|2026.10.28-18:55:00|PCL2.dl.swap
2|安装前预检|安装时先算作用域：不匹配仍然允许安装（用户可能在为另一个 PCL 版本备包），但立刻把「模块声明 X，当前宿主 Y」写进日志，安装提示也带状态；`apiVersion` 仍是硬拒绝|2026.10.28-18:55:00|PCL2.dl.swap
3|宿主版本暴露|`IHostInfo.PclVersion` 读宿主程序集的 `ProductVersion`（PCL 为 2.99.0.0），退回 `FileVersion`/`AssemblyVersion`；自检新增 `Host.PclVersion` 与「模块作用域判定」两项探针|2026.10.28-18:55:00|PCL2.dl.swap
4|打包器|`PclAddonPack` 新增 `--shapes` / `--host-versions` / `--permissions`，生成的 manifest.json 直接带上作用域与权限声明（原先只能手改包内 JSON）|2026.10.28-18:55:00|PCL2.dl.swap
5|界面|模块行作用域文本改为换行显示，不再被按钮挤到截断；列出 `API 范围 · 形状 · 宿主 · 权限名`|2026.10.28-18:55:00|PCL2.dl.swap
6|验证|harness 新增 6 项断言（PclVersion 非空、同版本范围命中、超范围拒绝、通配放行、宿主版本不匹配的模块状态为 skipped-host）全部 PASS，六个套件全绿；真机 2.99.0：用打包器生成 `shapes=[injected,patched] hostVersions=[">=2.99.0"] permissions=[…]` 的包 → 安装即启用（`状态=enabled`），自检 `Host.PclVersion = 2.99.0.0`、`模块作用域判定 = 空约束=True，*=True，精确匹配=True，">=99.0"=False`|2026.10.28-18:55:00|PCL2.dl.swap

---
## 2026.10.28-17:45:00 Alpha 9: 模块日志 + 崩溃隔离

GitCommitHash: tag v26.0-Alpha9（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha9.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\ModuleLog.cs            新文件：每模块日志（256 KB 轮转）
.\src\PclDlSwap.Payload\Swap\ScopedHook.cs           handler 抛错 → 自动停用 + 计数 + 写模块日志
.\src\PclDlSwap.Payload\Swap\AddonManager.cs         ScopedApi/ScopedHooks 写模块日志；卸载连日志一起删
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs  每行「日志」按钮 + 权限名展示 + 钩子抛错提示
.\src\PclDlSwap.ExLoaderHarness\Program.cs          4 项新断言（模块日志、挂钩记录、抛错记录、隔离回退）
.\docs\swap-api.md                                  §11 日志 / 崩溃隔离 / 权限声明
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader   版本号升 v26.0-Alpha 9
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|每模块日志|新增 `ModuleLog`：`Api.Log/LogWarning/LogError` 与平台代挂的钩子都写进 `<数据根>\logs\modules\<id>.log`（模块 id 命名空间、256 KB 轮转保留 `.1`），全局日志同一行带 `[<模块id>]` 前缀；模块行新增「日志」按钮直接打开文件（还没有日志时打开目录并提示）；卸载会把该模块的日志一并删除|2026.10.28-17:45:00|PCL2.dlSwap
2|崩溃隔离|模块 handler 抛异常时 `Registration.Call` 捕获并**自动停用该 handler**，Replace 模式回退到下一个 Replace handler 或宿主原方法（原先 `DynamicInvoke` 直接把异常抛进宿主，PCL 会弹「程序出现未知错误」）；失败计数进模块列表，模块行显示红色「钩子抛错 N 次（失败的钩子已自动停用，详见该模块日志）」|2026.10.28-17:45:00|PCL2.dlSwap
3|权限声明可见|模块行的作用域行不再只显示权限数量，改为列出最多三个权限名（`权限 host.settings.write、host.hooks.attach`）；文档同时写明：同进程共享 CLR，`permissions` 是声明与展示而非沙箱边界|2026.10.28-17:45:00|PCL2.dlSwap
4|验证|harness：新增「模块各自写自己的日志」「日志记录平台代挂的钩子」「日志记录 handler 抛错」「抛错后回退宿主原方法且不再重试」4 项断言全部 PASS，六个套件全绿；真机 2.99.0：安装示例模块后 `logs\modules\test.sample.log` 实录「示例模块已加载…」「挂钩 PCL.ModBase.ToUnixTimestampUtc（Prefix…）」，界面每行多出「日志」按钮|2026.10.28-17:45:00|PCL2.dlSwap

---
## 2026.10.28-16:05:00 Alpha 8: 加载器观察面 + 依赖遮蔽修复

GitCommitHash: tag v26.0-Alpha8（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha8.md）

ChangedFiles:
```
.\src\PCL2Swap.Api\IPclAccess.cs                  Loader 族新增 GetName/Weight/Error/Watch/CreateCombo/ListTasks/ListAll
.\src\PclDlSwap.Payload\Swap\PclAccess.cs         LoaderWatch + DelegateBridge（值类型事件桥）+ 组合/任务面实现
.\src\PclDlSwap.Payload\Swap\SelfTest.cs          真机加载器探针（真事件写日志；空组合自驱动）
.\src\PclDlSwap.Payload\Host\ExLoaderEntry.cs     预载解包目录的运行时程序集，修宿主目录旧同名 DLL 遮蔽
.\src\PclDlSwap.Loader\Program.cs                 运行时文件按内容哈希比较（不再看时间戳）
.\src\PclDlSwap.ExLoaderHarness\Program.cs        Loader 桩补事件/权重/任务栏注册表 + 11 项断言
.\docs\swap-api.md                                §10 Loader 族 + §10.1 任务与进度
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader  版本号升 v26.0-Alpha 8
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|加载器观察面|`Api.Pcl.Loader` 新增 `Watch(loader, onProgress, onStateChanged)` / `WatchPreviewFinish`：直接订阅宿主自己的 `ProgressChanged` 与 `OnStateChangedUi` 事件（**不是轮询**），先用当前值各触发一次，加载器结束（Finished/Failed/Canceled）时自动解除订阅，句柄 `Dispose` 也能提前解除。宿主事件参数是值类型（`LoadState` 枚举、`double`），模块侧只写 `Action`，平台用一个编译好的适配委托装箱转交|2026.10.28-16:05:00|PCL2.dlSwap
2|组合与任务面|新增 `CreateCombo(name, loaders, inputType)`（用宿主自己的 `LoaderCombo<TInput>`，任务栏进度与取消保持宿主实现）、`ListTasks`（组合走实例 `GetLoaderList`，普通任务走宿主静态递归走查）、`ListAll`（`ModLoader.LoaderTaskbar` 注册表，可按运行中过滤）、`GetName` / `GetProgressWeight` / `SetProgressWeight` / `GetError`|2026.10.28-16:05:00|PCL2.dlSwap
3|依赖遮蔽修复（现场级）|宿主目录里若留着上次磁盘补丁形状复制过去的 `PCL2Swap.Api.dll`，CLR 会先绑它、跳过 shim 内嵌的同名程序集 —— 真机表现为 `MissingMethodException：找不到方法 ListAll`。现在 `ExLoaderEntry.Activate` 先把解包目录的 `PCL2Swap.Api` / `Gee.External.Capstone` / `MonoMod.Utils` / `Mono.Cecil` 预载定版；`EnsureRuntimeFiles` 也改成按 SHA-256 内容比较（原来同大小同长度的旧文件会被误判为“已就位”）|2026.10.28-16:05:00|PCL2.dlSwap
4|验证|harness `--pcltest` 新增 11 项断言（Watch 播种/收事件/Dispose 停药/终态自动解绑、PreviewFinish、GetName、权重往返、CreateCombo、ListTasks、ListAll 及运行中过滤）全部 PASS；真机 2.99.0 自检 `ok=28 fail=0`，日志实录真实事件 `→ Waiting`、`→ Loading`、`PreviewFinish`、`→ Finished`；空 `LoaderCombo<object>` 由平台自建并启动，未触碰宿主既有加载器|2026.10.28-16:05:00|PCL2.dlSwap

---
## 2026.10.28-13:45:00 Alpha 7: 模块管理器（安装/卸载/作用域，无需重启）

GitCommitHash: tag v26.0-Alpha7（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha7.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\AddonManager.cs          Install / Uninstall / Retire / 延迟删除 / 形状作用域 / ScopedUi
.\src\PclDlSwap.Payload\Swap\AddonManifestReader.cs   shapes / permissions 数组解析修复
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs          UnregisterPage / RefreshSidebar / 卸载后切回宿主页
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs   模块管理卡片 + 每行卸载按钮 + 作用域行
.\src\PclDlSwap.Payload\Swap\SwapHost.cs              e2e 场景钩子（PCL2SWAP_E2E_INSTALL / _UNINSTALL）
.\src\PclDlSwap.ExLoaderHarness\Program.cs            --addontest 新增 10 项管理器断言
.\docs\swap-api.md                                    §11 模块管理 + 限制条目更新
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader    版本号升 v26.0-Alpha 7
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块管理器|`AddonManager` 新增 `Install` / `Uninstall`：安装 = 校验包 → 归档到 `<数据根>\addons\<id>-<版本>.swapaddon` → 解包 → 立即 `OnLoad/OnEnable` 并重建设置侧栏（**无需重启 PCL**）；同 id 再装即热更新，旧版先停并撤下页面，模块自己的 `config.json` 保留；卸载 = `OnDisable/OnUnload` → 注销页面 → 删除模块目录（含 config）与归档包，用户 Downloads 里的原包不动|2026.10.28-13:45:00|PCL2.dl.swap
2|延迟删除|入口 DLL 被 CLR 占用时 `Directory.Delete` 必然失败：现在把路径写进 `<数据根>\addons\.pending\*.txt`，下次启动 `CleanupPendingRemovals` 先清理再扫描模块，`.pending` 清空后自删。真机验证：卸载后日志出现「Addon 文件暂被占用，将在下次启动删除」，重启后「待删除模块文件已清理完成」，`test.sample\1.3.0` 与包文件消失|2026.10.28-13:45:00|PCL2.dl.swap
3|页面注销|`PageRegistry` 新增 `UnregisterPage` / `RefreshSidebar`：卸载模块时只撤下它自己注册的设置页（`ScopedUi` 把页面 id 命名空间化成 `<模块id>/<页id>` 并记在记录上），侧栏重建；若用户正停留在被卸载的页面，先切回宿主默认设置页；页面 id 不复用，残留 PageID 由 PageGet 兜底处理|2026.10.28-13:45:00|PCL2.dl.swap
4|作用域（shapes）|`manifest.json` 的 `shapes` 现在真正生效：声明 `["injected"]` 的模块在补丁形状下状态是「作用域不适用」，既不启动也不记为失败；`*` 表示全形状。**同时修掉一个老 bug**：`AddonManifestReader` 用 `Convert.ToString` 取数组，`shapes`/`permissions` 实际永远是空数组（数组被转成 `System.Object[]`），改用原始值解析|2026.10.28-13:45:00|PCL2.dl.swap
5|管理界面|Swap Addon 页新增「模块管理」卡片（安装模块… / 打开 Addon 目录 / 刷新）；每行显示 `作者 · API 版本范围 · 形状 · 权限数`，第三方模块多一个红色「卸载」按钮，确认框走宿主 MyMsgBox|2026.10.28-13:45:00|PCL2.dl.swap
6|验证|harness `--addontest` 新增 10 项断言（安装注册并启动、归档到模块目录、原包保留、拒绝无 manifest 的包、形状不匹配只跳过、卸载从列表剔除、删包、删除或排队、原包保留、内置模块拒绝卸载）全部 PASS；真机 2.99.0：`PCL2SWAP_E2E_INSTALL` 热装 `demo.sample` → 页面即时出现并可打开、侧栏变成 3 项，界面点「卸载」→ 确认框 → 行消失、侧栏回到 2 项、`modules.json` 剔除该模块|2026.10.28-13:45:00|PCL2.dl.swap

---
## 2026.10.28-08:35:00 Alpha 6: 设置子页面加固 + 页面度量对齐原生 + 补丁形状落地

GitCommitHash: tag v26.0-Alpha6（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha6.md）

ChangedFiles:
```
.\src\PclDlSwap.Payload\Swap\PageRegistry.cs        PageID 写入守卫 / 侧栏前置校验 / STA 化视图构建 / 未知 id 兜底
.\src\PclDlSwap.Payload\Swap\Views\SwapUi.cs        行网格固定标签列、卡片内边距、注释行距、按钮间距、下拉框高度
.\src\PclDlSwap.Payload\Swap\Views\SwapAddonView.cs 模块行重排（状态右对齐 + 35px 按钮）与钩子链两行式卡片
.\src\PclDlSwap.Payload\Host\CoreInstaller.cs       using System.Diagnostics（ActivateFromPatch 编译修复）
.\src\PclDlSwap.Payload\Host\Bootstrap.cs           两种形状都启动 SwapHost；disk-patch 走 ActivateFromPatch
.\src\PclDlSwap.Patcher\IlPatcher.cs                RewriteRuntimeBootstrap：ModMain..cctor 末尾注入 Bootstrap("disk-patch")
.\src\PclDlSwap.Loader\Program.cs                   EnsureRuntimeFiles：把 payload 及依赖复制到补丁 exe 目录
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|设置子页加固|「未知的设置子页面种类：101」只剩一个成因：宿主 `PageSetupLeft.PageGet` 的钩子没装上、而我们把 Swap 的 PageID 写进了宿主。现在 PageGet 未就绪时**不再写 PageID**（重进设置页退回宿主自己的子页），PageCheck 未就绪时**不插入侧栏项**（点了必然掉进宿主未知分支），PageGet 遇到任何宿主枚举外的 id 一律兜底成 `Default`，视图工厂在非 STA 线程时改走宿主 Dispatcher|2026.10.28-08:35:00|PCL2.dl.swap
2|页面度量对齐|Swap Settings 的值列原本落在 x≈527，官方 `PageSetupLaunch` 的值列在 x≈389：原因是 `SharedSizeGroup` 在整页作用域里被最长标签（`VirusTotal / MetaDefender Cloud`）撑开。改为固定 110px 标签列 + 24px 间距；卡片内边距改成官方 `20/40/18` + 18px 尾距，注释行距 18px，按钮间距 12px，下拉框高度 35px|2026.10.28-08:35:00|PCL2.dl.swap
3|模块列表|Swap Addon 行改为 9px 上下留白 + 72px 状态列（右对齐，启用态用主题色）+ 35px 原生按钮；钩子链从挤在一行的「·」文本改成「目标方法 / 模块 · 模式 · 命中」两行块|2026.10.28-08:35:00|PCL2.dl.swap
4|补丁形状落地|`PclDlSwap.Loader.exe` 打出的磁盘补丁 exe 现在真的会启动运行时：`ModMain..cctor` 末尾注入 `Bootstrap("disk-patch")`，SwapHost 以 Patched 形状启动（模块平台 + 设置页 + 事件总线都在），随后 `CoreInstaller.ActivateFromPatch` 只补 IL 表达不了的 UI 功能（下载页恢复 / 公告取消 / 反馈分流 / 关于页品牌行）；Loader 会把 payload 与依赖复制到补丁 exe 自己的目录|2026.10.28-08:35:00|PCL2.dl.swap
5|验证|注入形状真机（2.99.0 同源副本）：侧栏与两页正常、离开设置再回来由 PageGet 命中恢复、模块开关落盘 `modules.json`；补丁形状真机：`SwapHost 启动（形状 Patched）`+「磁盘补丁装配完成」，两页与开关同样正常；harness `--loaderhooktest / --scantest / --pagetest / --addontest` 全部 PASS；偏移清单 RVA 校验 PASS|2026.10.28-08:35:00|PCL2.dl.swap

---
## 2026.10.28-06:20:00 Alpha 6: out-of-the-box module settings

GitCommitHash: tag v26.0-Alpha6（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha6.md）

ChangedFiles:
```
.\src\PCL2Swap.Api\AddonSettings.cs                  new（IData 的类型化读写 + 自动落盘）
.\src\PCL2Swap.Api\ISwapApi.cs                       IUi + CheckBox / Combo
.\src\PclDlSwap.Payload\Swap\Views\SwapUi.cs         MyCheckBox / MyComboBox 构建（含 WPF 回退）
.\src\PclDlSwap.Payload\Swap\HostApi.cs              UiImpl 转发
.\src\PCL2Swap.SampleAddon\SampleAddon.cs            演示模块新增「模块设置（自动持久化）」卡片
.\src\PclDlSwap.ExLoaderHarness\Program.cs           UI 构建 + AddonSettings 断言
.\docs\swap-api.md                                   §6.2 模块设置项
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|模块设置构件|`IUi` 新增 `CheckBox`（宿主 MyCheckBox，其 Change 事件回读控件状态）与 `Combo`（宿主 MyComboBox + MyComboBoxItem，回调回传索引）；宿主缺控件时退回标准 WPF，语义不变。两张卡片 + 两行代码就能得到与官方设置页一致的模块设置区|2026.10.28-06:20:00|PCL2.dl.swap
2|AddonSettings|SDK 新增 `AddonSettings`：`Get/SetBool/Int/Double/String` 针对 `Api.Data` 的类型化读写，每次写入自动 `Save()` 到模块自己的 config.json|2026.10.28-06:20:00|PCL2.dl.swap
3|真机验证|示例模块新增设置卡片：勾选框与下拉框都能改、立即生效并持久化——真机 2.99.0 实测 config.json 出现 `demo.countHook=false`、`demo.refreshIndex=1`，日志打印「示例模块设置：刷新频率 = 2 秒」；harness 断言 UI 构建器（STA）与 AddonSettings 往返全部通过|2026.10.28-06:20:00|PCL2.dl.swap

---
## 2026.10.28-06:00:00 Alpha 6: native-look page builders in the SDK

GitCommitHash: tag v26.0-Alpha6（发布说明见 docs/RELEASE-NOTES-v26.0-Alpha6.md）

ChangedFiles:
```
.\src\PCL2Swap.Api\ISwapApi.cs                        IUi 增加 Card/Text/Button/InfoRow/Buttons/Note
.\src\PclDlSwap.Payload\Swap\HostApi.cs               UiImpl 转发到 Views.SwapUi（与内置页面同一实现）
.\src\PCL2Swap.SampleAddon\SampleAddon.cs             演示页改用 SDK 原生构建器
.\src\PclDlSwap.ExLoaderHarness\Program.cs            ui 构建器断言（STA 线程）
.\src\Pcl2Swap.Shim\Shim.cpp + SwapLog.cs + Loader    版本号升 v26.0-Alpha 6
```

ChangLog:
Num|File Name|Change Description|Change Time|Changer
----|----|----|----|----
1|原生外观 SDK|`IUi` 新增 `Card / Text / Button / InfoRow / Buttons / Note`：第三方模块无需拼 WPF 就能得到与官方设置页一致的卡片/按钮外观；宿主缺少 MyCard/MyButton 时退回普通 WPF。harness 断言构建器在 STA 线程返回可渲染元素|2026.10.28-06:00:00|PCL2.dl.swap
2|示例模块|演示页改用 SDK 原生构建器，真机截图确认与内置页面同款卡片外观（两张 MyCard + MyButton，含实时计数）|2026.10.28-06:00:00|PCL2.dl.swap
3|踩坑记录|WPF 控件必须在 STA 线程创建：harness 的 addon 测试原本跑在 MTA 主线程，调用构建器直接 InvalidOperationException（InputManager 要求 STA）。测试改为 STA 线程执行，SDK 文档也注明页面工厂在宿主 UI 线程调用|2026.10.28-06:00:00|PCL2.dl.swap
4|版本|工作构建升到 `v26.0-Alpha 6`（`%TEMP%\PCL2Swap\v26.0-Alpha6\`），与已发布的 Alpha 5 资产区分；真机自检 `ok=27 fail=0`|2026.10.28-06:00:00|PCL2.dl.swap

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

