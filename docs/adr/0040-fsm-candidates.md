# 0040 候选清单 — 值得改造为企业级状态机的代码位置

> 配合 [ADR 0040](0040-enterprise-fsm-framework.md)（企业级状态机框架 — 转换表 + 守卫 + 共享上下文）
>
> 扫描日期：2026-08-29 | 扫描范围：全代码库 117 个状态枚举 + 13 个 *Transitions.cs + 4 个 StateMachine 实现

## 现状基线

### 已有状态机基础设施

| 组件 | 位置 | 已有能力 | 缺失能力（ADR 040 要求） |
|------|------|----------|--------------------------|
| `StateMachine<TState>` | `foundation/Abstractions/00-core/core/Utils/State/StateMachine.cs` | 转换表、线程安全、StateChanged/TransitionFailed 事件、终态检测 | **守卫、事件枚举、共享上下文、OnEnter/OnExit/OnUpdate、Action** |
| `AgentStateMachine` | `core/ai/Agents/src/Coordinator/Core/AgentStateMachine.cs` | 复用 StateMachine + AgentStateContext 上下文 + 转换历史 | 守卫、事件枚举 |
| `TaskStateMachine` | `core/execution/Scheduling/src/Core/TaskStateMachine.cs` | 复用 StateMachine | 守卫、事件枚举、上下文 |
| `DownloadStateMachine` | `infrastructure/Infrastructure/Network/Downloader/StateMachine/DownloadStateMachine.cs` | **操作枚举 + IsAllowed 守卫**（最接近 ADR 040） | 共享上下文、OnEnter/OnExit、未复用 StateMachine 基础设施 |

### 已有 13 个静态转换表（*Transitions.cs）

仅有 `CanTransitionTo` 静态方法，无状态机实例、无守卫、无事件枚举：

`BridgeMainLifecycleTransitions`、`PluginHostStateTransitions`、`VoiceStateTransitions`、`PlanTransitions`（PlanState + PlanStep）、`QueryStateTransitions`、`ForkStateTransitions`、`PatientStateTransitions`、`ServiceStateTransitions`、`GoalStateTransitions`、`OnboardingStateTransitions`、`BridgeSessionTransitions`、`BackgroundTaskStateTransitions`

---

## 候选清单（按优先级排序）

### P0 — 最高优先级（ADR 040 明确点名 + 复杂守卫 + 共享上下文）

#### 1. ShannonEntropyDetector ⭐⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/execution/Brain/src/Context/Services/Loop/ShannonEntropyDetector.cs` |
| 当前实现 | 手写 `switch _state` + 3 个 HandleXxx 方法 + if-else 守卫 |
| 状态枚举 | `EntropyDetectionState`（Monitoring/Suspected/Confirmed，已 [Flags]） |
| 事件枚举（建议） | `EntropyEvent { Decline, Timeout, Confirm, Recover }` |
| 守卫 | `isDeclining`（declineStreak >= threshold）、`elapsed > confirmationWindow` |
| 共享上下文 | `_firstTriggerTime`、`_triggerCount`、`_entropyHistory`（RingBuffer） |
| 改造收益 | ADR 040 明确点名；转换表显式化；守卫从 if-else 提取为委托；上下文统一管理 |
| 测试 | 已有单元测试，改造后行为不变 |

#### 2. UnifiedCircuitBreaker ⭐⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `infrastructure/Infrastructure/Utils/Resilience/UnifiedCircuitBreaker.cs` |
| 当前实现 | 手写 `switch currentPhase` + if-else 守卫 + lock |
| 状态枚举 | `CircuitBreakerPhase`（Closed/Open/HalfOpen） |
| 事件枚举（建议） | `CircuitBreakerEvent { RecordSuccess, RecordFailure, TryProbe, OpenTimeout }` |
| 守卫 | `consecutiveFailures >= failureThreshold`、`elapsed >= openDuration`、`halfOpenProbeCount < halfOpenMaxProbe` |
| 共享上下文 | `_consecutiveFailures`、`_openedAt`、`_halfOpenProbeCount`、`_totalFailures`、`_totalSuccesses` |
| 改造收益 | 经典熔断器状态机教科书案例；转换表显式化；HalfOpen 探针限流守卫提取 |
| 测试 | 已有单元测试 |

