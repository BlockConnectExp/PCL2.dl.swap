# PCL2.dl.swap v26.1-Alpha 4

> Fourth Alpha of the v26.1 line: the injector gets **27% smaller** (90.7 MB -> 66.0 MB) by
> referencing only the WinUI surface it actually uses, gains **failure diagnostics in CLI mode**,
> and the build scripts can now run **in parallel** with another agent session.

![注入器：目标探测与注入包列表](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha4/shot-injector.png)

![注入器：注入成功](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha4/shot-injector-success.png)

---

## What's new

### 1. 注入器瘦身：90.7 MB → 66.0 MB

`Microsoft.WindowsAppSDK` 元包会把 AI/ML/Search/Widgets 子包一起拖进 self-contained 输出
（onnxruntime、DirectML、semantic-index 共约 62 MB）。注入器只引用真正用到的
`Microsoft.WindowsAppSDK.WinUI` + `Microsoft.WindowsAppSDK.Runtime`：

| 指标 | Alpha 3 | Alpha 4 |
|---|---|---|
| 解压后 | 225.6 MB | 163.5 MB |
| 发布 zip | 90.7 MB | **66.0 MB** |

语言卫星资源仅 3.4 MB，未裁剪；剩余体积是 WinUI self-contained 的正常组成。

### 2. CLI 失败诊断

`--cli inject` 失败时现在会：

- 打印 `---- 诊断（可直接粘贴到 Issue）----` 段落（环境 / 目标指纹 / 注入包 / 失败阶段）；
- 把同样内容保存到 `文档\PCL2Swap\reports\inject-failure-<时间>.md`；
- 维持退出码约定：`0` 成功、`1` 参数/环境错误、`2` 注入失败。

GUI 的失败对话框与 CLI 现在共用同一套 `Diagnostics` 生成逻辑，两条路径产出可比对的报告。

### 3. 并行构建

`scripts/build.ps1`、`src/Pcl2Swap.Shim/build.ps1`、`scripts/build-injector.ps1` 新增输出目录参数：

```powershell
.\scripts\build.ps1 -ToolVersion "v26.1-Alpha4" -DistDir "dist-a4"
.\src\Pcl2Swap.Shim\build.ps1 -SkipPayloadBuild -OutDir "dist-a4\exloader"
.\scripts\build-injector.ps1 -ToolVersion "v26.1-Alpha4" -DistDir "dist-a4"
```

当另一个会话的测试实例锁住 `dist\exloader\Pcl2Swap.Shim.x64.dll` 时，构建不再互相破坏。
`.gitignore` 同步忽略 `dist-*/`。

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest` / `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| 真机 2.99.0.0（413） | 启动并注入成功，`ok=30 fail=0`，日志 `宿主匹配：v299-rel413` |
| 真机 2.99.0.1（415） | 启动并注入成功，`ok=30 fail=0`，日志 `宿主匹配：v2990_1-rel415` |
| 注入器瘦身后 GUI | 正常启动、标题栏/主题/列表渲染正常 |
| 注入器瘦身后 CLI `list` | 4 个清单目标 / Releases / 随附注入包输出正常 |
| CLI 失败诊断（无效 PID） | `exit=2`，诊断段落完整，报告落盘 `inject-failure-20261031-012121.md` |

## Artifacts

| File | Purpose |
|---|---|
| `PCL2.dl.swap-Injector-v26.1-Alpha4.zip` | WinUI 3 注入器（self-contained，66.0 MB，含 shim 种子） |
| `Pcl2Swap.Shim.x64.dll` / `.dat` | ExLoader 通道的注入包 |
| `PCL2.dl.swap-v26.1-Alpha4.zip` | shim + dat + SHA256SUMS |
| `PCL2.dl.swap-Loader-v26.1-Alpha4.zip` | 磁盘补丁形态 |
| `pcl2swap-2.99.0-v26.1-Alpha4.dll` | 磁盘补丁产物 |

---

> 提示：注入器对以管理员运行的 PCL2 需要同权限启动；失败时先看 `文档\PCL2Swap\reports\`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
