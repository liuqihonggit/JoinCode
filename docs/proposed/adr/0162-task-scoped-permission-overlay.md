# 0162 — 任务作用域权限覆盖与后台执行策略统一

- 状态：**Phase 1 已实施（2026-11-23）**
- 实施说明：Phase 1 未新建 `PermissionScope` 类型；任务作用域由既有 `SubAgentContext`（AsyncLocal）承载 `PermissionMode/AllowedTools/DeniedTools`。Goal 仍通过全局 `PermissionManager` 切 `Unattended`（Goal 与主回合共享会话，独立 scope 留待 Phase 2）。
- 日期：2026-11-22
- 关联：[TASK105](../task/TASK105-ask后台任务临时关闭-现状取证与修复方案.md)、[TASK103 S14](../../implemented/task/TASK103-ReAct循环合并-两套归一.md)、[DSG053](../design/DSG053-unified-permission-decision-architecture.md)（本 ADR 是统一权限判定架构的“作用域”基础组件）
- 影响域：`guard.permission`、`mcp_tool_dispatch`、`llm.agents.*`、`storage.clock.goal`、`native_tui`、`native_gui`、`apps.cli`

---

## 背景

“后台任务执行时临时关闭 ask”目前靠三套机制拼起来：

1. **进程全局的 `PermissionManager` 模式**（`Unattended` 才 deny `ask_user_question`）；
2. **各 spawn 分支自己设的工具白名单**（只有 `Agent(run_in_background)` 设了 `AsyncAgentAllowedTools`，fork/resume 没设）；
3. **宿主 UI 的 `Deferred` 探针**（TUI/CLI/Avalonia 有，Web GUI 没有）。

这三套机制没有共享同一个“当前任务作用域”，导致：

- Goal 生产路径（`StartViaPipelineAsync` + `GoalPermissionModeMiddleware`）实际把模式设成 `Auto`，真正切 `Unattended` 的 `StartDirectAsync` 是死代码；
- 后台 fork / resume 继承父代理完整工具表，能拿到 `ask_user_question`；
- Goal 的 `GoalUserInteractionService` 直接调 `IInteractiveService`，绕过工具层；
- 全局模式切换会污染同进程的其他并发执行体，并在恢复时覆盖用户手动切换。

根因是**权限语义只有“进程全局”一个维度**，无法表达“这个任务期间临时 Unattended，任务结束恢复，且不影响别人”。

---

## 决策

引入两层新契约：

### 1. 任务作用域覆盖（实际实现）

- Phase 1 复用 `SubAgentContext` 的 AsyncLocal：`PermissionMode + AllowedTools + DeniedTools`；
- 解析顺序：`SubAgentContext.Current` 字段优先于 `PermissionManager.GetCurrentModeAsync()`；`PermissionChecker` 与 `PermissionAwareToolExecutor` 都走同一解析；
- 后台 spawn/fork/resume 在创建子代理时写入字段，`AgentBase.EnterScopeWithCwd` 进入 AsyncLocal 作用域，退出即恢复；
- 未改 `PermissionManager` 全局模式语义（用户基线 / `/permission-mode` 手工设置仍有效）；Goal 的全局切换作为特例保留。

> Phase 2 目标态：独立 `PermissionScope` record（mode/judge/allowAsk/denied/reason），让所有层读同一份；Phase 1 的 `SubAgentContext` 字段是其迁移基础。

### 2. `BackgroundExecutionPolicy`（唯一后台入口策略）

所有后台执行入口（async spawn、fork、resume、team）统一经它计算：

```
{ mode = Unattended, allowedTools = 后台白名单（async）/ 父工具池（fork/resume）, deniedTools = [ask_user_question, confirm_action] }
```

不变量：**交互类工具永不出现在后台工具集里**。前台任务不受影响。

- `EffectiveAllowedTools`：async agent 收敛默认白名单（防止空集合反向放大）；
- `PreserveToolsExceptDenied`：fork/resume 保留父代理工具池，仅剔除交互工具，避免误删 Git 等能力。

