# PCL2.dl.swap v26.1-Alpha 1

> First Alpha of the v26.1 line (the ten-Alpha cycle that will end in the official `v26.1`).
> Two manager features land together: **install review** (see what a package asks for before it
> touches the disk) and the **module detail view** (metadata, audit trail, hook chain, log tail and
> lifecycle actions for one module) — plus a real hook-registration leak fix on uninstall/reload.

![安装前预览：作用域、权限与 SHA-256](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha1/shot-install-preview.png)

![模块详情：元数据、审计、钩子链、日志尾部与动作](https://github.com/BlockConnectExp/PCL2.dl.swap/releases/download/v26.1-Alpha1/shot-module-detail.png)

---

## What's new since v26.0

### 1. 安装前预览（信任审查）

- 「安装模块…」现在是 **选包 → 原生对话框预览 → 确认才落盘**。预览列出：名称 / id / 版本 / API 范围 /
  入口 / 作者 / 说明 / **形状作用域** / **宿主版本作用域** / **权限声明** / 包大小 / **SHA-256**。
- 作用域与当前宿主不符时，预览里直接写明「注意：模块声明 X，当前宿主 Y —— 安装后不会启用」；
  已经装过同一个 id 时，标题与文案切换成「更新模块（旧 → 新）」。
- 取消只写一行日志（`安装模块已取消（预览阶段）`），不会留下半成品目录。

### 2. 模块详情视图（LSPosed 管理器式的单模块页）

- 每行的「详情」按钮展开管理页：状态 / id / 版本 / **安装时间** / **来源包** / **包 SHA-256** /
  模块目录 / 配置文件 / API 范围 / 入口 / 作者 / 形状与宿主版本作用域 / 权限声明 /
  钩子数（命中、抛错）/ 钩子链 / **模块日志尾部**。
- 动作：`禁用|启用`、`重载`、`打开日志`、`打开配置目录`、`收起`、`卸载`（紧凑动作条，原生按钮）。
- 来源与哈希在安装时记录；启动时发现的模块以包文件本身作为来源与时间。

### 3. 钩子注册清理（缺陷修复）

- 卸载 / 更新后模块的钩子注册原本留在链上，而**未知 addon id 被当作“已启用”** ——
  已卸载模块的 handler 会继续执行。`ScopedHook.RemoveAll` 现在在卸载与重载时清空该模块的全部注册。
- `重载` = `OnDisable` → 平台清理该模块遗留注册 → `OnLoad/OnEnable`；内置模块拒绝重载并提示用开关。
  这样重载不会让上一代的 Prefix handler 叠加执行。

## 已实测

| 场景 | 结果 |
|---|---|
| harness 六套件 | `--addontest`（新增 13 项：预览 4、审计/日志尾部/配置/选中态/重载/注册清理 9）/ `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| 真机 2.99.0：预览 | 原生对话框渲染，完整列出 `形状作用域 injected、patched`、`宿主版本 >=2.99.0`、`权限 host.hooks.attach、host.settings.write` 与 SHA-256；点「取消」日志 `安装预览返回：2` |
| 真机 2.99.0：详情 | 详情页完整渲染（作用域 / 权限 / 钩子链 / 日志尾部 / 六个动作同一行），见上图 |
| 真机 2.99.0：重载 | 日志实录 `Addon 重载：demo.scope` → `模块钩子已移除（链路已空）` → `加入既有钩子链` → `Addon 已启用` |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.1-Alpha1.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.1-Alpha1.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.1-Alpha1.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
a98794fca2da89d93c2efb25272bc7c80be44166b6ec92e715db8ee27e7f8fcd  Pcl2Swap.Shim.x64.dll
fca02b456457787fbb2d96cabfa4992e2ec4754b2afcdf6b2eb3b99f63d3cae8  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → Swap Addon → 模块管理 → 安装模块…：先出现审查对话框（作用域 / 权限 / SHA-256），
   点「取消」不会安装，点「安装」才会出现在列表里。
3. 点任意模块行的 **详情**：应看到该模块的元数据、审计信息、钩子链与日志尾部；
   点 **重载** 后日志里应出现「模块钩子已移除」与重新挂载。
4. 可选（脚本化）：`PCL2SWAP_E2E_PREVIEW=<包路径>` 启动，9 秒后同一个预览对话框会自己弹出。

## Known Limitations

- 预览显示的是 **manifest 的声明**，不是对包内容的静态审计；平台没有签名/来源校验
  （同进程模型下签名也不构成边界），SHA-256 只在你已知期望值时有用。
- 重载不重新加载程序集（CLR 无法卸载），只重跑 `OnDisable → OnLoad → OnEnable`；
  改代码后仍需重启 PCL。
- 权限仍是声明与展示，不构成沙箱。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
