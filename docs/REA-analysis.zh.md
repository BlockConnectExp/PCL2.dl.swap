# PCL2 下载模块提取与 OTA/沙箱注入 · 反编译级分析

> 目标：以官方 2.14.0 的下载模块为基线，注入到后续版本（重点 2.99.0，其下载不可用），
> 同时把「静默 OTA」改为显式可取消，并把「从 PCL 官方云下载的文件」改为
> **沙箱下载 → 多引擎查毒 → 通过后才落盘**。
>
> 本文所有结论均来自对官方二进制的静态反编译（IL 级），并给出可复核的样本哈希。

## 0. 样本台账（已实测校验）

| 编号 | 版本 | 构建类型 | VersionCode | 样本文件名 | SHA-256 | MVID |
|---|---|---|---|---|---|---|
| S1 | 2.12.8.1 | 快照版 | 401 | `Plain Craft Launcher 2.exe` | `d4f3e8f789cfd449ae9902ccadbadf5f35f3ac1b88793db37db5744cfd158fac` | `8558c9c3-b0f8-4e5c-8944-969432ccfb74` |
| S2 | 2.14.0.0 | **正式版（提取基线）** | 411 | `Plain Craft Launcher.exe` | `5582952ded3b443503f66c23b810802bd20f1a3e847b54d9e09653a825859a36` | `e6e9028d-8366-47b6-a8b0-1ce46a3e5278` |
| S3 | 2.14.0.0 | 快照版 | 412 | `快照版 2.14.0.0.exe` | `77fbc26a13abf725dbd26038cba023224c2ce5518f3ee9c4608acb2274d753f0` | `2a08e6c9-87e4-41c0-b237-17bd168c67fe` |
| S4 | 2.99.0.0 | **正式版（注入目标）** | 413 | `Plain Craft Launcher 2.exe` | `6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386` | `926d447e-daff-4169-8c64-adc3d37d5f78` |

四个样本均为 **PE32 / x86 / .NET Framework 4.8 托管程序**（CLI 头 72 字节、`il-only`、
`AnyCPU` 但实际以 x86 运行），因此模块提取与注入都可以在托管层完成。

校验命令（本仓 `tools/VerifyOffsets`）：

```powershell
dotnet run -c Release -- ..\..\data\offset-manifest.json <sample.exe>...
# 输出：sha256 / mvid / 命中条目 / token 解析 / RVA —— 当前 4/4 样本 RESULT: PASS
```

## 1. 程序集与资源形态

每个官方 EXE 就是完整的 PCL 主程序集（未加壳、类型名未混淆），关键清单资源：

| 样本 | `Plain Craft Launcher 2.g.resources` | `PCL.Resources.resources` |
|---|---|---|
| 2.12.8.1 | 748,726 B | 3,126,026 B |
| 2.14.0.0 | 773,756 B | 3,476,322 B |

`*.g.resources` 里是 **WPF BAML**（编译后的 XAML），`PCL.Resources.resources` 里是图片/字体等。
**下载页面本身就是 BAML 资源**（如 `/Plain Craft Launcher 2;component/pages/pagedownload/resource/pagedownloadmod.xaml`），
所以「提取下载模块（含页面）」必须以**程序集 + 资源**为单位搬运，
不能用反编译出的 C# 重新编译——那样会丢掉全部页面资源，等价于重写 UI。

## 2. 下载模块的边界（2.14.0 / 2.99.0 完全一致）

以下类型构成"下载模块"，两版本反编译产物**逐字节同尺寸**（除个别非下载改动）：

| 类型 | 2.14.0 反编译产物 | 2.99.0 反编译产物 | 职责 |
|---|---|---|---|
| `PCL.ModDownload` | 69,975 B | 69,975 B | 版本/加载器清单解析（Forge/Fabric/NeoForge/OptiFine…） |
| `PCL.ModDownloadLib` | 233,364 B | 233,364 B | 下载任务编排、Minecraft 安装清单落地 |
| `PCL.ModNet` | 115,775 B | 115,775 B | HTTP 层、多源重试、限速、`NetFile`/`LoaderDownload` |
| `PCL.ModLoader` | 35,285 B | 35,285 B | `LoaderTask` / `LoaderCombo` / 进度与取消 |
| `PCL.PageDownloadLeft` | 13,745 B | 13,745 B | 下载页左侧导航与子页切换 |
| `PCL.PageDownloadInstall` | 99,363 B | 100,366 B | **安装/下载主页面（唯一有差异的下载页）** |
| `PCL.PageDownloadMod` / `Pack` / `DataPack` / `ResourcePack` / `Shader` | 各 ~2.1 KB | 同 | 各资源子页壳（内容由 `PageResource` + BAML 驱动） |
| `PCL.PageDownloadResourceDetail` | 46,301 B | 46,301 B | 资源详情（Modrinth/CurseForge 版本列表） |
| `PCL.PageResource` | — | — | 资源搜索/列表共用页面 |
| `PCL.PageDownloadModify` | 存在 | 存在 | 整合包修改 |

