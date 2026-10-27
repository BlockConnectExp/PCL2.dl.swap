# 本地真机 E2E（无需 ExLoader）

用于在没有 ExLoader、甚至**在你自己的 PCL 正开着**的情况下，对注入形状做端到端验证。

## 1. 注入器

`tools/PclInject`（net48 x64）：把 shim 用 `LoadLibraryW` 送进真实 PCL 进程。

```powershell
# 先启动一份 PCL 副本，等它把 CLR/窗口拉起来（约 6 秒）再注入；
# 在进程挂起时注入会拿到 0x80131023（CLR 尚未就绪）。
$p = Start-Process .\test\e2e-parallel\Pcl2SwapE2E.exe -PassThru
Start-Sleep 6
.\tools\PclInject\bin\Release\net48\PclInject.exe --pid $p.Id `
  --dll .\dist\exloader\Pcl2Swap.Shim.x64.dll
```

## 2. 绕过单实例（可与 owner 的 PCL 并行）

PCL 用命名互斥体 `PCL_SingletonMutex` 做单实例。给**测试副本**打等长字符串补丁即可并行：

```powershell
$bytes = [IO.File]::ReadAllBytes($copy)
$old = [Text.Encoding]::Unicode.GetBytes('PCL_SingletonMutex')      # 18 字符
$new = [Text.Encoding]::Unicode.GetBytes('PCL_SwapE2EMutex1')        # 同样 18 字符
# 逐字节替换（UTF-16LE），写回副本；正式产物不要打这个补丁
```

补丁只用于 `test\e2e-parallel\Pcl2SwapE2E.exe`（与现场 2.99.0 构建同源）。

## 3. 隔离环境与自检

```powershell
$env:TEMP = "$pwd\test\e2e-temp"          # shim 解包/日志按进程 TEMP 走
$env:PCL2SWAP_DATA = "$env:TEMP\data"     # 模块状态与 Addon 的持久根（默认 %APPDATA%\PCL2Swap）
$env:PCL2SWAP_SELFTEST = '1'              # 启动后 5 秒在宿主 UI 线程跑一遍只读自检
```

自检覆盖 Host / Surface / Reflect / Secret / Animation / Settings / Base / Instance /
Download / Launch，并列出每个模块的启用状态；结果全部落在
`%TEMP%\PCL2Swap\<版本>\pcl2swap.log`。实测（2.99.0，注入形状）：`ok=24 fail=0`。

## 4. UI 交互

窗口是每显示器 DPI 感知的：脚本里先 `SetProcessDPIAware()`，再用
`GetWindowRect` + `SetCursorPos` + `mouse_event` 点窗口相对坐标。
2.99.0 参考坐标（窗口 1125x688，125% DPI）：

| 目标 | 窗口内坐标 |
|---|---|
| 顶部「设置」 | 613, 54 |
| 顶部「其他」 | 810, 54 |
| 侧栏 Swap Addon | 100, 243 |
| 侧栏 Swap Settings | 100, 302 |
| 第一行模块的开关按钮 | 994, 245 |

## 5. 判定标准

1. `pcl2swap.log` 出现 `[selftest] 完成：ok=N fail=0`；
2. `[page] 模块状态变化` / `Addon 已启用|禁用` 与 `modules.json` 一致；
3. 离开再回到「设置」不出现 `未知的设置子页面种类`；
4. PCL 自身 `PCL\Log1.txt` 无 `程序出现未知错误|InvalidCastException|切换主要页面失败`。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
