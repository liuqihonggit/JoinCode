# TASK105 — ask 在后台任务中的临时关闭：现状取证与修复方案

> 状态：**P0 修复已实施（2026-11-23）；通知队列/Goal/AI Judge 已接线，Phase 2+ 见 DSG053** · 日期 2026-11-22
> 关联：[TASK103 S14](../../implemented/task/TASK103-ReAct循环合并-两套归一.md)（后台回调到达即关闭 ask）、[ADR 0162](../adr/0162-task-scoped-permission-overlay.md)（proposed，任务作用域权限覆盖）
> **统一方案**：[DSG053](../design/DSG053-unified-permission-decision-architecture.md)（硬规则 + AI 灰区判定 + 监控升级 + 按需 ask）。本文是 DSG053 Phase 1 的前置取证与最小闭环，Phase 2+ 的 AI Judge / `Autonomous` / `PendingConfirmationQueue` 以 DSG053 为准。
> 范围：`guard.permission` / `mcp.tool_dispatch` / `llm.agents.*` / `storage.clock.goal` / `native_tui` / `native_gui`
> 本仓库：`D:\project\w3`（下文行号均指该树，2026-11-22 快照）

---

## 0. 结论（先看这里）

**有问题，而且不是“少一个开关”，是三套“关闭 ask”机制各管一段、没有收口到任务作用域；真正跑后台长任务的那条链路在关键处没接上。**

三个 P0：

1. **Goal 长任务实际运行在 `Auto`，ask 没关**：生产路径 `StartViaPipelineAsync` 走 `GoalPermissionModeMiddleware`，Start 时把模式设成 `Auto`；真正切 `Unattended` 的 `StartDirectAsync` 在 pipeline 存在时永远不执行。
2. **后台 fork / resume 能拿到父代理完整工具表（含 `ask_user_question`）**：`UseExactTools=true` + `CacheSafeParams` 直接继承父工具表；`SubAgentOptions.PermissionMode` 只存不用，工具执行取进程全局模式。
3. **Goal 的 ask 绕过工具层，且 TUI 超时失效**：`GoalUserInteractionService` 直接调 `IInteractiveService`，不经过 SecurityClass / Unattended；`TuiInteractiveService` 忽略取消令牌，60 秒超时永远不触发。

一句话：**“临时关闭 ask”现在靠三样东西——全局权限模式、各 spawn 分支自己设的工具白名单、宿主 UI 的 Defer 探针；三者没有共享同一个“当前任务作用域”，所以每条后台链路都可能有缺口。**

---

## 0.1 实施记录（2026-11-23）

| 缺陷 | 落地改动 | 验证 |
|---|---|---|
| D1 Goal 生产路径 Auto | `GoalPermissionModeMiddleware` Start → `Unattended`；已是 Bypass/Unattended 则跳过；`GoalEngine` 日志改为 `{0} -> {1}` | Clock Goal 413 项通过 |
| D2 fork/resume 继承 ask | `BackgroundExecutionPolicy` 统一 `Mode=Unattended` + `DeniedTools=[ask_user_question, confirm_action]`；`AgentForkMiddleware`/`ForkSpawnMiddleware`/`AgentServiceImpl.Resume`/`ContextSetupMiddleware` 全部接入；fork/resume 保留父工具池仅剔除交互工具 | Hands fork/whitelist 8 项通过 |
| D2 模式无消费者 | `SubAgentContext.PermissionMode` 由 `PermissionChecker.ResolveScopedMode()` 与 `PermissionAwareToolExecutor` 消费，优先于全局模式 | MCP 77 项通过 |
| D3 Goal 绕过工具层 | `GoalUserInteractionService` 在 Unattended/Bypass 跳过 `IInteractiveService`，返回 Stop，避免 TUI 同步阻塞 | Clock Goal 测试通过 |
| 通知跨目标丢弃 | `AgentNotificationQueue` 按 target 分桶；`SessionController` 用 `_mainAgent.UniqueId` 消费；`BackgroundNotificationHandler` 用当前 AgentId | Infra queue 8 项通过 |
| 后台工具集空集合反向放大 | `EffectiveAllowedTools` 空交集回退默认白名单；`PreserveToolsExceptDenied` 空结果回退默认白名单 | 相关单测通过 |

**未完成**：Web GUI `DshUserQuestionBroker` Defer、`PendingConfirmationQueue`、TUI 取消令牌真机、手动 exe 验收（ADR 0080）。