页面注册关系（`PageDownloadLeft.PageGet` → `FormMain.PageSubType`）：

```
Default(=0)        -> PageDownloadInstall
DownloadMod(=2)    -> PageDownloadMod
DownloadPack(=3)   -> PageDownloadPack
DownloadDataPack=4 -> PageDownloadDataPack
DownloadResourcePack=5 -> PageDownloadResourcePack
DownloadShader(=6) -> PageDownloadShader
```

## 3. 静默 OTA 的完整链路（已定位到方法级）

### 3.1 调用链

```
ModSecret.ServerSub(LoaderTask)                     ← 拉取 notice.cfg / ServerConfig.json
   └─ ServerConfig["Update"][Release|Snapshot]
        ├─ PatchUrls    （增量补丁，MsDelta）
        └─ DownloadUrls （完整包）
   └─ ModSecret.UpdateStart(Silent := <由调用方决定>)
        ├─ Silent = True   ──► IsUpdateWaitingRestart = True（后台静默替换）
        └─ Silent = False  ──► ModMain.Hint("PCL 正在下载更新…")
   └─ 下载至 %TEMP%\PCL\<version>.<code>.<ext>
   └─ zip 解包 / MsDelta.Apply
   └─ 移动到  <启动器目录>\PCL\Plain Craft Launcher 2.exe
        └─ ModSecret.UpdateRestart(TriggerRestartAndByEnd)
             └─ ModSecret.UpdateReplace(ProcessId, OldFileName, NewFileName, TriggerRestart)
```

关键证据（2.14.0 反编译，`PCL.ModSecret.cs`）：

| 行 | 内容 |
|---|---|
| 833 | `private static void UpdateStart(bool Silent, IEnumerable<string> UrlsOverride = null)` |
| 851 | `IsSilentUpdate = Silent;` |
| 852 | `if (!IsSilentUpdate) ModMain.Hint("PCL 正在下载更新，更新结束时将自动重启，请稍候！");` |
| 206 / 233 | `if (IsSilentUpdate) { IsUpdateWaitingRestart = true; … }` |
| 174 / 914 | `Logger.Info("更新开始，静默：{0}，扩展名：{1}，下载目标：{2}，URL：{3}", …)` |
| 943 | `if (!IsSilentUpdate) { …显示进度… }` |
| 1321 / 1354 | `UpdateRestart(bool)` / `UpdateReplace(int, string, string, bool)` |
| 1465 | `NetDownloadByLoader(ServerConfig["Update"]["Release"]["DownloadUrls"], %TEMP%\PCL\Latest.zip, …)` |

`ModSecret.UpdateStart` 是**唯一**的 OTA 入口 choke point：无论静默还是显式，
最终都会进入它。因此把它作为拦截点，既能"禁止静默 OTA"，也不会漏掉任何自动更新路径。

### 3.2 权限闸门

`ModSecret.HasUpdatePermission`（`get_HasUpdatePermission`）在 2.12.8.1 中依赖
"土豆码"（`PotatoFeatures.Contains("Update")`），2.14.0/2.99.0 中已恒为 `true`
（`private static bool HasUpdatePermission => true;`）。把该属性置 `false`
可以从权限层直接掐死自动更新，是仅次于 `UpdateStart` 的第二道闸。

### 3.3 官方 PCL 云服务端点（非游戏资源源）

```
https://pcl2-server-1253424809.file.myqcloud.com/notice.cfg{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/minecraft/download.json{CDN}
https://pcl2-server-1253424809.file.myqcloud.com/notice.json{CDN}
```

`{CDN}` 占位符由 `ModSecret.SecretCdnSign` 生成签名（DES 解固定串 + SHA-1 拼接
`path-timestamp-rand-0-secret`）。实机访问当前返回 **403**，说明官方更新链路对旧版本已收紧，
这也解释了"更新功能被删/不可用"的体感来源。

## 4. 偏移适配模型（后续版本只改偏移）

`data/offset-manifest.json` 是唯一需要随版本更新的机器可读文件：

