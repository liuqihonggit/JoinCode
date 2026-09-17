# 字段合并与重构任务清单

> 来源:AsyncLocal 优化后的子代理分析,21 个建议,按变清晰程度 × 低风险排序

## 🔴 高优先级(零/低风险,高收益)

### 1. TcsFactory 工厂方法(9+ 处重复)
- [ ] 在 `lib/abstractions/abs_core/core_utils/core/` 新增 `TcsFactory` 静态类
- [ ] 替换 9+ 处 `CreateTcs()` / `new TaskCompletionSource(RunContinuationsAsynchronously)`
- 位置:kit/mcp_tool_dispatch、kit/hands/voice、kit/brain/summary、kit/brain/query、kit/hands/shell、server/bridge/client、server/eyes/lsp、llm/agents/Coordinator/Team

### 2. ServiceHost 双字典合并
- [ ] `lib/clock/hosting/ServiceHost.cs` `_services`+`_serviceStatuses` → `ConcurrentDictionary<string, ServiceEntry>`
- ServiceEntry = { IWorkflowService Service, ServiceStatus Status }

### 3. GraphExecutionContext 三字典合并
- [ ] `lib/clock/goal/models/GraphExecutionContext.cs` `RetryCount`+`CompletedNodes`+`FailedNodes` → `ConcurrentDictionary<string, NodeExecutionState>`
- NodeExecutionState = { int RetryCount, NodeStatus Status }

### 4. MailboxBase 双字典合并
- [ ] `lib/async_lock/MailboxBase.cs` `_agentChannels`+`_agentSessions` → `ConcurrentDictionary<string, AgentMailboxEntry>`
- AgentMailboxEntry = { Channel<TMessage> Channel, string? SessionId }

### 5. TaskService 双字典合并
- [ ] `lib/scheduling/services/TaskService.cs` `_tasks`+`_taskStateMachines` → `ConcurrentDictionary<string, TaskEntry>`
- TaskEntry = { TaskItem Item, TaskStateMachine StateMachine }

## 🟡 中优先级(低风险,中收益)

### 6. BridgeClient 统计字段合并
- [ ] `server/bridge/client/BridgeClient.cs` 5 统计字段 → `BridgeClientStats` struct
- 字段:_totalMessagesReceived/_totalMessagesProcessed/_totalEchoFiltered/_totalDuplicatesFiltered/_startedAt

### 7. FileToolHandlers 14 字段→Context 引用
- [ ] `kit/hands/tool_handlers/dev_tools/handlers/FileToolHandlers.cs` 14 展开字段 → 持有 `FileToolHandlersContext _ctx` 单引用

### 8. QqBotAdapter+FeishuBotAdapter 孪生类提取基类
- [ ] `llm/agents/Coordinator/Core/Messaging/` 提取 `PlatformBotAdapterBase<TConfig>`
- 子类只覆写 PlatformName/BuildSendUrl/BuildSendContent/AcquireTokenAsync

### 9. TelemetryService 三指标字典统一
- [ ] `lib/infrastructure/telemetry/TelemetryService.cs` `_counters`+`_histograms`+`_gauges` → `ConcurrentDictionary<string, ITelemetryMetric>`

### 10. OutputLoopConfig+ToolCallSequenceConfig 重复字段提取基类
- [ ] `kit/brain/context/services/core/LoopInterventionOptions.cs` 提取 `LoopPatternDetectorConfig` 基类
- 公共字段:WindowSize/MinPatternLength/RequiredRepeats

## 🟢 低优先级(中风险,中低收益)

### 11. IRemoteRefreshOptions 3 实现类提取基类
- [ ] `lib/guard/policy/RemotePolicyOptions.cs` + `lib/guard/configuration/remote/RemoteSettingsOptions.cs` + `kit/brain/cost_tracking/feature_flags/FeatureFlagOptions.cs`
- 提取 `RemoteRefreshOptionsBase` 含 5 公共字段

### 12. BuildQueueService+BuildQueueRouter 提取基类
- [ ] `kit/hands/build/` 提取 `BuildQueueBase` 持有共享字段+ExecuteBuildCoreAsync

### 13. ConsoleCancelScope 提升为公共工具类
- [ ] `kit/slash/transport/BridgeMainCommand.cs:365-387` → `lib/abstractions/abs_core/core_utils/core/ConsoleCancelScope.cs`

### 14. PluginManager 三 host 统一
- [ ] `lib/plugins.infrastructure/services/PluginManager.cs` 引入 PluginHost 基类/接口

### 15. AgentBase 字段拆分(预算/输出值对象)
- [ ] `llm/agents/Coordinator/Fork/AgentBase.cs` 提取 `AgentBudget` + `AgentOutput`

### 16. ForkEntry 字段分组(身份+运行时)
- [ ] `llm/agents/Coordinator/Fork/ForkSubAgentManagerActor.cs` 拆为 `ForkIdentity`(不可变) + `ForkRuntime`(可变)

### 17. AgentWorktreeService 无关 static 字段分散
- [ ] `llm/agents/Services/Support/AgentWorktreeService.cs` 提取 `WorktreePatternMatcher` + `JsonFormatting`

### 18. NamedPipeMailbox+NetworkMailbox _receiveLoopTask 上提基类
- [ ] `llm/agents/Coordinator/Core/Messaging/` `_receiveLoopTask`+StartReceiveLoop 骨架上提到 MailboxBase

### 19. PreventSleepScope 调用重复(关联 #12)
- [ ] 与 BuildQueueBase 合并实现

### 20. SubAgentContext+SubAgentEventChannel 合并(谨慎,风险高)
- [ ] 评估是否合并,当前建议暂不动

### 21. InMemoryFileSystem _files+_directories(不建议合并)
- [ ] 类型不同、语义互斥,合并引入 cast 复杂度 — 跳过

## 实施顺序
1. 先做 #1(TcsFactory)— 零风险,创建工具类+替换调用方
2. 批量做 #2-5(字典合并)— 模式相同,逐个编译+测试+提交
3. 做 #6-7(字段合并)
4. 做 #8(Bot 适配器基类)
5. 做 #9-10
6. 低优先级按需做

## 状态跟踪
- 起始:2026-09-18
- 已完成 AsyncLocal 优化(6 个提交):54ac13d2e, 16de25e23, 080b0cf, a1fb3efd7, f8534d1b2, 10908543d
- #1-10 已完成(从别分支拉取,已在 git 中)
- #11-14 已完成并 rebase 合并:dc5e6c75d (RemoteRefreshOptionsBase/BuildQueueBase/ConsoleCancelScope/IPluginHost 统一)
  - 合并 HEAD 的 BuildQueueEntryStore + 我的 BuildQueueBase:基类内部用 _store(BuildQueueEntryStore)
  - _store 可访问性用 private protected(同程序集子类可见)
  - CreateQueuedEntry 返回 (Entry, Tcs) 元组,避免 TryGetTcs null 抑制
  - 修复 IPluginHost.Kind → PluginType 命名冲突(与 PluginResourceBase.Kind 同名)
- #15-18 已完成并提交:a020d63e3
  - #15 AgentBase → AgentBudget + AgentOutput 值对象
  - #16 ForkEntry → ForkIdentity(不可变) + ForkRuntime(可变)
  - #17 AgentWorktreeService → WorktreePatternMatcher + WorktreeJsonFormatting
  - #18 NamedPipeMailbox+NetworkMailbox → StreamMailboxBase 模板方法模式
- #19 已随 #12 解决(PreventSleepScope 仅在 BuildQueueBase.ExecuteBuildCoreAsync 中调用)
- #20-21 跳过(风险高/不建议合并)
