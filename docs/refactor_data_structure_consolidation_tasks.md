# 数据结构合并重构任务清单

> 生成时间:2026-09-18
> 背景:刚完成 AsyncLocal 优化(FlowId+ActorId 合并为 AsyncFlowIdentity、提取 AsyncLocalScope<T>、消除 ManagedThreadId)。
> 本清单由 10 个并行子代理扫描全工程得出,共 62 处重构机会。
> 执行顺序:从低收益(P3)开始做起,渐进式推进。

---

## 📊 优先级总览

| 优先级 | 数量 | 说明 |
|--------|------|------|
| P0 最高 | 5 项 | 收益最大、风险可控,但改动面广 |
| P1 高 | 11 项 | 语义清晰、收益明显 |
| P2 中 | 6 组 | 模式一致可批量重构 |
| P3 低 | 4 项 | 收尾打磨,本次从这里开始 |

---

## 🔵 P3 低优先级(本次起始)

### [P3-1] PlanModeManager ManagedThreadId 拋余 ✅ 已完成
- **文件**:`kit/brain/planning/planning2/PlanModeManager.cs:152`
- **当前**:`$"session_{Environment.CurrentManagedThreadId}_{...}"`
- **重构**:→ `$"session_{AsyncFlowIdentity.CurrentFlowId}_{...}"`(需确保 FlowId 已注册)
- **理由**:async 流跨线程切换时 ManagedThreadId 会变,导致 slug 漂移;FlowId 基于 AsyncLocal 跨 await 流转
- **状态**:✅ 已完成

### [P3-2] ActorBase.SetActorId/ClearActorId 手动 try-finally ✅ 已完成
- **文件**:`lib/async_lock/ActorBase.cs:240-263`
- **重构**:提取 `AsyncFlowIdentity.EnterActorScope` + 私有 `ActorScope`(因循环依赖不能复用 AsyncLocalScope,在 AsyncFlowIdentity 内定义)
- **收益**:消除手动 try-finally,恢复 previous 比 ClearActorId 更正确(嵌套 Actor 保留外层 ActorId)
- **状态**:✅ 已完成

### [P3-3] public 字段→属性(SessionPlanState + EntropyFsmContext)
- **文件**:`kit/brain/planning/planning2/PlanModeManager.cs:40-48` + `kit/brain/context/services/loop/ShannonEntropyDetector.cs:47-54`
- **重构**:public 字段改为 `{ get; set; }` 自动属性,可加状态转换守卫
- **状态**:⏳ 待做

### [P3-4] Entity 基类 3 个 ID 字段(谨慎,暂不动)
- **文件**:`lib/abstractions/abs_core/core_entity/Entity.cs:11,13,44`
- **重构**:合并为 `EntityIdentity { ObjectId, SessionId, TraceId }`
- **状态**:⏸️ 暂缓(影响面大,建议实体子系统大规模重构时一并处理)

---

## 🟢 P2 中优先级(后续推进)

### [P2-1] Dictionary 重复·5 组完全重复
- BuildQueueService + BuildQueueRouter(`_entries`+`_waitHandles`)→ `BuildQueueEntryStore` ✅ `44533ba5b`
- InProcessMailbox + FileMailbox(`_deliveredMessageIds`+`IsDuplicate`)→ `MessageDedupTracker` ✅ `28db751c7`
- UsageTracker + CostTracker(`_usageRecords`+`_sessionIndex`)→ `TokenUsageStore` ⏭️ 跳过:两处 TokenUsageRecord 是不同类型(字段名不同 InputTokens/PromptTokens),泛型提取引入 Func 参数+放置位置困难(Hands/Brain 无共享工具项目),属巧合相似非本质重复
- McpTcpServer + McpHttpServer(`_sessions`)→ `McpSessionRegistry`
- InMemoryFileSystem + PhysicalFileSystem(`_editLocks`+`GetOrAdd`)→ `EditLockRegistry` ✅ `4de0884d9`
- **状态**:✅ 4/5 组已完成(UsageTracker+CostTracker 跳过,原因见上)

