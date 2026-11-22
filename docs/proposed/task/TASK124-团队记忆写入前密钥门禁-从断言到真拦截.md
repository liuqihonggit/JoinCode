# TASK124 — #50 团队记忆写入前密钥门禁：从「已接线但恒不触发」到真拦截

> 状态：已闭合 · 日期：2026-11-23 · 基线：`w2` @ 待提交（#45 提交之后）
> 前置：[TASK119](TASK119-团队记忆密钥扫描-取证与上传门禁.md) 已补出站门禁（上传前扫描），本项补的是**入站/落盘前**。

## 一、症状与一手证据

TASK119 的结论是「上传前已拦、写入前未拦」。按交接文档去加 `validateInput` 时才看清：
**写入链早就接好了，坏的是守卫自己。**

- 写入链已挂检查：`FileToolHandlers.Write.cs:24`、`FileToolHandlers.Edit.cs:28/115/189/350`、
  `FileToolHandlers.Patch.cs:31`、`NotebookToolHandlers.cs:193`、`BundledSkillToolHandlers.cs:643`
  全部 `.Then(_writeDefense.CheckTeamMemSecrets)`；
- `WriteDefenseService.CheckTeamMemSecrets` → `SecretGuardNode` → `ITeamMemSecretGuard` 链路完整；
- 但 `TeamMemSecretGuard` 的构造参数是 `string? teamMemDirectory = null`，
  **全仓没有一处传入**（`grep "new TeamMemSecretGuard"` 零命中），
  DI 走 `ServiceDescriptor.Singleton<TeamMemSecretGuard, TeamMemSecretGuard>()`，可空参数取默认 → `null`；
- 于是 `IsTeamMemPath` 第一行 `if (string.IsNullOrWhiteSpace(_teamMemDirectory)) return false;` 恒真，
  `TryGetTeamMemSecretViolation` 永远返回 `false` —— **门禁常年敞开**。

**根因不是忘记接线，是契约形状不对**：团队目录按 `teamId` 变化（`<memdir>/team-memories/teams/<teamId>/…`），
但接口按「单个团队目录字符串」设计，没有任何一个点能提供这个值。

## 二、修法

1. **抽出布局唯一真相源** `JoinCode.Abstractions.Interfaces.TeamMemoryLayout`
   （`modules/abstractions/memory/memdir/TeamMemoryLayout.cs`）：
   `ResolveRoot(memdirStoragePath) => <memdir>/team-memories`，`RootDirectoryName` 常量集中一处。
2. **守卫改为按 memdir 根解析**：`TeamMemSecretGuard(WorkflowConfig? config = null)`，
   根 = `TeamMemoryLayout.ResolveRoot(config?.MemdirPath ?? AppDataConstants.Paths.MemdirDirectory)`。
   `WorkflowConfig` 与 `AppDataConstants` 都在 abstractions 层且在 `Guard.Security` 的 GlobalUsings 内，
   不引入 guard → storage 的反向依赖。
3. **路径判定改标准前缀**：去尾分隔符后判「等于根」或「根 + 分隔符」，
   `team-memories-evil` 这类前缀攻击不匹配。
4. **顺带消除布局漂移**：`TeamMemoryPaths` 原来在 `StoragePath` 为空时退回 `~/.jcc/team-memories`，
   而 `MemdirOptions.StoragePath` 默认是 `~/.jcc/memdir` → 同一概念两套默认目录。
   现在两处都走 `TeamMemoryLayout`，删掉 `GetDefaultTeamBaseDirectory()`。
5. **错误信息只回规则名**：`matches.Select(m => m.RuleId)`（原 `m.Label` 会掺人读文案），
   不回显密钥原文 —— 这条信息会进模型上下文与日志。

## 三、验收

单测（`guard.security.tests/defense/TeamMemSecretGuardTests.cs`，8 项）：
路径命中/根自身/兄弟前缀/域外、团队文件命中密钥（且断言不回显原文）、干净内容放行、非团队区放行、默认配置根。

| 套件 | 结果 |
|------|------|
| `Guard.Security.Tests` | 1264/1264 |
| `Vault.Memdir.Tests`（路径 + 同步筛选） | 33/33 |
| `Hands.ToolHandlers.Tests`（defense） | 11/11 |
| `Build-AllDomains.ps1 -Changed` | 2 个项目全部通过 |

真机（`mcp_serve` + `JCC_PERMISSION_MODE=bypass`，`write` 工具三态）：

```text
1) 团队目录 + 假密钥 → execution_failed
   "Content contains potential secrets (aws-access-token) and cannot be written to team memory…"
   suggestions: 移除密钥内容或使用允许的写入方式
2) 团队目录 + 普通文本 → File created successfully
3) 团队目录之外 + 假密钥 → File created successfully（个人区不受影响）
```

第 1 条同时证明两件事：拦截真的触发，且只回规则名 `aws-access-token`、不含密钥文本。
探针产生的两个文件已删除，`~/.jcc/memdir` 未留残留。

**过程教训**：第一次真机调用被 `PD002 用户拒绝工具执行: write` 拦下 —— 那是权限层（无人确认即拒绝），
不是我们的门禁；不设 `JCC_PERMISSION_MODE=bypass` 会把「权限层拒绝」误当成「密钥门禁生效」。

## 四、残余

- 拒绝结果的展示沿用通用 `execution_failed`；TUI/GUI 若要「安全」专用样式，属 UI 层另立项。
- 出站门禁（TASK119）保留 —— 纵深两层：落盘前拦新写入，上传前拦历史遗留文件。
- `memory_add_team_path` 等工具只登记路径、不写内容，无需加扫描。