---

## 1. 需求语义（先把“后台任务”说清）

用户诉求（2026-11-22）：**后台任务需要执行的时候，需要临时关闭 ask。**

本项目已有的权威语义（TASK103 S14，`docs/implemented/task/TASK103-ReAct循环合并-两套归一.md:314-341`）：

- 等待输入期间轮询两条件：输入就绪 → 正常读取；后台通知待处理 → **立即关闭本次提问返回 `Deferred`**；
- **不按时间关闭**（用户明确否决 30 秒空闲超时）；
- 绝不假定未作答问题的答案；处理后由模型重问；
- 非交互路径保持 `FailureResult`。

本任务覆盖三类“后台”：

| 类别 | 入口 | 现在的 ask 行为 |
|---|---|---|
| 长任务 / Goal | `GoalEngine` | 生产路径是 Auto → ask 可用 |
| 后台子代理 | `Agent(run_in_background)`、fork、resume | async 有白名单；fork/resume 无 |
| 后台通知 | shell / agent 完成入队 `IAgentNotificationQueue` | TUI/CLI/Avalonia 会 Defer；Web GUI 不会 |

---

## 2. 现有链路（证据表）

| 层 | 机制 | 代码位置 | 生效条件 | 缺口 |
|---|---|---|---|---|
| 权限模式层 | `Unattended` + `UnattendedDenied` | `modules/abstractions/abstractions.primitives.utils/src/constants/tool_names/UserInteractionToolName.cs:14-23`；`modules/guard/guard/permission/utils/AgentToolRestrictions.cs:47-57`；`modules/mcp/mcp_tool_dispatch/core/middleware/AgentRestrictionMiddleware.cs:51-64` | 当前模式**确实是** `Unattended` | 模式是进程全局；Goal 生产路径不是 Unattended |
| 后台 Agent 层 | 工具白名单 | `modules/cognition/hands_core/tool_handlers/system_tools/agent/AsyncAgentAllowedTools.cs:11-28`；`AgentBackgroundSpawnMiddleware.cs:49` | 只覆盖 `Agent(run_in_background)` 的 spawn | fork / resume 不覆盖 |
| UI 交互层 | 通知到达即 `Deferred` | `TASK103...md:314-341`；`native_tui/src/NativeTui.Core/repl/ReplShell.cs:946`；`native_gui/apps/NativeGui.Host/hosting/AvaloniaInteractiveService.cs:115`；`modules/apps/cli/core/interaction/TerminalInteractiveService.cs` | TUI / CLI / Avalonia | Web GUI（`DshUserQuestionBroker`）无 Defer |

---

## 3. 缺陷清单（按严重度）

### D1（P0）Goal 长任务生产路径仍是 Auto，ask 没关

| 证据 | 行为 |
|---|---|
| `modules/storage/clock/goal/core/goal_engine/GoalEngine.cs:320-380` | `StartViaPipelineAsync` 是 `_lifecyclePipeline != null` 时的 Start 路径 |
| `modules/storage/clock/goal/core/registry/PersistentGoalRegistry.cs:128-132` | 注入全部 `IGoalLifecycleMiddleware` → 生产环境 pipeline 必然存在 |
| `modules/storage/clock/goal/pipeline/middleware/GoalPermissionModeMiddleware.cs:29-38` | Start 时 `SetPermissionModeAsync(PermissionMode.Auto)`；Clear/MarkCompleted/MarkUnmet 时恢复 |
| `GoalEngine.cs:391-431`、`:921-951` | `StartDirectAsync` + `SwitchToGoalPermissionModeAsync` 才切 `Unattended`；pipeline 存在时是死代码 |

**影响**：Goal 期间 `ask_user_question` 仍被允许，对话框可随时弹出；长任务越久，撞上 ask 的概率越高。

**附带**：Goal 的两套 save/restore（`GoalEngine._savedPermissionMode` 与 `GoalLifecycleContext.SavedPermissionMode`）互相独立，存在恢复时覆盖用户手动切换的风险。

### D2（P0）后台 fork / resume 继承父代理完整工具表，ask 可达