| 字段 | 含义 | 用途 |
|---|---|---|
| `id` / `version` / `versionCode` / `buildType` | 版本身份 | 人读、日志 |
| `sha256` | 整个 EXE 哈希 | 精确绑定，防错配 |
| `mvid` | 模块 MVID | **首选匹配键**（同版本重建也能识别） |
| `tokens.*` | 方法 metadata token | 免名字匹配的稳定定位（跨版本常常不变） |
| `rva.*` | 方法体 RVA | 需要走文件/原生偏移时使用 |

当前已实测命中（`VerifyOffsets` 全绿）：

| 方法 | S1 (2.12.8.1) | S2 (2.14.0 rel) | S3 (2.14.0 snap) | S4 (2.99.0 rel) |
|---|---|---|---|---|
| `PCL.ModSecret.UpdateStart` | `0x06000419` / `0x268F4` | `0x0600041F` / `0x24B50` | `0x0600041F` / `0x24F38` | `0x0600041F` / `0x249F4` |
| `PCL.ModSecret.get_HasUpdatePermission` | `0x06000420` / `0x280ED` | `0x06000426` / `0x26294` | `0x06000426` / `0x2660D` | `0x06000426` / `0x26138` |
| `PCL.ModSecret.UpdateCheckByButton` | `0x06000418` / `0x26820` | `0x0600041E` / `0x24A7C` | `0x0600041E` / `0x24E64` | `0x0600041E` / `0x24920` |
| `PCL.ModSecret.UpdateRestart` | `0x0600041A` / `0x26A78` | `0x06000420` / `0x24CD4` | `0x06000420` / `0x250BC` | `0x06000420` / `0x24B78` |
| `PCL.ModSecret.UpdateReplace` | `0x0600041B` / `0x26BB4` | `0x06000421` / `0x24E10` | `0x06000421` / `0x251F8` | `0x06000421` / `0x24CB4` |

> 观察：2.14.0 与 2.99.0 的 `ModSecret` 方法 **token 完全一致**，只有 RVA 变化。
> 这正是"后续版本只适配内存偏移量"成立的依据。

## 5. 运行时拦截的实测结论（重要，决定实现路线）

本仓已实现并**实测**了 x86 托管 detour（`PclDlSwap.Payload/Hook`，含 Capstone 反汇编、
序言按整指令搬迁、trampoline 生成）。实测结论：

| 现象 | 结果 |
|---|---|
| 目标方法首字节被改成 `E9`（相对跳转） | ✅ 已证实（`target[0]=0xE9`） |
| 钩子函数被调用 | ✅ 已证实（能打印出与调用方一致的参数） |
| trampoline 回跳并执行原方法体 | ❌ **不稳定**：JIT 序言为 `push reg` 序列后用 `ecx/edx` 传参（x86 快速调用），通过托管委托进入 trampoline 时寄存器态与原生调用点不一致，回跳后崩溃（`0x80000003`） |

因此**不采用**"托管 trampoline 回跳"的方案。可行且更稳的路线（按推荐顺序）：

1. **CLR Profiler 在 JIT 时重写 IL**（`CORECLR_ENABLE_PROFILING` / `COR_PROFILER` + `ICorProfilerInfo::SetILFunctionBody`）：
   官方支持、不碰机器码、与 x86/x64 无关；启动器负责设置环境变量并拉起 PCL。
2. **原生 stub 钩子**：用 `stdcall` 原生小段接管入口（自建原生 trampoline），
   从原生侧按 stdcall 读参并转发；托管侧只做逻辑。
3. **IL 内存改写**：`MethodDesc -> m_pIL` 覆写 + 清 JIT 标志位强制重编译
   （需按运行时版本校验结构偏移，风险最高但最快）。

方案 1 与 Owner 期望的"运行前准备好 → 自动启动并注入，不注入就是原版"完全吻合。

## 6. 沙箱 / 查毒 / 落盘流水线（已实现，可独立验证）

`PclDlSwap.Payload/Sandbox`：

```
PCL 云下载请求
  ├─ URL 命中 PCL 官方域（pcl2-server / meloong.com / pclc.cc / pcl2.net）？
  │    否 → 原样交给宿主（游戏资源下载不受影响）
  │    是 ↓
  ├─ 下载到 %TEMP%\PclDlSwap\sandbox\<guid>.<ext>（**不执行、不落最终路径**）
  ├─ 打 Mark-of-the-Web（Zone.Identifier ADS）
  ├─ 多引擎扫描
  │    Microsoft Defender  （MpCmdRun.exe -Scan -ScanType 3 -DisableRemediation）
  │    ClamAV              （clamscan.exe，如已安装）
  │    VirusTotal          （配置 API Key 后按 SHA-256 查报告）
  │    MetaDefender Cloud  （配置 API Key 后上传取 `detected_by`）
  ├─ 有任一引擎判定恶意 / 未通过 → **不落盘** + 顶层弹窗给出目标路径与检测报告
  └─ 全部通过 → 拷到目标路径（保留 MotW）
```