#### 3. LoopInterventionMiddleware（InterventionLevel） ⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/execution/Brain/src/Context/Services/LoopInterventionMiddleware.cs` |
| 当前实现 | `ClassifyIntervention(triggerCount)` switch 表达式 |
| 状态枚举 | `InterventionLevel`（None/Soft/Hard/Compact） |
| 事件枚举（建议） | `InterventionEvent { LoopDetected, ProgressMade, ThresholdReached, Reset }` |
| 守卫 | `triggerCount` 在某区间、`hasProgressed` |
| 共享上下文 | `loopTriggerCount`、`effectiveTriggerCount`、`hasProgressed` |
| 改造收益 | ADR 0018 提到干预级别用枚举 + 决策方法；状态机化后漏斗级别转换显式 |

---

### P1 — 高优先级（已用 StateMachine.cs 但缺守卫/事件枚举，或已接近 ADR 040 模式）

#### 4. DownloadStateMachine ⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `infrastructure/Infrastructure/Network/Downloader/StateMachine/DownloadStateMachine.cs` |
| 当前实现 | **已有操作枚举（DownloadOperation）+ IsAllowed 守卫**，但自己实现未复用 StateMachine.cs |
| 改造 | 统一到 ADR 040 框架，复用 StateMachine 基础设施，加共享上下文（下载进度、字节计数） |

#### 5. LspServerInstance ⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `services/Eyes/src/Lsp/Internal/Server/LspServerInstance.cs` |
| 当前实现 | 已用 `StateMachine<LspServerState>`，守卫逻辑（maxRestarts 检查）分散在调用方 |
| 事件枚举（建议） | `LspEvent { Start, Started, Stop, Stopped, Crash, Recover, ExceedMaxRestarts }` |
| 改造 | 提取守卫到转换表；加事件枚举；OnEnter/OnExit 钩子处理连接/断开 |

#### 6. AgentStateMachine ⭐⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/ai/Agents/src/Coordinator/Core/AgentStateMachine.cs` |
| 当前实现 | 已用 StateMachine.cs + AgentStateContext，但无守卫、无事件枚举 |
| 事件枚举（建议） | `AgentEvent { Start, Pause, Resume, Complete, Fail, Cancel, Retry }` |
| 改造 | 加事件枚举 + 守卫（如 Retry 次数限制）；OnEnter/OnExit 记录时间戳 |
| **状态** | **跳过** — API 为 `TryTransitionAsync(agentId, newState)` 目标状态驱动，调用方 8 处直接传 `TaskExecutionStatus` 值；改事件驱动需改所有调用方 + 测试，收益不抵成本（减法思维 ADR 0023） |

#### 7. TaskStateMachine ⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/execution/Scheduling/src/Core/TaskStateMachine.cs` |
| 当前实现 | 已用 StateMachine.cs，无守卫、无事件枚举、无上下文 |
| 改造 | 加事件枚举 + 守卫（如依赖满足才能 Pending→Running） |
| **状态** | **跳过** — API 为 `TryTransitionTo(targetState)` 目标状态驱动，本质是"目标状态校验"非"事件驱动转换"；强行加事件枚举是过度设计（减法思维 ADR 0023） |

---

### P2 — 中优先级（有状态枚举但未用状态机，逻辑较简单）

#### 8. MonitorMcpTask（MonitorState） ⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/execution/Scheduling/src/Tasks/MonitorMcpTask.cs:46` |
| 当前实现 | 直接赋值 `session.State = MonitorState.Starting`，**无转换校验** |
| 状态枚举 | `MonitorState`（Starting/Running/Stopped/Error） |
| 改造 | 加转换表 + 守卫，防止非法状态跳转 |

#### 9. CompactOutputGuard（CompactFallbackLevel） ⭐

| 项 | 内容 |
|----|------|
| 位置 | `core/execution/Brain/src/Context/Compact/Guard/CompactOutputGuard.cs` |
| 当前实现 | if-else 链返回不同 CompactFallbackLevel |
| 状态枚举 | `CompactFallbackLevel`（None/Sanitize/Microcompact/Truncate/Abort） |
| 改造 | 降级链状态机化，转换表显式化降级路径 |

