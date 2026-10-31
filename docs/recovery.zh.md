# PCL2 Swap 安全模式与启动恢复

> 目标：当某个 Addon 导致 PCL 在启动早期崩溃或无法进入稳定窗口时，下一次启动仍能进入
> **Swap 管理页**，并把所有 Addon 隔离开，而不是让用户反复遇到同一个回环崩溃。

## 1 启动哨兵

SwapHost 每次启动都会写入：

```
<数据根>\recovery\startup-journal.json
```

字段包括本会话 id、PCL 版本、交付形状、模块清单、启动尝试序号、未稳定次数和安全模式状态。
写 journal 时先标记 `stable=false`，宿主完成启动并保持窗口存活约 12 秒后，后台心跳才改成
`stable=true` 并清零未稳定次数。

因此：

- 正常启动超过稳定窗口 → 下一次启动按正常启动处理；
- 连续两次在稳定窗口前退出或崩溃 → 第三次启动进入安全模式；
- 安全模式本身也会被写入 journal，直到用户明确清除。

## 2 安全模式行为

安全模式当前会话中：

- 所有 Addon（包括 Download MC / OTA Protector / Cancel Button 和第三方模块）都不执行
  `OnLoad/OnEnable`；
- 所有已安装模块仍出现在 Swap Addon 列表，状态显示「安全跳过」；
- `ISwapApi.Host.SafeMode` 为 true，`SafeModeReason` 给出原因；
- PageRegistry、Swap Settings、恢复卡片和其他宿主基础 UI 仍然可用，便于修复。

安全模式不会删除任何包、配置或日志。

## 3 手动恢复

打开 `设置 → Swap Addon`，在恢复卡片中：

1. **退出安全模式（重启后生效）**：清除持久化旗标，但当前会话仍保持安全；
2. 关闭并重新启动 PCL；
3. Addon 按 `modules.json` 中原来的启用状态加载。

也可以手动删除或编辑：

```
<数据根>\recovery\startup-journal.json
```

## 4 测试开关

自动化测试可以使用：

| 环境变量 | 作用 |
|---|---|
| `PCL2SWAP_SAFE_MODE=1` | 强制本次启动进入安全模式 |
| `PCL2SWAP_RECOVERY_STABLE_MS=1000` | 缩短稳定窗口，最低 1000 ms |

## 5 当前边界

- 安全模式只负责 SwapHost 管理下的 Addon；PCL 自身的网络、更新、账户流程不在其范围内。
- 当前会话清除安全模式不会热加载模块；这是刻意设计，避免恢复后立刻再次崩溃。
- journal 位于用户数据目录；不同 `PCL2SWAP_DATA` 环境彼此隔离。
