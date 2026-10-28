# PCL2.dl.swap v26.0-Alpha 10

> Tenth public Pre-Release: modules can finally say **which PCL builds they support**. `hostVersions`
> is the scope field for a single-app platform - a module that does not fit the running launcher is
> skipped with a visible status instead of being loaded into a build it was never tested on - and the
> install path tells you about the mismatch before you wonder why nothing happened.

---

## Highlights since Alpha 9

1. **宿主版本作用域（`hostVersions`）**
   - 每项互相 OR：`"*"`、精确版本 `"2.99.0.0"`、或比较式 `">=2.99.0"` / `"<3.0"` / `"=2.99.0.1"`；
     带 `-beta`、`+413` 这类后缀会先归一化再比较。
   - 留空 = 任意宿主（Alpha 9 的行为，老包不受影响）。
   - 不匹配时模块**不启动**：状态显示「宿主版本不适用」，不算失败、不会弹错。
2. **安装前兼容预检**：安装时会先算作用域，不匹配**仍然允许安装**（你可能在为另一个 PCL 版本
   备包），但会立刻在日志里写明「模块声明 X，当前宿主 Y」，界面安装提示也带状态。`apiVersion`
   仍是硬拒绝（平台 API 契约）。
3. **宿主版本暴露**：`Api.Host.PclVersion` 读宿主程序集的 `ProductVersion`（PCL 为 `2.99.0.0`），
   退回 `FileVersion` / `AssemblyVersion`；模块可以据此自己做兼容判断。
4. **打包器补齐**：`PclAddonPack` 新增 `--shapes` / `--host-versions` / `--permissions`，
   生成的 `manifest.json` 直接带上作用域与权限声明。
5. **界面**：模块行的作用域文本改为换行显示，完整列出
   `API 范围 · 形状 · 宿主 · 权限名`（之前会被按钮挤到截断）。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| harness 全套 | `--addontest`（新增宿主版本作用域 4 项）/ `--pcltest`（新增 `Host.PclVersion`）/ `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| harness 作用域断言 | `">=本机版本"` 命中、`">=99.0"` 拒绝、`"*"` 放行；装一个 `hostVersions: [">=99.0"]` 的包 → 状态 `skipped-host`（不是 failed） |
| 真机 2.99.0（打包器产物） | 用 `PclAddonPack --shapes injected,patched --host-versions ">=2.99.0" --permissions …` 打出的包安装后直接启用（`状态=enabled`），说明 packer → manifest → 解析 → 作用域判定 → 启用整条链路通了 |
| 真机 2.99.0 自检 | `Host.PclVersion = 2.99.0.0`；`模块作用域判定 = 宿主 2.99.0.0：空约束=True，*=True，精确匹配=True，">=99.0"=False` |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha10.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha10.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha10.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
e243bc141496604cfd7140ce1f41f9fdb8d858cf2c2aed8f59214c6acad9759e  Pcl2Swap.Shim.x64.dll
e47181f3caf74ef05d96eebffcae59c26a4a48d152efb71ab274015954884ac7  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 用打包器打一个带作用域的包：
   `PclAddonPack --project <dir> --id demo --name Demo --version 1.0.0 --entry <type> --host-versions ">=2.99.0"`
   → 设置 → Swap Addon → 安装模块… → 模块行应显示 `宿主 >=2.99.0` 且状态「已启用」。
3. 把 `--host-versions` 改成 `">=99.0"` 再打一个：安装会成功，但状态是「宿主版本不适用」，
   主日志里能看到「安装的模块不在当前宿主作用域内（仍然安装）：…」。
4. 可选：`PCL2SWAP_SELFTEST=1` 启动后，日志里应出现 `Host.PclVersion = 2.99.0.0` 与
   `[selftest] ok 模块作用域判定 = …`。

## Known Limitations

- `hostVersions` 比较的是**宿主程序集版本字符串**，不是 PCL 的「版本代号」（413 之类的 build
  number）——需要精确到代号时用 `PclVersion` 自行判断。
- 作用域只在模块启动时判定：换 PCL 版本后需要重启 PCL（模块列表随之刷新）。
- 权限仍是声明与展示，不构成沙箱（模块与宿主同进程）。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