### [P2-2] InMemoryIndexStore 重复锁 Scope — 3 个私有类
- **文件**:`server/code_index/indexing/InMemoryIndexStore.cs:134-168`
- **重构**:删除 3 个私有嵌套类,改用已有 `LockScope.cs` 扩展方法
- **状态**:✅ `b00d7ceae`

### [P2-3] 字段混乱·4 个类各 11-13 字段
- LspClient(13)→ `LspProcessChannel` + `LspMessageRouter` ✅ `fC` `508aa627b`+`f6f331c29`+`be498a5e0`(组合根+重复片段消除+字典合并)
- BridgeSubprocessHandle(13)→ `SubprocessIoChannels` + `SubprocessState` ✅ `c087516fc`(含 Kill/ForceKill 重复消除)
- TeamManager(13)→ `TeamRegistry` ✅ `4538d5db6`+`c72f7cdfa`(Rooms 优化)
- LspManager(11)→ `LspServerRegistry` + 字段位置统一 ✅ `76f00a14d`(含 DisposeAsync 顺序修复)
- 附带修复 `_disposed` 类型不一致(bool vs int+Interlocked)
- **状态**:✅ 全部完成

### [P2-4] static 字段合并·6 组规则集
- EnvOverrideApplier: 合并 ProtocolByVendor+ApiKeyEnvVarByVendor 为单字典+VendorInference record ✅ `2c87334f6`
- 其余5个: 字段和方法不多,不需要组合根拆分,跳过
- **状态**:✅ 1/6 完成(仅 EnvOverrideApplier 有 key 相同的字典值得合并)

### [P2-5] 字段混乱·CostTracker 16 字段 + FileToolHandlers 20 字段
- CostTracker 拆 `UsageStore`/`BudgetGuard`/`SessionStats`
- FileToolHandlers 按子领域二次聚合 `FileEditDeps`/`LspDeps`
- **状态**:⏳ 待做

### [P2-6] ID 字段合并·10 处 3-4 ID 字段
- ChatStreamEvent/TeammateContext/CrashExecutionContext/SubagentStopHookContext/AgentState+StateDocuments/TaskState+StateDocuments/TeamContext/ReconnectTeammateEntry/AgentCompletedEventArgs/AgentMetadata
- **状态**:⏳ 待做

---

## 🟡 P1 高优先级(后续推进)

### [P1-1] TeammateContext → SubAgentContext 合并
- **文件**:`lib/abstractions/abs_agents/team/TeammateContext.cs:5` + `lib/scheduling/tasks/core/TeammateLoopRunner.cs:106-107`
- **重构**:`TeammateMeta` 作为可选子对象挂到 `SubAgentContext`,消除 1 个 AsyncLocal
- **状态**:⏳ 待做

### [P1-2] AgentIdentity 统一族(6 处高价值)
- AgentDescriptor/SubAgentContext/CoordinatorMessage/TranscriptEntry/AgentRegistryInfo/TeammateStatus
- 提取 `AgentIdentity` + `TeammateIdentity` + `MessageIdentity` 类型族
- **状态**:⏳ 待做

### [P1-3] WorktreePatternCache 完全复制粘贴 ✅ 已完成
- **文件**:`llm/agents/Services/Support/AgentWorktreeService.cs` + `WorktreeConfigMiddleware.cs`
- `WorktreeIncludePatternMatcher` 已存在;实际重复是 AgentWorktreeService 中4个死代码方法(CopyConfigFilesAsync/CopyWorktreeIncludeFilesAsync/ConfigureWorktreeHooksPathAsync/CreateSymlinksAsync),已被 WorktreeConfigMiddleware 中间件取代
- **状态**:✅ `5c61f104e`(删除133行死代码,Agents 594测试通过)

### [P1-4] AgentRuntimeRegistry — _agentStartTimes 跨类重复 ✅ 已完成
- AgentServiceImpl + AgentCoordinator 各自维护 `_agentStartTimes` ConcurrentDictionary<string,DateTime>
- 提取 `AgentStartTimer`(Record/TryRemoveDurationMs/Remove/TryGet)
- **状态**:✅ `1679a4160`(Agents 594测试通过)

