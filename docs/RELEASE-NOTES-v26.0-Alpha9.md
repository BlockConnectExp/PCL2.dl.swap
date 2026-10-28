# PCL2.dl.swap v26.0-Alpha 9

> Ninth public Pre-Release: modules get their **own log file** and a failing module can no longer
> take the launcher down with it. A module handler that throws is now deactivated on the spot, the
> host call falls back to the original method, and the failure is counted in the module list and
> written to that module's log.

---

## Highlights since Alpha 8

1. **每模块日志（LSPosed 式“模块日志”页签）**
   - `Api.Log / LogWarning / LogError` 与平台代挂的每个钩子都写进
     `%APPDATA%\PCL2Swap\logs\modules\<模块id>.log`；全局日志同一行带 `[<模块id>]` 前缀，
     “这行是谁写的”不用再猜。
   - 模块行新增 **日志** 按钮：直接打开该文件；模块还没写过日志时打开日志目录并给出提示。
   - 256 KB 轮转一次（保留 `.1` 备份），卸载模块时它的日志一起删除。
2. **崩溃隔离**
   - handler 抛异常时立刻捕获并**停用该 handler**：Replace 模式回退到下一个 Replace handler
     或宿主原方法，Prefix/Postfix 直接跳过。
   - 失败次数记在模块列表里（红色「钩子抛错 N 次（失败的钩子已自动停用，详见该模块日志）」），
     细节写进模块日志。
   - 修复前 `DynamicInvoke` 会把异常直接抛进宿主方法，PCL 会弹「程序出现未知错误」。
3. **权限声明可见**：模块行不再只写权限数量，而是列出最多三个权限名
   （`权限 host.settings.write、host.hooks.attach`）。文档同时讲清楚：模块与宿主同进程共享 CLR，
   `Reflect`/`Surface` 本来就是逃生口，`permissions` 是**声明与展示**，不是沙箱边界。

## 已实测（本轮）

| 场景 | 结果 |
|---|---|
| harness 全套 | `--addontest`（新增 4 项日志/隔离断言）/ `--pcltest` / `--pagetest` / `--scantest` / `--loaderhooktest` / `--surfacetest` 全部 PASS |
| harness 隔离断言 | 抛错的 Replace handler：宿主调用返回原方法结果（`Ping(3) == 6`）、handler 只被调用一次（已停用、不重试）、`ScopedHook.ErrorsFor` 计数为 1 |
| harness 日志断言 | 每个模块有自己的日志文件；日志里有平台代挂的钩子（`挂钩 …`）；日志里有 handler 抛错记录；卸载后日志被删除 |
| 真机 2.99.0 | 安装示例模块后 `logs\modules\test.sample.log` 实录「示例模块已加载…」「挂钩 PCL.ModBase.ToUnixTimestampUtc（Prefix，hookId sample.demo.prefix）」；界面每行都有「日志」按钮，第三方模块行多了红色「卸载」 |

## Artifacts

| File | Purpose |
|---|---|
| `Pcl2Swap.Shim.x64.dll` | the ExLoader injection shim (upload this on the injector card) |
| `Pcl2Swap.Shim.x64.dat` | the same build as a decimal byte dump for ExLoader's userFiles encoding |
| `PCL2.dl.swap-v26.0-Alpha9.zip` | shim + dat + `SHA256SUMS.txt` |
| `source-v26.0-Alpha9.zip` | source snapshot of this release |
| `PCL2.dl.swap-Loader-v26.0-Alpha9.zip` | the disk-patch shape (Loader + Patcher + Payload + dependencies), no ExLoader needed |

SHA-256:

```
762e0c6a9b025037936d9f6c568bf237849a51e37550a145fcb7e596e63e2b8b  Pcl2Swap.Shim.x64.dll
890edc0770f246e518780a23d53f1de9c60e2c86f4235b5cc5d7d1f1c73c14a8  Pcl2Swap.Shim.x64.dat
```

## How to verify

1. Re-import `Pcl2Swap.Shim.x64.dll` in ExLoader and start PCL (fully close PCL first).
2. 设置 → **Swap Addon**：每行都有「日志」按钮；点开第三方模块的日志应能看到它的加载、挂钩与
   任何它自己写的行。
3. 装一个会抛错的模块：宿主方法仍按原样返回（不会弹「程序出现未知错误」），模块行出现红色
   「钩子抛错 N 次」，对应模块日志里能看到该异常的类型与消息。
4. 卸载模块后，`%APPDATA%\PCL2Swap\logs\modules\<模块id>.log` 也会消失。

## Known Limitations

- 日志是**追加写**：模块频繁输出会以 256 KB 为界轮转一次，`.1` 之后的内容不保留。
- 隔离只到“handler 级”：一个 handler 抛错后停用该 handler，不会卸载整个模块
  （模块其余功能继续工作，模块行会显示错误计数）。
- 模块与宿主同进程共享 CLR：没有沙箱，`permissions` 只是声明。

**Network**: the module itself makes no network connections - PCL keeps doing all of its own
networking. The only local processes it may start are the on-disk malware scanners you enable.

---

License: MIT

GitHub@BlockConnectExp

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