#### 10. GlobalRunStatusViewModel（StallDetectionState） ⭐

| 项 | 内容 |
|----|------|
| 位置 | `app/JoinCodeGui/ViewModels/GlobalRunStatusViewModel.cs` |
| 状态枚举 | `StallDetectionState`（Monitoring/Stalled）— **仅 2 状态** |
| 改造收益 | 较低（状态太少），但可统一模式 |

---

### P3 — 低优先级（已有 *Transitions.cs 静态转换表，按需升级）

以下 13 个已有静态转换表，可按需升级为完整状态机（有复杂守卫/上下文的优先）：

| # | 转换表 | 位置 | 升级触发条件 |
|---|--------|------|-------------|
| 11 | `BridgeMainLifecycleTransitions` | `services/Bridge/src/Session/Main/` | 加守卫（如 Running→ShuttingDown 需等待活跃会话） |
| 12 | `PluginHostStateTransitions` | `infrastructure/Infrastructure/Plugins/Plugins/` | 加守卫（如 Loaded→Unloaded 需无活跃引用） |
| 13 | `VoiceStateTransitions` | `core/execution/Hands/src/Voice/` | 加共享上下文（音频缓冲、录音时长） |
| 14 | `PlanStateTransitions` + `PlanStepTransitions` | `core/execution/Brain/src/Planning/Planning2/ToolHandlers/` | 加守卫（如 Draft→Executing 需审批通过） |
| 15 | `QueryStateTransitions` | `core/execution/Brain/src/Query/Query2/Transitions/` | 加守卫（如查询超时、结果就绪） |
| 16 | `ForkStateTransitions` | `core/ai/Agents/src/Coordinator/Fork/` | 加守卫（如 Running→Merged 需父代理确认） |
| 17 | `PatientStateTransitions` | `core/ai/Agents/src/Doctor/` | 加守卫（诊断流程医疗语义） |
| 18 | `ServiceStateTransitions` | `composition/Clock/src/Hosting/` | 加守卫（如 Stopping→Stopped 需等待请求排空） |
| 19 | `GoalStateTransitions` | `composition/Clock/src/Goal/Core/` | 加守卫（如目标完成需所有子目标完成） |
| 20 | `OnboardingStateTransitions` | `app/JoinCode/Cli/Onboarding/` | 较简单，升级收益低 |
| 21 | `BridgeSessionTransitions` | `services/Bridge/src/Session/` | 加守卫（会话生命周期） |
| 22 | `BackgroundTaskStateTransitions` | `core/execution/Hands/src/SystemActuator/Abstractions/` | 加守卫（如后台任务取消需清理资源） |

---

## 不建议改造

