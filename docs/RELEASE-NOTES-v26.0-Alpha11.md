# PCL2.dl.swap v26.0-Alpha 11

> Eleventh public Pre-Release: installing a module now goes through a **review dialog**. Before a
> single byte reaches the module store you see its name/id/version, the platform API range, the
> declared shape and host-version scope, the permissions it asks for, and the package SHA-256 - the
> module-platform counterpart of LSPosed's install review.

---

## Highlights since Alpha 10

1. **安装前预览（信任审查）**
   - `AddonManager.Inspect` 在**不落盘**的前提下读包（校验错误与安装时完全一致），
     `DescribeForReview` 生成审查文本：名称 / id / 版本 / API 范围 / 入口 / 作者 / 说明 /
     形状作用域 / 宿主版本作用域 / 权限声明 / 包大小 / **SHA-256**。
   - 作用域与当前宿主不符时，预览里直接写明「注意：模块声明 X，当前宿主 Y —— 安装后不会启用」。
   - 已经装过同一个 id 时，标题与文案切换成「更新模块（旧版本 → 新版本）」。
2. **确认后才落盘**：「安装模块…」的流程改成 选包 → 宿主原生 `MyMsgBox` 预览 →（「安装」/「更新」
   或「取消」）→ 归档 + 解包 + 启用。取消会写日志（`安装模块已取消（预览阶段）`），
   不会留下任何半成品目录。
3. **脚本可验证**：新增 `PCL2SWAP_E2E_PREVIEW=<包>`，启动 9 秒后在宿主 UI 线程弹出同一条预览，
   返回码写进日志（1 = 确认，2 = 取消），真机 e2e 因此能覆盖这个对话框。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| harness 全套 | `--addontest`（新增 4 项预览断言）/ `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| harness 预览断言 | 未安装的包可被检查、检查过程不触碰模块目录、预览文本含 API 范围与 SHA-256、预览文本含权限声明 |
| 真机 2.99.0（预览对话框） | 原生 MyMsgBox 渲染，完整列出 `形状作用域 injected、patched`、`宿主版本 >=2.99.0`、`权限 host.hooks.attach、host.settings.write`、包大小与 SHA-256；点「取消」后日志出现 `[e2e] 安装预览返回：2` |
| 真机 2.99.0（安装路径） | 同一条 `AddonManager.Install` 路径在 Alpha 10 已验证（含作用域判定与模块日志） |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha11.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha11.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha11.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
b12463e3113e7e1202d251ee6c65122f583cf03204c025815359800ac2c7cf0d  Pcl2Swap.Shim.x64.dll
e8cafd3582853d725b2381e47d43ec469a72dcb845ca8225ce9c7cffe830cecc  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → Swap Addon → 模块管理 → 安装模块… → 选一个 `.swapaddon`：应先出现审查对话框，
   显示模块信息、作用域、权限与 SHA-256；点「取消」不会安装，点「安装」才会出现在列表里。
3. 已经装过的模块再选一次同样的包：对话框标题变成「更新模块」，并显示旧版本 → 新版本。
4. 可选（脚本化）：设 `PCL2SWAP_E2E_PREVIEW=<包路径>` 启动，9 秒后同一个对话框会自己弹出，
   日志里会写 `[e2e] 安装预览返回：1|2`。

## Known Limitations

- 预览显示的是 **manifest 的声明**，不是对包内容的静态审计：包内 dll 不做反编译检查。
- SHA-256 只在你已知期望值时有用；平台没有签名/来源校验（同进程模型下签名也不构成边界）。
- 取消只会中止这次安装，不清理你选中的原始文件（平台从不修改用户自己的文件）。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