| 证据 | 行为 |
|---|---|
| `modules/cognition/hands_core/tool_handlers/system_tools/agent/AgentForkMiddleware.cs:128-140` | 后台 fork：`UseExactTools=true`、`CacheSafeParams=父`、`RunInBackground=true`、`PermissionMode=Plan` |
| `modules/llm/agents.fork/ForkSpawnMiddleware.cs:88-102` | `AllowedTools = cacheSafeParams.ToolNames`（父代理整套工具，含 `ask_user_question`） |
| `modules/abstractions/abstractions.models/models/models_agent/agent/agent_core/SubAgentOptions.cs:41-42`；`modules/llm/agents.fork/AgentBase.cs:184-195` | `PermissionMode` 只存到 `SubAgentContext`，无消费者 |
| `modules/mcp/mcp_tool_dispatch/core/execution/PermissionAwareToolExecutor.cs:75-85` | 工具执行取 `PermissionManager.GetCurrentModeAsync()`（进程全局） |
| `modules/cognition/hands_core/tool_handlers/system_tools/agent/SubAgentControlToolHandlers.cs:147-151`；`AgentServiceImpl.cs:571-577` | 后台 resume 只传 `AgentId/NewPrompt/RunInBackground`，不传工具白名单/模式 |

**影响**：后台 fork 的 ask 会走同一宿主的 `IInteractiveService`，在 TUI/GUI 里直接弹卡片，用户不知道它来自后台；resume 后工具集回退到 definition/默认，也可能拿回 ask。

### D3（P0）Goal 的 ask 绕过工具层；TUI 超时失效

| 证据 | 行为 |
|---|---|
| `modules/storage/clock/goal/core/goal_engine/GoalUserInteractionService.cs:24-60` | 直接调 `_interactiveService.AskUserQuestionAsync(...)`，不经过 `AgentRestrictionMiddleware` / SecurityClass |
| `native_tui/apps/NativeTui.Host/TuiInteractiveService.cs:14-41` | 忽略 `cancellationToken`；`bridge.Ask(...)` 同步阻塞 |
| `native_gui/apps/NativeGui.Host/hosting/AvaloniaInteractiveService.cs`；`modules/apps/cli/core/interaction/TerminalInteractiveService.cs` | 这两个实现尊重取消令牌 |

**影响**：即使把工具层 ask 禁掉，Goal 仍能从侧门弹问；TUI 下 60 秒超时不触发，只能等按键或后台通知 Defer，Goal 循环可被无限挂住。

### D4（P1）Unattended 只认 Denied，Allowed 集是死代码

| 证据 | 行为 |
|---|---|
| `modules/guard/guard/permission/utils/AgentToolRestrictions.cs:60-79` | `IsToolAllowedForMode` 只查 `GetDeniedTools(mode)`；`GetAllowedTools(...)` 零调用方 |
| `AgentToolRestrictions.cs:36-45` | `GetAllowedTools(Unattended)` 直接返回 `_autoAllowed`；生成的 `UnattendedAllowedTools` 无人使用 |
| `UserInteractionToolName.cs:14-23` | `ask_user_question` 标 `PlanAllowed=false`，但不在 `PlanDeniedTools` → Plan 模式仍可 ask |

**影响**：`AutoAllowed / PlanAllowed / AskAllowed / UnattendedAllowed` 四个标记全部不生效，只有显式 `*Denied` 有用；以后新增交互工具只要漏标 `UnattendedDenied` 就会漏网。

### D5（P1）通知队列是全局单桶，误唤醒 + 丢通知

| 证据 | 行为 |
|---|---|
| `modules/abstractions/infrastructure/pipeline/core/AgentNotificationQueue.cs:51` | `HasPendingNotifications => !_queue.IsEmpty`，不区分目标代理/会话 |
| `AgentNotificationQueue.cs:32-48` | `DequeueAll(currentAgentId)` 出队所有条目，只把匹配的加入结果；**其他目标的通知被丢弃** |
| `modules/abstractions/abstractions.primitives.interfaces/src/application/AgentNotificationQueueExtensions.cs:13` | 现网调用 `DequeueAll()` 无过滤，latent 丢弃问题尚未触发 |

**影响**：TUI/CLI/Avalonia 的 Defer 探针用全局“非空”判定 → 无关后台通知会误关用户正在回答的问题；一旦有人按 target 过滤，其他 agent 的通知会静默丢失。

### D6（P1）Web GUI 没有 Defer

| 证据 | 行为 |
|---|---|
| `native_gui/src/NativeGui.Core/web_host/DshUserQuestionBroker.cs:75-125` | 发 `user-questions/request` 后等待 `$events/result`；只有轮次取消 / 断连才会结算 |
| `native_gui/src/NativeGui.Core/web_host/` 全目录 | 未接 `IAgentNotificationQueue` |

