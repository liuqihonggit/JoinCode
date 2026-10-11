# 引擎层统一子代理状态源 — GUI 双模型合并

> ADR: [0121](../adr/0121-engine-agent-state-source-unification.md)

## 目标

引擎层作为唯一权威源，GUI 统一 pull，合并 `SubAgentRun` 和 `BackgroundAgentItemVm` 为一套模型。

## 阶段规划

### 阶段1：引擎层扩展状态结构

- [ ] 1.1 新增 `AgentActivityEntry` record（Timestamp/Type/Text/Glyph）
- [ ] 1.2 新增 `AgentActivityHistory` 类（环形缓冲上限 200，线程安全 CAS）
- [ ] 1.3 `AgentRuntimeState` 增加 `AgentActivityHistory?` 字段
- [ ] 1.4 `RunningAgentInfo` 扩展：Activities/LastActivityText/FinalOutput/IsSuccess/ExecutionTimeMs/Role
- [ ] 1.5 `IAgentService` 新增 `GetAgentActivityHistoryAsync(agentId, ct)` 方法
- [ ] 1.6 `AgentServiceImpl` 实现 `GetAgentActivityHistoryAsync`
- [ ] 1.7 编译 + 单元测试 + 提交

### 阶段2：事件发出时追加到引擎状态

- [ ] 2.1 `AgentStreamExecutionMiddleware` Emit 后追加活动到引擎状态
- [ ] 2.2 `AgentForkMiddleware` Emit 后追加活动到引擎状态
- [ ] 2.3 `ForkSubAgentManagerActor` 后台完成时追加活动
- [ ] 2.4 `AgentServiceImpl.GetRunningAgentsAsync` 填充 Activities/LastActivityText/FinalOutput/IsSuccess/ExecutionTimeMs/Role
- [ ] 2.5 编译 + 单元测试 + 提交

### 阶段3：GUI 合并为单一模型

- [ ] 3.1 `BackgroundAgentItemVm` 扩展：从扩展后的 `RunningAgentInfo` 构造，含 Activities/FinalOutput/IsSuccess/ExecutionTimeMs/Role/Transcript
- [ ] 3.2 `BackgroundAgentItemVm.State` 改用 `AgentStatus` 枚举
- [ ] 3.3 删除 `RunningStates` FrozenSet 和字符串比较
- [ ] 3.4 `TranscriptWindow` 改为接收 `BackgroundAgentItemVm`
- [ ] 3.5 XAML 统一为一个 DataTemplate
- [ ] 3.6 编译 + 单元测试 + 提交

### 阶段4：删除旧模型

- [ ] 4.1 删除 `SubAgentRun`、`SubAgentTranscriptItem`、`SubAgentRunState`
- [ ] 4.2 删除 `SubAgentRunTracker`
- [ ] 4.3 删除 `AgentRunVm`
- [ ] 4.4 删除 `BackgroundAgentsPanelViewModel.FillActivities` 桥接
- [ ] 4.5 `ChatTurnProcessor` 删除 `_agentTracker`，事件流直接更新 `BackgroundAgentItemVm`
- [ ] 4.6 删除 `BackgroundAgentsPanelViewModel.UpdateTracker`/`runTracker` 注入
- [ ] 4.7 编译 + 单元测试 + 提交

### 阶段5：测试修复 + 全量验证

- [ ] 5.1 修复所有引用旧模型的测试
- [ ] 5.2 全量编译 JoinCode.slnx
- [ ] 5.3 全量单元测试
- [ ] 5.4 GUI exe 手动验证
- [ ] 5.5 提交 + PR

## 风险

- 引擎层内存占用增加（每子代理 200 条活动历史）— 可接受
- fork 路径无流式 chunk，活动历史只能从 AgentStarted/AgentFinished 两端填充 — 需验证
- 事件发出路径增加同步追加开销 — 无锁 CAS，可接受