### [P1-5] CallTrace + PromptConfigSnapshot 手动 try-finally
- 增加 `EnterScope` 工厂方法,委托 `AsyncLocalScope<T>`
- **状态**:⏳ 待做

### [P1-6] 颜色 Scope·CliOutputContract + FailFastChecker + ErrorConsole
- 改用已有 `TerminalHelper.SetColor()` 的 `using` 模式
- **状态**:⏳ 待做

### [P1-7] MemoryType 三维度属性表
- **文件**:`lib/vault/memdir/memdir2/core/MemoryType.cs:39,47,55`
- 合并 `DefaultTtls` + `BaseRelevanceWeights` 为 `FrozenDictionary<MemoryType, MemoryTypeProfile>`
- **状态**:⏳ 待做

### [P1-8] CpuParallelism 双基线状态
- **文件**:`lib/infrastructure/utils/system/CpuParallelism.cs:8,9,12-15,18-20`
- 7 个可变 static 字段合并为 `WindowsCpuBaseline` + `FallbackCpuBaseline` record struct
- **状态**:⏳ 待做

### [P1-9] ReasoningOptionsBuilder 14 字段 + ReasoningEngine 13 字段
- Builder 拆 3 个配置 record;Engine 拆 `ReasoningBudget` + `ReasoningComponents`
- **状态**:⏳ 待做

### [P1-10] StreamReader/Writer(stream, Encoding.UTF8) — ~40 处
- 提取 `StreamTextScope` 扩展方法 `AsUtf8Reader()`/`ReadAllTextUtf8()`
- **状态**:⏳ 待做

### [P1-11] SystemActuatorCommandContext — 20 字段 + 3 个 Timer
- 拆 `CommandSpec`/`ProcessLifecycle`/`CommandRuntimeState` + 提取 `ThreadSafeTimerField`
- **状态**:⏳ 待做

---

## 🔥 P0 最高优先级(后续推进)

### [P0-1] BridgeSessionTracker + HandleWorkContext + ShutdownContext — 20 个字典重复
- HandleWorkContext/ShutdownContext 删除字典字段,改为持有 `internal BridgeSessionTracker Tracker` 引用
- **状态**:⏳ 待做

### [P0-2] MainViewModel — 约 55 个字段,5 个职责组
- 拆 `SessionInfrastructure`/`ChatPreferences`/`UiFeedbackState`/`SessionHistoryManager`/`ConnectionDropdown`
- **状态**:⏳ 待做

### [P0-3] BridgeMain 25 字段 + AgentServiceImpl 22 字段
- BridgeMain 拆 5 个内聚类;AgentServiceImpl 拆 `AgentRuntimeState` + 依赖组
- **状态**:⏳ 待做

### [P0-4] GDI SelectObject 恢复模式 — 7 处重复
- 提取 `GdiSelectScope` IDisposable 结构
- **状态**:⏳ 待做

### [P0-5] MSBuildWorkspace.Create + FileStream + FileShare.ReadWrite
- 提取 `MsBuildWorkspaceScope.Enter()` + `SafeFileScope.OpenRead/OpenWrite/OpenAppend`
- **状态**:⏳ 待做

---

## 📈 执行记录

| 日期 | 任务 | 提交 | 验证 |
|------|------|------|------|
| 2026-09-18 | [P3-1] PlanModeManager ManagedThreadId → FlowId | b058d792a | AsyncLock 183 + Brain.Other 295 测试通过 |
| 2026-09-18 | [P3-2] ActorBase EnterActorScope | 待提交 | AsyncLock 183 + Brain.Other 295 测试通过 |

---

<!-- 🤖 Auto Decision: 2026-09-18 -->
<!-- 决策: 从 P3 低收益开始渐进式推进,先做单点修改风险最低的 PlanModeManager -->
<!-- 原因: 用户明确要求"从低收益开始做起",且 P3-1 是单点修改最安全 -->
<!-- 替代方案: 从 P0 开始收益最大但改动面广风险高 -->
<!-- 验证: 待编译测试 -->