**影响**：Web GUI 的 ask 卡片挂起期间，后台回调无法抢占；与 TASK103 的“后台始终可读”语义不一致。

### D7（P2）提示词层与权限层互相打架

| 证据 | 行为 |
|---|---|
| `modules/cognition/prompts/sections/system/FactInquirySection.cs:6-33` | 关键词触发就要求“信息不足必须追问”，无模式判断，且写的是 `ask_user` 非真实工具名 |
| `modules/cognition/prompts/sections/rules/SessionGuidanceSection.cs:16-18` | `EnabledTools` 含 `ask_user_question` 就注入 ask 引导 |
| 项目 `AGENTS.md` | “每次回复结束前必须调用 `ask_user` 工具”会被带进后台子代理提示词 |

**影响**：模型在无人值守/后台上下文里仍被提示“必须追问”，随后被工具层拒绝，浪费回合；或绕过工具走 D3 的侧门。

### D8（P2）拒绝文案误导

| 证据 | 行为 |
|---|---|
| `modules/mcp/mcp_tool_dispatch/core/middleware/AgentRestrictionMiddleware.cs:51-64` | 把 mode-denied 集合作为 `AllAgentDisallowedTools` 传入 `ToolFilterContext`（第 5 参） |
| `modules/guard/guard/permission/utils/ToolFilterPolicy.cs:22` | 该层拒绝原因文案是“工具被全局禁用（防递归）”，不是“无人值守禁用交互工具” |

**影响**：模型不知道是模式导致，容易换命令/换工具重试；违反 AGENTS.md 的“错误必须有诱导”。

### D9（P2）测试假安全

| 证据 | 行为 |
|---|---|
| `modules/cognition/hands.tool_handlers.tests/agent/AsyncAgentAllowedToolsTests.cs:20-23` | 断言 `"ask_user"` / `"AskUser"`，真实工具名是 `ask_user_question` → 永远绿 |
| `modules/mcp/mcp_tool_dispatch.tests/middleware/AgentToolRestrictionsUnattendedTests.cs:42-49` | 把“Unattended 继承 Auto allowed”写成期望，锁死 allowed 集失效的现状 |

### D10（P3）默认交互实现伪造答案

| 证据 | 行为 |
|---|---|
| `modules/cognition/brain_planning/planning/services/InteractiveService.cs:27-72` | 默认（Mock）实现返回 `"user answer"` / `Options[0].Label`，注释自认“自动选第一项” |
| `modules/storage/vault/user_interaction/UserInteractionService.cs` | headless 下 `AskQuestionAsync` 返回 `(true, "headless-auto-approved")` |

**影响**：在没有宿主覆盖的引擎/SDK/headless 环境里，ask 不是被关闭，而是被**伪造答案**；模型拿假用户决策继续执行，比阻塞更危险。

---

## 4. 调研前置四问

### 4.1 专家隐性实践（顶尖权限/并发/交互工程师会做、用户没要求的事）

1. **权限作用域化**：并发任务（前台 + 后台 + 子代理）的安全语义不能用进程全局可变状态表达；必须 per-task/per-agent 覆盖，否则“临时关闭”必然影响别人。
2. **工具可见性与执行门禁同源**：模型看到的工具表、提示词里的工具引导、执行层拒绝必须吃同一份 effective tool set；否则会出现“看得见、调了被拒”。
3. **后台 fail-closed**：无人值守/无宿主时交互工具应直接不可见或明确失败，绝不 mock 自动应答（伪造同意比拒绝更危险）。
4. **唤醒按 target 路由**：后台通知队列必须按目标 agent/session 分区，广播式 `HasPendingNotifications` 会造成 cross-talk。
5. **事件驱动而非超时**：TASK103 已由用户拍板；任何“空闲 N 秒关闭”都是回退。
6. **拒绝可解释**：拒绝必须回答“为什么/哪个层/怎么改”，否则模型会换命令绕过（AGENTS.md 第 9 条）。

### 4.2 原始素材清单

