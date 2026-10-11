# 0121. 引擎层统一子代理状态源 — GUI 双模型合并

- 状态：accepted
- 日期：2026-09-30
- 决策者：用户 + AI

## 背景

GUI 层存在两套并行的子代理模型，数据源分裂：

1. `BackgroundAgentItemVm` — 管理面板行 VM，从引擎 `IAgentService.GetRunningAgentsAsync` + `IForkSubAgentManager.GetActiveForksAsync` **pull** 快照（含 fork、跨回合、4 态 string、500ms 心跳重建）
2. `SubAgentRun` — 运行态记录，从 `ChatStreamEvent` 事件流 **push** 聚合（单回合、3 态枚举、回合常驻、线程安全锁、含 Transcript）

**根因**：引擎层没有"子代理活动历史"概念。`AgentRuntimeState` 只持有 `ProgressTracker`（最近 5 条活动）+ 完成源，不保留活动历史/transcript/最终输出。事件发出类（`AgentStreamExecutionMiddleware`/`AgentForkMiddleware`/`ForkSubAgentManagerActor`）只发射事件不维护状态。GUI 无奈自己在 `SubAgentRunTracker` 聚合事件流，造轮子。

**后果**：
- 两套模型 7 个属性重叠（AgentId/Name/Description/State/ToolUseCount/LastActivityText/VisibleActivities），State 类型还不一致（string 4 态 vs 枚举 3 态）
- `FillActivities` 桥接（`BackgroundAgentsPanelViewModel.cs:225-231`）按 AgentId 单向拷贝，是范式边界的补丁
- 引擎层 `IAgentTranscriptService` 已持久化 transcript 到磁盘，但 GUI 未消费，独立维护 `SubAgentRun._transcript`，双份维护

## 决策

**引擎层作为唯一权威源，GUI 统一 pull，事件流仅用于实时增量通知。**

### 引擎层扩展

1. `AgentRuntimeState` 增加 `AgentActivityHistory` 字段（完整活动历史，环形缓冲上限 200 条）
2. 事件发出时同步追加到引擎状态（`AgentStreamExecutionMiddleware`/`AgentForkMiddleware` Emit 后追加）
3. `RunningAgentInfo` 扩展：增加 `Activities`（`IReadOnlyList<AgentActivityEntry>`）+ `LastActivityText` + `FinalOutput` + `IsSuccess` + `ExecutionTimeMs` + `Role`
4. fork 路径：`ForkRuntime` 增加 `AgentActivityHistory`，从 AgentStarted/AgentFinished + AgentBase 内部状态填充
5. 新增 `IAgentService.GetAgentActivityHistoryAsync(agentId, ct)` 方法（分页查询完整历史）

### GUI 层合并

1. 删除 `SubAgentRun`、`SubAgentRunTracker`、`SubAgentTranscriptItem`、`SubAgentRunState`、`AgentRunVm`
2. `BackgroundAgentItemVm` 扩展为唯一模型，从扩展后的 `RunningAgentInfo` 构造，含活动历史/transcript/最终输出
3. 删除 `BackgroundAgentsPanelViewModel.FillActivities` 桥接（不再需要跨模型拷贝）
4. `ChatTurnProcessor` 删除 `_agentTracker`，事件流仅用于实时 UI 增量刷新（直接更新 `BackgroundAgentItemVm` 的活动属性）
5. `TranscriptWindow` 改为接收 `BackgroundAgentItemVm`
6. XAML 统一为一个 DataTemplate

### 状态统一

- 删除 `SubAgentRunState` 枚举（3 态）
- `BackgroundAgentItemVm.State` 改用 `AgentStatus` 枚举（引擎层已有，含 Pending/Running/Paused/Completed/Failed）
- 删除 `RunningStates` FrozenSet 和字符串比较

## 替代方案

### 方案 B：Hybrid（引擎权威源 + 事件流增量对账）

保留事件流实时刷新 + 引擎权威源定期对账。`SubAgentRun` 和 `BackgroundAgentItemVm` 合并为一个类从引擎 pull，事件流仅触发刷新。

**放弃原因**：保留两套数据通路（事件 + pull）增加复杂度，对账逻辑易出 bug。用户明确要求"只有一套"。

### 方案 C：仅提取共享只读接口

两者实现 `ISubAgentView` 接口，XAML 绑定到接口。不动引擎层。

**放弃原因**：不根治数据源分裂，表面重复消除但底层仍双份维护。用户明确要求"大修大改""工程绝对正确"。

## 后果

- 正面：
  - 引擎层唯一权威源，GUI 单一模型，消除 7 个重叠属性和 State 类型不一致
  - 消除 `FillActivities` 桥接补丁
  - 消除 GUI 层 `SubAgentRunTracker` 造轮子
  - 引擎层活动历史可供其他消费方（如 CLI、API）复用
  - fork 和普通子代理状态统一
- 负面：
  - 引擎层内存占用增加（每个子代理保留 200 条活动历史）
  - 事件发出路径增加同步追加开销（无锁 CAS，可接受）
  - 改动量大（引擎 + GUI 15+ 文件）
- 中性：
  - 事件流保留用于实时 UI 增量刷新，但不再作为状态聚合源