策略要点：
- **默认"未验证即拦截"**：引擎不可用也会阻断，只有显式开启 `AllowDegradedCommit` 才放行。
- 端口/路径全部走 `config/swap.config.json`，不写死。
- 扫描产物默认保留在沙箱目录，便于事后取证。

## 7. 产物命名与分发

最终产物：`pcl2swap-{PCL2Ver.}-{Ver.}.dll`

- `{PCL2Ver.}`：注入目标的 PCL 版本（当前 `2.99.0`）。
- `{Ver.}`：本工具自身的补丁/模块版本。
- 命名即声明：一个 DLL 对应一个目标版本，内部绑定 `offset-manifest.json` 的 token/RVA。

## 8. 未完成项与下一步

| 项 | 状态 |
|---|---|
| 全量反编译（4 个样本，各 655/665 类型） | ✅ 已完成，产物在 `_scratch/pcl2-dlswap/source-*` |
| 偏移清单 + 校验器 | ✅ 已完成，4/4 样本 PASS |
| 沙箱/查毒/落盘流水线 | ✅ 已实现（含 MotW、多引擎、失败即拦截） |
| OTA 拦截/权限闸门逻辑 | ✅ 逻辑实现；运行时挂载待改用 Profiler 路线 |
| 2.14.0 下载模块（含 BAML 页面）可注入化 | ⏳ 需完成资源改名 + 程序集绑定重定向 |
| 启动器（环境变量注入 + 自动拉起） | ⏳ 待实现 |
| 后续版本偏移自动生成脚本 | ⏳ 待实现 |

## 13. 复现路径

```powershell
# 1) 反编译（逐类型容错）
dotnet run -c Release -- <sample.exe> <outDir>        # tools/DecompAll
# 2) 方法/偏移表
dotnet run -c Release -- <sample.exe> <out.tsv>       # tools/MethodTable
# 3) 清单校验
dotnet run -c Release -- data\offset-manifest.json <sample.exe>...
# 4) 钩子内核自测（含目标字节与 trampoline 字节打印）
dotnet run -c Release                                  # tools/DetourTest
```

---

## 9. 2.99.0 的下载封锁点（已精确定位到单点）

2.99.0 **没有删除**下载模块：`ModDownload` / `ModDownloadLib` / `ModNet` / `ModLoader`
与全部 `PageDownload*` 页面、以及 `ModDownload.DlClientListLoader` 都仍在程序集里。
它只改了一处：

`PCL.PageDownloadInstall.LoaderInit()`（v2140 token `0x06000513` / v299 同 token）

```csharp
// 2.14.0（可用）
PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null,
               ModDownload.DlClientListLoader, a0 => LoadMinecraft_OnFinish());

// 2.99.0（封锁）：第 5 个参数被换成抛异常的假加载器
PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null,
               new ModLoader.LoaderTask<string, ModDownload.DlClientListResult>("关闭下载", a0 =>
               {
                   throw new Exception("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……");
               }),
               a0 => LoadMinecraft_OnFinish());
```

同族还有两处：`PageDownloadInstall/_Closure$__._Lambda$__R3-3`（token 待补）与
`_Lambda$__3-0`，都只做同一件事——抛这条异常。

后果：异常文案被 `FormMain`（`PCL.FormMain.cs:261` / `1259` / `2602`）专门识别，
用于把"下载被关闭"当作预期状态处理（提示 + 停止后续流程），
所以表现为"下载功能不可用"而不是崩溃。

| 版本 | LoaderInit token | LoaderInit RVA |
|---|---|---|
| 2.12.8.1 (401) | `0x0600050C`（private，布局更早） | `0x308AC` |
| 2.14.0.0 (411) | `0x06000513` | `0x2E010` |
| 2.14.0.0 (412) | `0x06000513` | `0x2E6D4` |
| 2.99.0.0 (413) | `0x06000513` | `0x2DEB4` |

## 10. 实例管理对下载的引用（已核实，含影响评估）

Owner 提示的"下载在游戏实例管理里被引用"成立，且是**同一套代码**：

```
FormMain.cs:2347
  if (PageCurrent == PageType.Download && PageCurrentSub == PageSubType.Default
      && ModMain.FrmDownloadInstall.IsInSelectPage)
      ModMain.FrmDownloadInstall.ExitSelectPage();      // 进入实例管理时退出选择模式

PageDownloadInstall.cs:1590  PrepareSelectPage()   -> IsInSelectPage = true
PageDownloadInstall.cs:1616  EnterSelectPage()     -> PrepareSelectPage()
PageDownloadInstall.cs:1705  MinecraftSelected()   -> EnterSelectPage()（"更改游戏版本"入口）
```