- **用户原始决策**：`docs/implemented/task/TASK103-ReAct循环合并-两套归一.md:314-341`（S14 设计 + 四列验收 + 真机记录）。
- **DSH 问答宿主语义**：`docs/proposed/task/TASK066-GUI全组件DSH对接与TUI差异收敛.md`（卡片阻塞、cancel 帧）。
- **权限层唯一数据源**：`SecurityClassAttribute.cs` + `UserInteractionToolName.cs` + 生成的 `ToolSecuritySets.g.cs`（`UnattendedDeniedTools` / `PlanDeniedTools`）。
- **本仓库源码**：`GoalEngine.cs`、`GoalPermissionModeMiddleware.cs`、`AgentForkMiddleware.cs`、`ForkSpawnMiddleware.cs`、`AgentBackgroundSpawnMiddleware.cs`、`AsyncAgentAllowedTools.cs`、`AgentRestrictionMiddleware.cs`、`AgentToolRestrictions.cs`、`AgentNotificationQueue.cs`、`TuiInteractiveService.cs`、`AvaloniaInteractiveService.cs`、`DshUserQuestionBroker.cs`、`GoalUserInteractionService.cs`。
- **禁止只看**：本文件自身、任何 AI 总结、已过期的 TASK 结论（例如把 Goal 写成 Unattended 的旧注释）。

### 4.3 具体方法与工具

