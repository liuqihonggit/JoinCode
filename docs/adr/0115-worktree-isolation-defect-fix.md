# 0115. Worktree 隔离缺陷修复 — cwd 污染 + Teammate 不隔离

- 状态：proposed
- 日期：2026-09-19
- 决策者：用户 + AI

## 背景

调查发现子代理 git worktree 隔离存在两个实际风险缺陷(对标 ClaudeCode ts Issue #76250 和 Agent Teams 不隔离问题):

### 缺陷 1: 跨 worktree 的 cwd 污染

**根因**: `SubAgentContext.GetEffectiveCwd()` 是死代码(无调用方),shell 执行链路使用进程级 `Directory.GetCurrentDirectory()`:

- `kit/hands/shell/handlers/ShellToolHandlers.cs:65` → `_fs.GetCurrentDirectory()`(进程级)
- `kit/hands/system_actuator/abstractions/SystemActuatorBase.cs:253` → `_fs.GetCurrentDirectory()`(进程级)
- `kit/hands/system_actuator/abstractions/CwdTracker.cs:33` → `_fs.SetCurrentDirectory()` → `Directory.SetCurrentDirectory()`(进程级全局)

**触发链**: 子代理 A 执行 `cd /worktreeA` → `CwdTracker` 修改进程级 cwd → 子代理 B 的 shell 命令(未传 working_directory)跑到 worktreeA

**对比 ClaudeCode ts**: ts 用 `runWithCwdOverride`(AsyncLocalStorage)正确隔离,不污染父代理。本项目有 AsyncLocal 机制但未接入 shell 链路。

### 缺陷 2: Agent Teams 不做 worktree 隔离

**根因**: `WorktreeDecisionPolicy.Decide()` 实现了正确逻辑(Teammate 应开 worktree),但未集成到生产 spawn 链路:

- `lib/scheduling/tasks/core/InProcessTeammateTask.cs:145` → `IsolationMode = AgentIsolationMode.None`(默认)
- `lib/infrastructure/hot_spot/WorktreeDecisionPolicy.cs:58` → `Decide()` 仅测试调用
- `llm/agents/Coordinator/Team/core/TeammateStatusBuilder.cs:102` → `WorktreePath = null`(硬编码)

## 决策

### 缺陷 1 修复: shell 链路读 GetEffectiveCwd

让 `ShellToolHandlers` 和 `SystemActuatorBase` 在 `working_directory` 为空时,优先读 `SubAgentContext.GetEffectiveCwd()` 而非 `_fs.GetCurrentDirectory()`:

```
working_directory 为空时:
  SubAgentContext.GetEffectiveCwd(_fs.GetCurrentDirectory())
  = SubAgentContext.Current?.CwdOverride ?? _fs.GetCurrentDirectory()
```

这样子代理的 AsyncLocal `CwdOverride` 会被 shell 链路读取,实现流式 cwd 隔离。

### 缺陷 2 修复: 集成 WorktreeDecisionPolicy 到 spawn 链路

在 `AgentForkMiddleware` 创建 Teammate 时,调用 `WorktreeDecisionPolicy.Decide()` 决定隔离模式,而非直接用 `AgentIsolationMode.None` 默认值:

```
IsolationMode = context.Isolation 显式传值
  ?? _worktreeDecisionPolicy.Decide(enableWorktree, ExecutorVariant.Teammate)
```

用户显式传值优先,未传则由决策策略根据全局开关 + Variant 决定。

## 替代方案(考虑过但放弃)

### A1. 改 CwdTracker 不修改进程级 cwd

放弃原因: CwdTracker 的进程级 cwd 跟踪是给主代理用的(单 session 场景),子代理应通过 AsyncLocal 隔离。两者不冲突,修复方向是让 shell 链路优先读 AsyncLocal。

### A2. 改 InProcessTeammateTask 默认值为 Worktree

放弃原因: 硬编码默认值不够灵活,应通过 `WorktreeDecisionPolicy` 根据全局开关 + Variant 动态决策。直接改默认值会绕过决策策略。

## 验证

- TDD: 先写 E2E 红测试复现缺陷 → 单元红测试定位根因 → 修复 → 绿测试
- 手动验证: 用启动参数实际运行 exe,验证子代理 cwd 隔离和 Teammate worktree 隔离

## 影响

- `ShellToolHandlers` / `SystemActuatorBase` — cwd 解析逻辑变更
- `AgentForkMiddleware` — Teammate 隔离模式决策变更
- 无后向兼容要求(AGENTS.md: 无后向兼容)