即：**"更改游戏版本"复用 `PageDownloadInstall` 的选择页模式**，
它同样依赖 `ModDownload.DlClientListLoader`（`PageDownloadInstall.cs:1571/2097`）。

影响评估：

1. 恢复 `LoaderInit()` 会**同时**救回「下载页」与「更改游戏版本」两条路径，
   不需要为实例管理单独打补丁。
2. 两条路径共用同一加载器，因此查毒沙箱只需挂在 `ModNet` 的下载入口，覆盖范围天然一致。
3. 反过来说，**若未来官方删除 `ModDownload` / `DlClientListLoader`**，
   这两条路径会一起失效——这正是必须尽早做"自持下载模块"的原因（见 §11）。

## 11. 自持下载模块的形态（面向"官方删除"的终局方案）

因为 2.99.0 仍带模块，短期只需恢复闸门；但官方一旦删除，方案必须能独立存活：

1. 从 2.14.0 取出下载模块相关的 IL + BAML + 资源，**编入 `pcl2swap-*.dll` 自身**
   （不是外挂引用宿主）。
2. 注入后把「下载页入口」重定向到 DLL 内部实现：宿主仍提供窗口/主题/设置，
   DLL 提供页面与下载逻辑。
3. 与 OTA 闸门、查毒沙箱共用同一套偏移清单：新版本只需补 token/RVA（本仓 §4 的模型）。

这样即使宿主删掉 `ModDownload`，`pcl2swap` 仍是自洽的下载实现。

## 12. 实现路线定稿（含实测淘汰记录）

为把上面三件事真正落地，先后实测了四条路线，结果如下——记录在此以免重复踩坑：

| 路线 | 实测结果 | 结论 |
|---|---|---|
| 托管 detour（改 JIT 后机器码首字节 + trampoline） | 目标首字节确实被写成 `E9`、钩子确实被调用，但 trampoline 回跳在 x86 快速调用约定下不稳定（`0x80000003` 崩溃） | 淘汰 |
| `MethodRental.SwapMethodBody` 跨程序集改 IL | 同程序集可行；跨程序集即使换成最简 `ldc.i4.0; ret` 也直接崩溃 | 淘汰 |
| `calli` + 函数指针槽（无外部 token） | 同样被 CLR 拒绝（跨程序集 IL 替换本身不被允许） | 淘汰 |
| 原生 CLR Profiler（`ICorProfilerInfo::SetILFunctionBody`） | Profiler DLL 能被 CLR 加载、COM 工厂正常、`QueryInterface` 正常；但 `Initialize` 未被调用（.NET Framework 4.8 x86 下 CLR 未完成初始化握手）。已产出可复用源码（`src/PclDlSwap.Profiler`，228 KB DLL） | 暂缓 |
| **磁盘 IL 补丁（dnlib 重写 EXE）** | **通过**：补丁后 2.99.0 正常启动，三处改写经 IL 复核全部生效 | **采用** |

### 为什么磁盘补丁能成立

1. 官方 PCL **无强名**（`PublicKeyToken=null`），改 IL 不会破坏签名校验。
2. PCL 自身支持“启动器目录 + `PCL\` 子目录”的双层结构（更新包就是写到 `PCL\Plain Craft Launcher 2.exe`），
   所以把补丁副本放在同目录运行是它本来就支持的形态，路径解析不需要额外处理。
3. 补丁只引用宿主**已有**类型，不新增任何 `AssemblyRef`——dnlib 无法给已加载模块追加引用表项，
   而“零新增引用”正好绕开这个限制，也让补丁后的模块结构与原版几乎一致。
4. 沙箱/查毒既可以由被注入的 Loader 在桌面侧做（`-watch` 看门狗，已实现），
   也可以由 Patcher 在打补丁时复用同一套 `ScannerSet` / `DownloadSandbox`。

### 已交付形态

```
pcl2swap-2.99.0-v26.0.Alpha1.dll      （= PclDlSwap.Patcher.dll）
PclDlSwap.Loader.exe                  （打补丁 + 启动 + 可选看门狗）
PclDlSwap.Payload.dll                 （多引擎查毒 / 沙箱 / 看门狗 / 日志）
data\offset-manifest.json             （6 方法 × 4 版本，VerifyOffsets 全绿）
```

对应 Owner 的最终要求：产物为单一 `*.dll`（Patcher），加上负责拉起进程与看门狗的可执行入口。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->

