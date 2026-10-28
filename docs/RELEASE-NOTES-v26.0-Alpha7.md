# PCL2.dl.swap v26.0-Alpha 7

> Seventh public Pre-Release: the swap grows a **module manager**. `.swapaddon` packages can now be
> installed, updated and uninstalled from inside the launcher - no restart, no manual folder
> juggling - and a module's declared **scope** (`shapes`) is finally enforced. This is the piece that
> turns the addon format from "a folder you drop files into" into an LSPosed-style manager.

---

## Highlights since Alpha 6

1. **安装 / 更新 / 卸载都在界面里**
   - 设置 → Swap Addon → **模块管理 → 安装模块…**：选包 → 校验 manifest → 归档到
     `%APPDATA%\PCL2Swap\addons\<id>-<版本>.swapaddon` → 解包 → **立即** `OnLoad/OnEnable`、
     注册设置页并重建侧栏。装完就能用，**不需要重启 PCL**。
   - 同一个 id 再装即热更新：旧版先停、页面撤下，模块自己的 `config.json` 保留。
   - 第三方模块行多一个红色 **卸载**：确认后停止模块、注销它的设置页（若你正停在那页，会切回
     宿主的默认设置页）、删除模块目录（含 config）与归档包。你自己 Downloads 里的原包不动——
     平台只删自己复制进模块目录的东西。
2. **延迟删除**：入口 DLL 已被 CLR 加载时删除必然失败，所以被占用的路径写进
   `<数据根>\addons\.pending\*.txt`，下次启动先清理再扫描模块。真机验证：卸载后重启，
   `test.sample\1.3.0` 与归档包一起消失。
3. **作用域（shapes）真正生效**：`manifest.json` 里 `shapes: ["injected"]` 的模块在磁盘补丁形状下
   状态显示「作用域不适用」——不启动、也不算失败；`*` 表示全形状。
   **顺带修掉一个一直存在的老 bug**：manifest 解析用 `Convert.ToString` 取数组，`shapes` 与
   `permissions` 实际永远是空数组（拿到的是 `System.Object[]` 这个名字）。现在按原始值解析，
   模块行会显示 `作者 · API 版本范围 · 形状 · 权限数`。
4. **页面注销**：模块注册的设置页现在带命名空间（`<模块id>/<页id>`）并记在模块记录上，
   卸载只撤它自己的页面；页面 id 不复用，残留的 PageID 由 PageGet 兜底成宿主默认页。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| harness 全套 | `--addontest`（含新增 10 项管理器断言）/ `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` / `--pcltest` 全部 PASS |
| 真机 2.99.0：热装 | `PCL2SWAP_E2E_INSTALL=<包>` → 日志 `Addon 已解包` → `Addon 已启用` → `[e2e] 安装结果：test.sample 1.3.0 状态=enabled`；侧栏立刻变成 3 项，模块页可打开（`PageID 102`，逻辑 id `test.sample/sample.demo`） |
| 真机 2.99.0：界面卸载 | 点行内「卸载」→ 宿主原生确认框 → 「已注销“示例模块”（PageID 102）」→ 侧栏回到 2 项 → 列表刷新 → `modules.json` 剔除该模块 |
| 真机 2.99.0：延迟删除 | 卸载时 DLL 被占用 → 写入 `.pending` 标记；重启后 `待删除模块文件已清理完成`，模块目录与包文件消失 |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha7.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha7.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha7.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
c7e796c0b972a68da29d6475fd4e62655b488988806bee6c8e033b30ac9e8443  Pcl2Swap.Shim.x64.dll
3f7101669d16355a46fe09348176cb12b5feb882af0580d856f5f12251807f41  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → **Swap Addon** → 模块管理 → 安装模块…：选一个 `.swapaddon`（例如用
   `tools/PclAddonPack` 打出来的示例）→ 列表立刻出现该模块、侧栏出现它的页面。
3. 点该行的 **禁用/启用**：钩子类拦截立即放行/恢复（日志与界面都会写明结构类修复需重启）。
4. 点 **卸载**：确认框 → 行消失、侧栏页面消失、`%APPDATA%\PCL2Swap\addons\<id>` 被清掉；
   若日志提示 DLL 被占用，重启后再看一次，`.pending` 会被清空。
5. `manifest.json` 里写 `"shapes": ["patched"]` 的模块在注入形状下应显示「作用域不适用」。

## Known Limitations

- 卸载是**行为级**的：gate 留在宿主入口处并直通原方法，物理跳转要等进程结束。
- 结构类修复（原版游戏页重驱 / 下载侧栏恢复）仍要在重启后才完全回退。
- 被锁住的模块文件要等下一次启动清理；清理发生在模块扫描之前，所以不会“复活”。
- 模块与宿主同进程共享 CLR：按定义是受信任代码，没有沙箱隔离。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking (installing a `.swapaddon` reads only the file you pick on disk). The only local
processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
