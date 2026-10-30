# PCL2.dl.swap v26.1

> The **official release** of the v26.1 line (Alpha 1-10). PCL2 Swap grows from a download
> restore patch into an LSPosed-style module platform: signable packages, strict permissions,
> a WinUI 3 injector, runtime English, two page slots and a buildable addon template.

![Swap Settings on v26.1](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1/shot-swap-settings.png)

![English Help page](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1/shot-help-en.png)

![Demo addon registered through the template](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1/shot-demo-addon.png)

---

## v26.1 历程（Alpha 1 → 正式版）

| Alpha | 主题 |
|---|---|
| Alpha 1 | 模块详情视图、钩子注册清理 |
| Alpha 2 | 包信任链（RSA 签名 + 文件哈希 + 版本级告警） |
| Alpha 3 | WinUI 3 注入器、PCL 2.99.0.1 全量回归 |
| Alpha 4 | 注入器瘦身 90.7→66.0 MB、CLI 失败诊断、并行构建 |
| Alpha 5 | PCL2 英文支持（运行时翻译层）、API 1.1 |
| Alpha 6 | 更多页槽位、Shell/Clipboard 后端、API 1.2 |
| Alpha 7 | 权限 opt-in 严格门控 |
| Alpha 8 | 英文覆盖深化（帮助/关于/下载页） |
| Alpha 9 | Addon 模板工程、热更新/卸载生命周期修复 |
| **v26.1** | 收官：zip-slip 防御断言、英文版本括号归一化、正式发布 |

## 核心能力

| 能力 | 状态 |
|---|---|
| ExLoader 通道（shim dll/dat）+ 磁盘补丁通道 | 双形状 |
| `.swapaddon` 安装/更新/卸载/热重载，延迟删除 | 已实测 |
| 包信任链：`files` 清单 + RSA-SHA256 + `trusted-keys` | 已实测 |
| 权限严格门控（声明才约束，11 个门控点） | 已实测 |
| 设置页槽位 100+ / 更多页槽位 200+ | 已实测 |
| 运行时英文（导航/设置/帮助/关于/日志/Swap 页面） | 已实测 |
| WinUI 3 注入器（GUI + CLI，66 MB） | 已实测 |
| Addon 模板 `examples/demo-addon` | 已实测 |

## 兼容性

| PCL2 版本 | 状态 |
|---|---|
| 2.99.0.0（413） | `ok=45 fail=0` |
| 2.99.0.1（415） | `ok=45 fail=0` |

API 版本 `1.2.0`；1.0/1.1 addon 完全兼容。未声明 permissions 的模块走兼容模式。

## 最终验证矩阵

| 验证 | 结果 |
|---|---|
| harness 六套件 | addontest / pcltest / pagetest / scantest / loaderhooktest / surfacetest 全 PASS |
| zip-slip 防御 | `../evil.txt` 条目未被写出（新断言） |
| 真机自检 2.99.0.0 / 2.99.0.1 | `ok=45 fail=0` |
| 模块生命周期 | 安装 launch #1 → 同版本更新 launch #2 → 卸载 True（页面 102/200 注销）→ 重启清理 |
| 英文 | Help/About/Swap Settings/启动日志英文化，可切回中文 |
| 注入器 | 目标探测/Release 列表/运行中注入/启动并注入/CLI 失败诊断 |

## 升级说明

- 从 v26.0 或任意 v26.1 Alpha 升级：直接使用本 Release 的 shim 或注入器即可；
- 已安装的 `.swapaddon` 在升级后自动加载；未声明 permissions 的包保持兼容模式；
- 英文默认跟随系统语言（中文系统保持中文），可在 Swap Settings → Language 切换。

## 已知限制

1. 英文为渐进覆盖，词典外文本保持中文；
2. 同版本热更新会保留旧版本目录（DLL 被 CLR 锁定），下次启动清理；
3. CLI 的 gh issue 提交依赖本机 `gh auth login`；
4. PCL2 2.12 赞助版样本尚未纳入兼容矩阵（等待样本提供）。

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1.zip` | WinUI 3 注入器（self-contained，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1.dll` | 磁盘补丁产物 |

---

> 下一步（v26.2 线）：更多页/英文继续覆盖、PCL 2.12 样本 REA、注入器干净机器验证。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
