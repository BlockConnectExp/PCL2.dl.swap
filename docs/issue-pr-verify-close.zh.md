# Issue → PR → Verify → Close

> 本机首个完整问题闭环的可复用流程。目标不是“提交一个看起来像修复的 diff”，而是让问题单、
> 代码、回归证据、真实宿主证据、PR 合并和 issue 关闭形成同一条可审计链。

---

## 1. 建立可追踪上下文

```powershell
gh issue view <number> --repo BlockConnectExp/PCL2.dl.swap --comments `
  --json number,title,state,author,body,comments,url
git status --short --branch
git remote -v
```

在 `_devdocs/<timestamp>-issue-<number>-TODO.md` 建立检查表。先只读问题、评论、当前分支、
远端 release/tag 与工作区状态，不根据标题直接下结论。

## 2. 推导与伪证

使用项目广播指定的顺序：

```text
推导 -> 伪证 -> 复现 -> 找同类 -> 推伪证 -> 逐个修 -> 再验证
```

最低证据集合：

1. 官方宿主反编译与 IL/metadata 位置；
2. 当前版本与一个能力正常的旧版基线对照；
3. 实际调用点与异常文本的全程序扫描；
4. 不只检查报告指出的方法，还检查同文案、同 closure、同 catch 的相邻门；
5. 每一条假设都要有证伪实验或明确证据边界。

## 3. 分支与备份

```powershell
git branch backup/issue-<number>-pre-fix <base>
git switch -c fix/issue-<number>-<slug> <base>
```

备份 ref 只用于回退定位，不覆盖用户已有改动。所有提交和 tag 使用 SSH 签名。

## 4. 修复约束

- 优先恢复宿主原有实现或调用原生入口，不复制一套看似相似、实际行为不同的安装器。
- 功能挂在现有用户开关下；关闭开关必须回退宿主原行为。
- 同类路径一次修复：菜单、拖拽、资源页如果共用同一方法，以调用链证明覆盖。
- 新增测试必须包含反例：未命中门、关闭开关、错误格式或旧版无门宿主不能误伤。
- 不在测试中修改官方 EXE、日常 PCL 2.12、PCL CE 或真实用户数据。

## 5. 验证门

```powershell
dotnet build .\src\PclDlSwap.Payload\PclDlSwap.Payload.csproj -c Release
dotnet build .\src\PclDlSwap.ExLoaderHarness\PclDlSwap.ExLoaderHarness.csproj -c Release
.\src\PclDlSwap.ExLoaderHarness\bin\Release\net48\PclDlSwap.ExLoaderHarness.exe `
  --<focused-test> .\dist\PclDlSwap.Payload.dll
.\scripts\build.ps1
.\src\Pcl2Swap.Shim\build.ps1 -SkipPayloadBuild
```

发布级修复继续执行全部 harness、offset manifest、双官方宿主注入和真实 UI 路径。证据写入
`test/issue<number>-evidence/`，至少包括：

- harness 终端输出；
- 双宿主 `pcl2swap.log`；
- 文件选择器/目标页面的窗口截图或等价可观察证据；
- 双宿主 JSON 汇总；
- 修复前后行为差异。

## 6. PR 与合并

PR 标题应描述行为变化，正文必须包含：

```markdown
Fixes #<number>

## Root cause
## Changes
## Verification
## Risk / compatibility
```

推送前检查：

```powershell
git diff --check
git status --short
git diff --stat
git log -1 --show-signature
```

PR 创建后先复核 diff 与 checks，再合并。不要把“PR 已创建”当作“问题已修复”。

## 7. 关闭审计

合并后执行：

```powershell
gh pr view <pr> --repo BlockConnectExp/PCL2.dl.swap `
  --json number,state,mergedAt,mergeCommit,url,statusCheckRollup
gh issue view <number> --repo BlockConnectExp/PCL2.dl.swap `
  --json number,state,stateReason,closedAt,url,comments
```

只有以下条件同时满足才关闭：

1. PR 的 merge commit 已落到目标分支；
2. issue 状态为 `CLOSED`，原因指向修复；
3. 关闭评论包含 commit、PR、测试和真实宿主证据；
4. 远端目标分支、tag/release 状态没有被误改；
5. 本机工作区干净，没有遗留测试进程或未关闭隧道。

<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->