| 枚举 | 原因 |
|------|------|
| `StallDetectionState`（仅 2 状态） | 状态太少，状态机框架开销大于收益 |
| `ConversationMode` | E2E 测试用，非生产代码 |
| `EffortLevel`、`DisclosureLevel`、`ComplexityLevel`、`CommandRiskLevel`、`DangerLevel` | 纯标记/级别枚举，无状态转换 |
| `AgentState`（Tui）、`SubAgentRunState`、`StatusKind`、`CliExperienceMode`、`EditorMode`、`PresentationMode`、`VimMode`、`SnipMode`、`NotebookEditMode`、`DiffViewMode`、`SlashCompletionMode`、`AskUserSelectionStatus` | UI/ViewModel 绑定枚举，非状态机 |
| `DebugLogLevel`、`StatusSymbol`、`MessageStatus`、`TelemetryStatusCode` | 纯展示/日志枚举 |
| `TransportMode`、`BridgeSpawnMode`、`BridgeSpawnModeSource`、`SearchOutputMode`、`ModelModalityKind`、`RouteMatchMode`、`PermissionMode`、`PermissionLevel`、`DeviceTrustLevel`、`AgentIsolationMode`、`SkillExecutionMode`、`WorkflowExecutionMode`、`ExecutionMode`、`VcrMode` | 配置/模式选择枚举，无动态转换 |
| `TodoStatus`、`PlanStatus`（已有 Transitions）、`GoalStatus`（已有 Transitions）、`WorktreeEntityStatus`、`BashProcessStatus`、`ShellCommandStatus`、`CodeSessionStatus`、`DreamTaskStatus`、`DreamPhase`、`SubAgentSummaryStatus`、`SubAgentEnvelopeState`、`DoctorReportStatus`、`McpProgressStatus`、`ReconnectStatus`、`PluginUnloadStatus`、`CrashSnapshotState`、`SystemActuatorCommandStatus`、`AsyncHookProcessStatus`、`BridgeStatusState`、`BridgeConnectionState`、`BridgeSubprocessStatus`、`BridgeState`、`SessionStatus`、`BridgeSessionStatus`、`PeerSessionStatus`、`McpConnectionStatus`、`BridgeLifecycleState`、`TransportConnectionState`、`SshConnectionState`、`NetworkConnectivityState`、`SandboxExecutionState`、`SandboxHealthState`、`CursorState`、`ElementState`、`PluginFiberState`、`LongRunningTaskState`、`TaskExecutionStatus`（已被 AgentStateMachine 用）、`AgentStatus`、`PermissionRequestStatus`、`VoiceRecordingState`（已有 Transitions）、`ElicitMode`、`McpReconnectAcceptLevel`、`SystemActuatorLifecycleState`、`ForkState`（已有 Transitions）、`PatientState`（已有 Transitions）、`StepState`、`ToolStatus`、`SubAgentRunState`、`LenientLevel`、`TestState` | 已有 Transitions 或纯标记或被其他高优先级覆盖 |

---

## 改造路径建议

### 阶段 1：框架实现（ADR 0040 accepted）

1. 在 `foundation/Abstractions/00-core/core/Utils/State/` 扩展 `StateMachine<TState>` 或新建 `Fsm<TState, TEvent>`
2. 增加：`TEvent` 事件枚举约束、`TransitionGuard` 委托、`FsmContext` 共享上下文、`IFsmState` 接口（OnEnter/OnUpdate/OnExit/OnEvent）
3. 转换表升级为 `Dictionary<TransitionKey, TransitionRule>`，`TransitionKey = (FromState, Event)`，`TransitionRule = (Target, Guard, Action)`
4. DI 注册替代反射扫描（AOT 兼容）
5. 单元测试 + AOT 编译验证

### 阶段 2：P0 候选改造（3 个）

1. `ShannonEntropyDetector` → 事件驱动状态机
2. `UnifiedCircuitBreaker` → 事件驱动状态机
3. `LoopInterventionMiddleware` → 干预级别状态机

### 阶段 3：P1 候选改造（4 个）

4. `DownloadStateMachine` → 统一到 ADR 040 框架
5. `LspServerInstance` → 提取守卫 + 事件枚举
6. `AgentStateMachine` → 加事件枚举 + 守卫
7. `TaskStateMachine` → 加事件枚举 + 守卫

### 阶段 4：P2/P3 按需改造

根据实际需求逐步升级，不一次性重构。

---

## 决策依据

- ADR 0040 明确点名 ShannonEntropyDetector 和命令拦截
- `UnifiedCircuitBreaker` 是经典三态熔断器，状态机教科书案例，改造收益最高
- `DownloadStateMachine` 已最接近 ADR 040 模式（有操作枚举 + 守卫），统一成本最低
- P3 的 13 个 *Transitions.cs 已有转换表基础，升级为完整状态机只需加守卫 + 上下文，按需进行
- 不建议改造的枚举共 ~60 个，多为纯标记/UI 绑定/配置选择，无动态状态转换语义

<!-- 🤖 Auto Decision: 2026-08-29 -->
<!-- 决策: 将候选分为 P0/P1/P2/P3 四级优先级，P0 为 ADR 0040 明确点名 + 经典状态机案例 -->
<!-- 原因: 避免一次性大规模重构（AGENTS.md 渐进式原则），优先改造收益最高、风险最低的候选 -->
<!-- 替代方案: 全部 117 个枚举逐一评估改造 — 放弃，大部分是纯标记枚举无状态转换语义 -->
<!-- 验证: 文档分析，未改代码，无需编译 ✅ -->