1. **三段追踪**：每个工具/模式做“定义（SecurityClass）→ 传递（SubAgentOptions/ForkOptions/Scope）→ 消费（middleware/executor）”逐行核对；判定“无消费”以全仓 `rg` 零命中为准。
2. **红测试**：先写会失败的测试锁定现状（见 §5 Phase 0），再改代码。
3. **真机**：TUI PTY + MockServer，首轮并行 `bash(run_in_background)` + 后台 fork；观察 ask 是否被弹出、是否 Defer；用 `JCC_DEBUGLOG=1` 打 `[PERM-SCOPE]` 标签确认入口命中。
4. **全仓编译**：跨 guard/mcp/llm/storage 的契约变更后跑 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`。

### 4.4 独立验证来源

1. **真实 TUI / GUI 交互**：无按键场景下后台回调到达，对话框应自动关闭并返回 `Deferred`（不是 mock）。
2. **审计日志**：`Unattended` 自动执行 + audit 记录（已有 `DangerousCommandProtectionMiddleware.cs:350-364`）。
3. **既有测试基线**：`NativeTui.Tests`（AskQuestionTests）、`Mcp.Tests`（UserInteractionToolHandlersTests）、`Brain.Context.Tests`（BackgroundNotificationHandlerTests）、`Guard.Security.Tests`。
4. **全仓编译产物**：`Abstractions.Guard.dll` / `Mcp.ToolDispatch.dll` 含新 scope 类型与拒绝文案。

---

## 5. 修复方案（分阶段）

> 原则：先补红测试锁定现状，再按 P0 → P1 → P2 渐进；每阶段独立编译+测试+提交。跨模块决策见 ADR 0162。

### Phase 0 — 红测试（先复现，不改代码）

| # | 测试 | 期望（现状为红） |
|---|---|---|
| T0-1 | `GoalStart_SetsUnattended`：`GoalEngine.StartViaPipelineAsync` 后读 `PermissionManager.GetCurrentModeAsync()` | 期望 Unattended；现状 Auto（红） |
| T0-2 | `BackgroundFork_ToolSet_ExcludesAsk`：`ForkSpawnMiddleware` 产物 `AllowedTools` 不含 `ask_user_question` | 现状含（红） |
| T0-3 | `ResumeBackground_ToolSet_ExcludesAsk` | 现状不保证（红） |
| T0-4 | `GoalInteraction_Unattended_DoesNotPrompt`：Unattended 下 `GoalUserInteractionService` 直接 CoordinatorTakeover | 现状仍调 `IInteractiveService`（红） |
| T0-5 | `NotificationQueue_TargetFilter_DoesNotDropOthers`：A 目标出队不影响 B 目标 | 现状 B 被丢弃（红） |
| T0-6 | `AsyncAgentAllowedTools_DoesNotContain_AskUserQuestion`：用真实工具名 | 现状通过（补真名后仍应绿） |

### Phase 1 — 最小闭环（P0）

1. **Goal 权限模式单源**（需先定语义，见 §9 Q1）：
   - 若 Goal = 无人值守：`GoalPermissionModeMiddleware` 改为 `Unattended`，删除 `StartDirectAsync` 内的重复切换与 `_savedPermissionMode`，只保留一处 save/restore；
   - 若 Goal = 可交互：删除 `StartDirectAsync` 的 Unattended 分支与注释，明确“Goal 允许 ask”，并把 Goal 的 ask 纳入 `GoalUserInteractionService` 模式判断。
2. **后台 fork/resume 强制白名单**：在 `AgentForkMiddleware.ExecuteForkPathAsync` 与 `SubAgentControlToolHandlers.ResumeFromTranscriptAsync` 注入 `BackgroundExecutionPolicy`（见 Phase 2），最低限度把 `AllowedTools` 收窄到 `AsyncAgentAllowedTools.Tools`。
3. **Goal 侧门**：`GoalUserInteractionService` 在调用前检查当前模式/作用域；Unattended 直接 `CoordinatorTakeover`，不弹窗；TUI 实现补 `ct` 观察（`WaitAsync(ct)` 或桥内取消）。

### Phase 2 — 架构收敛（P1，ADR 0162 落地）

1. **`PermissionScope`（AsyncLocal 覆盖）**：
   - `ToolExecutionContext.AgentMode` 解析顺序：`PermissionScope.CurrentMode ?? PermissionManager.GetCurrentModeAsync()`；
   - `AgentRestrictionMiddleware` 合并 `PermissionScope.DeniedTools`；
   - 后台任务入口用 `using var scope = PermissionScope.Unattended("background")`，退出即恢复，不动全局。
2. **`BackgroundExecutionPolicy` 统一入口**：async spawn / fork / resume / team / cron / goal 共用一处，输出 `{ mode, allowedTools, deniedTools, reason }`；不变量：`ask_user_question`、`confirm_action` 永不出现在后台工具集。
3. **EffectiveToolSet 单源**：工具表构建、prompt `EnabledTools`、执行门禁三处消费同一份结果；`ToolPermissionFilter.FilterToolsByDenyRules`（当前零调用方）接入工具表构建。
4. **通知队列分桶**：`HasPendingNotifications(targetAgentId)`；`DequeueAll` 不再丢弃非目标条目（按 key 分 Channel 或取出后回队）。

### Phase 3 — 宿主与提示词（P2/P3）

1. `DshUserQuestionBroker` 接入 `IAgentNotificationQueue`，卡片挂起期间收到目标通知 → cancel 帧 + `Deferred`。
2. `FactInquirySection` / `SessionGuidanceSection` 增加模式判断：Unattended / 后台 scope 下不注入 ask 引导；`ask_user` 改为真实工具名 `ask_user_question`。
3. `AgentRestrictionMiddleware` 拒绝文案改为可诱导：“无人值守模式禁用交互工具 `ask_user_question`；请继续执行，或把待确认项记录后由前台回合询问。”
4. `Core.Planning.InteractiveService` 改为 fail-closed（`FailureResult("no interactive host")`），Mock 只留在测试；`UserInteractionService` headless 不再 auto-approve。

---

## 6. 变更文件清单（预估）

| 文件 | 变更 |
|---|---|
| `modules/storage/clock/goal/pipeline/middleware/GoalPermissionModeMiddleware.cs` | 单源化 Goal 模式（Unattended 或显式 Auto），删除双 save/restore |
| `modules/storage/clock/goal/core/goal_engine/GoalEngine.cs` | 删除 `StartDirectAsync` 重复切换 / 或改为 scope |
| `modules/storage/clock/goal/core/goal_engine/GoalUserInteractionService.cs` | 模式检查 + 尊重 ct |
| `modules/cognition/hands_core/tool_handlers/system_tools/agent/AgentForkMiddleware.cs` | 后台 fork 注入 BackgroundExecutionPolicy |
| `modules/cognition/hands_core/tool_handlers/system_tools/agent/SubAgentControlToolHandlers.cs` | resume 注入白名单 |
| `modules/llm/agents.coordinator.core/AgentServiceImpl.cs` | 批量 resume 注入白名单 |
| `modules/guard/guard/permission/utils/AgentToolRestrictions.cs`（或新增 `PermissionScope`） | 作用域覆盖 + 合并 denied |
| `modules/mcp/mcp_tool_dispatch/core/middleware/AgentRestrictionMiddleware.cs` | 消费 scope + 拒绝文案 |
| `modules/abstractions/infrastructure/pipeline/core/AgentNotificationQueue.cs` | 按 target 分桶，修复丢弃 |
| `native_gui/src/NativeGui.Core/web_host/DshUserQuestionBroker.cs` | 接入后台通知 Defer |
| `native_tui/apps/NativeTui.Host/TuiInteractiveService.cs` | ct 观察 |
| `modules/cognition/prompts/sections/system/FactInquirySection.cs`、`rules/SessionGuidanceSection.cs` | 模式感知 + 真实工具名 |
| `modules/cognition/brain_planning/planning/services/InteractiveService.cs`、`modules/storage/vault/user_interaction/UserInteractionService.cs` | fail-closed |
| 对应测试项目 | Phase 0 红测试 + 真实名断言 + E2E |

---

## 7. 需求落地四列验收表（强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `PermissionScope`（任务作用域 mode/denied） | `ToolExecutionContext.AgentMode`、`AgentRestrictionMiddleware` | ❌ | ❌ 计划：单测（嵌套/并发/恢复）+ TUI 真机 |
| `BackgroundExecutionPolicy`（唯一后台入口策略） | async spawn / fork / resume / team / cron / goal | ❌ | ❌ 计划：每个入口一条工具表断言 + 真机 |
| EffectiveToolSet（工具表/prompt/门禁同源） | 工具表构建、`EnabledTools`、`QueryEngine` 门禁 | ❌ | ❌ 计划：`--dump-tools` 产物对比 + 交互拒绝 E2E |
| 通知队列按 target 分桶 | `TuiAskBridge`、`TerminalInteractiveService`、`AvaloniaInteractiveService`、`DshUserQuestionBroker` | ❌ | ❌ 计划：A/B 目标并发单测 + TUI PTY 真机 |
| Goal 模式单源 | `GoalPermissionModeMiddleware` / `GoalEngine` | ❌ | ❌ 计划：Goal start 读 mode 断言 + 长任务真机 |
| Goal 交互侧门守卫 | `GoalUserInteractionService` → `NodeCompletionPipeline` | ❌ | ❌ 计划：Unattended 下不弹窗单测 + TUI 真机 |
| 提示词模式感知 | `FactInquirySection` / `SessionGuidanceSection` | ❌ | ❌ 计划：`/prompt` 产物断言（Unattended 无 ask 段） |
| 拒绝文案诱导 | `AgentRestrictionMiddleware` | ❌ | ❌ 计划：错误结果文本断言 |
| 真实工具名测试 | `AsyncAgentAllowedToolsTests` 等 | ❌ | ❌ 计划：红→绿 |

> `已验收`列必须走 ADR 0080 手动 exe 验收（真实运行非 mock）。

---

## 8. 风险与回滚

| 风险 | 说明 | 缓解 |
|---|---|---|
| 行为变更 | Goal 期间不再弹 ask，用户可能感觉“没被通知” | 在 TUI/GUI footer 显示“无人值守（Goal）”+ 审计日志；停止条件仍可由 heartbeat/协调者接管 |
| 作用域泄漏 | AsyncLocal 在 detached 线程/长生命周期回调里丢失 | 只在 spawn/Goal 入口设置；子任务用 `Task.Run`（默认捕获 ExecutionContext）；加 `[PERM-SCOPE]` 打点验证 |
| 队列改造 | 现网 `DrainFormatted()` 无过滤，改分桶需同步改调用方 | 先保留无参重载（排空全部当前会话），再逐步迁移 |
| 工具表收窄过度 | 后台 fork 可能需要写文件/跑命令 | 白名单 = `AsyncAgentAllowedTools.Tools`，只排除交互类；不用 `Denied=*` |

---

## 9. 待决策问题（评审时必须回答）

1. **Goal 长任务的产品语义**：`Unattended`（完全无人值守，禁止 ask，红灯自动执行+审计）还是 `Auto`（允许 ask 澄清）？——决定 Phase 1 是“改成 Unattended”还是“保留 Auto 并只在交互侧门加超时/Defer”。
2. **后台 fork 是否一律禁止 ask**（推荐是）：若某些场景需要“后台任务向用户确认”，应改为“返回待确认项，由前台回合统一问”，而不是后台直接弹。
3. **Web GUI 是否纳入后台通知抢占 ask**（推荐是）：若暂不纳入，需要在文档明确“Web GUI ask 只阻塞本会话，后台通知延迟到下一轮”。

---

## 10. 备注

- 本文是**静态链路取证 + 修复方案**，未改任何代码；所有行号可在 2026-11-22 快照上复核。
- 若评审通过，按 Phase 0 先落红测试；跨模块契约（`PermissionScope` / `BackgroundExecutionPolicy`）先落 ADR 0162 再实施。