配套的 `EffectiveToolSet`（工具表/prompt/门禁三处同源）仍属 Phase 2；Phase 1 已把 `AllowedTools/DeniedTools` 注入执行上下文与权限检查，先保证“模型可见的工具 = 执行允许的工具”主路径。

---

## 备选方案（考虑过但放弃）

### A. 只修 Goal 双源，保留全局模式

- 做法：把 `GoalPermissionModeMiddleware` 改成 `Unattended`，删掉 `StartDirectAsync` 的重复切换。
- 放弃原因：只能解决 Goal；fork/resume 仍可 ask；全局模式仍会污染并发任务与用户手动切换。治标不治本。

### B. 每个会话一个 `PermissionManager` 实例

- 做法：把单例改为 per-session 注册。
- 放弃原因：同一会话内仍然无法区分“前台交互”与“后台任务”；而且 Goal 与主回合共享会话，语义还是不够。

### C. 任务作用域覆盖（本 ADR 选择）

- 优点：并发安全、恢复无副作用、后台入口一处收口、可扩展到未来的 team/cron/subagent。
- 代价：需要改 `ToolExecutionContext` 的解析点、`AgentRestrictionMiddleware` 的合并逻辑、所有后台 spawn 入口；有一次性迁移成本。

---

## 影响

| 模块 | 变更 |
|---|---|
| `guard.permission` | `PermissionChecker` 解析 `SubAgentContext` scoped mode；`AgentRestrictionMiddleware` 消费 allowed/denied；拒绝文案改为可诱导 |
| `mcp_tool_dispatch` | `PermissionAwareToolExecutor` 解析 scoped mode 并注入 `ToolExecutionContext.AllowedTools/DeniedTools`；`AgentRestrictionMiddleware` 消费三层 |
| `llm.agents.*` | `AgentSpawnOptions/SubAgentOptions` 新增 `DeniedTools/PermissionMode`；fork/resume/context-setup 统一写入 |
| `storage.clock.goal` | Goal 权限模式单源为 `Unattended`；`GoalUserInteractionService` 在 Unattended/Bypass 跳过交互 |
| `native_tui` / `native_gui` / `apps.cli` | `IInteractiveService` 取消令牌与 Web GUI Defer 仍按 TASK103/TASK105 待做 |
| `abstractions.infrastructure` | 通知队列按 target 分桶，修复跨目标丢弃 |

---

## 验收

以 [TASK105 §7 四列验收表](../task/TASK105-ask后台任务临时关闭-现状取证与修复方案.md) 为准：

- `SubAgentContext` mode/allowed/denied 解析单测（fork/resume/后端 spawn）；
- 每个后台入口一条“工具表不含 ask”断言（`BackgroundExecutionPolicy.DeniedTools`）；
- Goal start 读 `GetCurrentModeAsync()` 断言 + Unattended 不弹交互；
- TUI PTY 真机：后台回调到达时 ask 自动 Defer（无按键）— **未做**；
- 全仓编译 `Build-AllDomains.ps1` 本轮跑过一次通过，后续按用户要求改为按需编译。

---

## 后果

**正面**：

- “临时关闭 ask”成为任务级语义，不再靠全局副作用；
- 后台 fork/resume/Goal 全部 fail-closed；
- 用户手动切换权限模式不会被后台任务恢复覆盖。

**负面 / 风险**：

- `PermissionScope` 依赖 `AsyncLocal` 传播，detached 线程/长生命周期回调可能丢失；需在入口打 `[PERM-SCOPE]` 并用单测覆盖。
- 行为变更：Goal 期间不再弹 ask，需在 UI 显示“无人值守（Goal）”并提供审计记录，避免用户误以为被忽略。

---

## 待决策

Goal 长任务的产品语义（Unattended vs 可交互 Auto）未定，见 TASK105 §9 Q1；本 ADR 以“Goal = Unattended”为推荐路径，评审时如改为 Auto，则 `BackgroundExecutionPolicy` 只覆盖 fail-closed 的后台子代理，Goal 走交互侧门（超时 + Defer）。
