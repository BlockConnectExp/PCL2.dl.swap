# PCL2.dl.swap v26.1-Alpha 5

> Fifth Alpha of the v26.1 line: **English support** lands as a runtime translation layer for a
> Chinese-only PCL2, and the addon SDK grows to **API 1.1** with a bilingual surface, two new
> back-end accessors and four new front-end controls.

![English launcher: navigation, buttons and log lines](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha5/shot-english-launch.png)

![English Swap Settings: language switch and host information](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha5/shot-english-settings.png)

---

## What's new

### 1. PCL2 英文支持（运行时翻译层）

PCL2 本身没有本地化资源，所有 UI 文本都是中文硬编码。Swap 用与补丁一致的方式交付英文：

- **语言设置**：`设置 → Swap Settings → Language`（Follow system / Chinese / English），
  状态存 `<数据根>\ui.json`，切换立即生效、可一键回退中文。
- **宿主翻译**：`HostTranslator` 遍历 WPF 视觉树，翻译 TextBlock/Run、只读 TextBox、
  RichTextBox、ContentControl 字符串、Header、Window.Title、ToolTip 与 `Text`/`Info` 属性。
- **词典**：内置约 110 条（导航 / 下载 / 设置卡片 / 关于 / 反馈 / Swap 页面 / 启动日志），
  支持 `<数据根>\i18n\en-US.json` 磁盘覆盖与运行时 `I18n.Register`。
- **短语表**：带运行时变量的行（`[时间] 登录加载已开始`、`错误：…`、`钩子抛错 N 次…`）
  逐段替换，保证动态行也能英文化。

实测英文效果：顶部 Launch/Download/Settings/More、Online/Offline、启动日志三行、
Launch Game / Versions / Version Settings、设置侧栏与全部卡片、Swap Settings 语言卡与
Host information（含 `Addon directory` 完整标签）、侧栏 `Manage Swap addons (3)`。

### 2. SDK API 1.1

| 新增 | 类型 | 说明 |
|---|---|---|
| `ISwapApi.I18n` | 后端 | `T()` / `Register()` / `SetLanguage()` / `IsEnglish` |
| `IPclAccess.Ui` | 后端 | `CurrentPage` / `MainWindow` / `FindPage` / `Translate` / `Restore` |
| `IPclAccess.Logs` | 后端 | `LogPath` / `Tail(n)`（只读 PCL 日志） |
| `IUi.Slider` | 前端 | 标签 + 滑杆 + 数值读数 |
| `IUi.TextInput` | 前端 | 标签 + 输入框（优先宿主 MyTextBox） |
| `IUi.Link` | 前端 | 下划线链接（走宿主 OpenWebsite） |
| `IUi.Separator` | 前端 | 主题色分隔线 |

API 1.1 只增加成员，1.0 addon 与 `apiVersion: ">=1.0 <2.0"` 完全兼容。

### 3. 文档

- `docs/i18n.zh.md`：英文层架构、词典扩展、Addon 用法与已知限制。
- `docs/swap-api.md` §12：API 1.1 全量示例。

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest` / `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS（示例 addon 读到 API 1.1.0） |
| 真机 2.99.0.1 · 注入自检 | `ok=38 fail=0`（新增 8 条：I18n.Explicit / I18n.Roundtrip / 4 个 UI 控件 / Logs.LogPath / Ui.CurrentPage） |
| I18n 往返回路 | `EN→Host information；ZH→宿主信息` |
| 英文界面 | 主界面、设置页、Swap Settings、侧栏 Info、启动日志行均按预期英文化 |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha5.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha5.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha5.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha5.dll` | 磁盘补丁产物 |

---

> 英文支持是渐进覆盖：词典外的 PCL 文本保持中文，可按 `docs/i18n.zh.md` 通过磁盘词典补充。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
