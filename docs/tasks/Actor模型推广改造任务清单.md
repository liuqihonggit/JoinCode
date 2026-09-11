# Actor 模型推广改造任务清单

> 检查日期: 2026-09-12
> 背景: ADR 0091 已确认 ActorBase 双工改造完成（8 个直接派生+3 个生态类），但项目中仍有大量并发状态未用 Actor 模型
> 相关 ADR: 0074（监督树）、0091（双工就地升级）、0068（统一持久化管道 Actor）
> 基类位置: `foundation/AsyncLock/src/ActorBase.cs` — `ActorBase<TCommand, TOut> : IAsyncDisposable`

## 一、已覆盖清单（11 个）

### 直接继承 ActorBase（8 个）

| Actor | 位置 | TCommand | TOut | 用途 |
|-------|------|----------|------|------|
| `SupervisedActor<T>` | foundation/AsyncLock/src/SupervisedActor.cs:197 | T | SupervisorEvent | 监督树抽象基类 |
| `RouterActor<T>` | foundation/AsyncLock/src/RouterActor.cs:71 | IRouterCommand | RouterEvent<T> | 多 Worker 负载分发 |
| `GatewayActor<TReq,TResp>` | foundation/AsyncLock/src/GatewayActor.cs:63 | IGatewayCommand | GatewayEvent | 限流/重试/熔断 |
| `StreamingToolExecutorActor` | core/execution/Brain/.../StreamingToolExecutorActor.cs:34 | IToolCommand | StreamingToolResult | 流式工具执行 |
| `McpRequestRegistryActor` | services/Mcp/.../McpRequestRegistryActor.cs:8 | IRequestCommand | JsonRpcResponse | MCP 请求注册 |
| `PersistencePipeline` | infrastructure/IO/PersistencePipeline.cs:10 | PersistRequest | Unit | 统一持久化管道 |
| `BuildWorker` | core/execution/Hands/.../BuildQueueRouter.cs:252 | ICommand | BuildEvent | 编译 Worker |
| `BuildQueueRouterActor` | core/execution/Hands/.../BuildQueueRouter.cs:237 | IRouterCommand | RouterEvent | 编译队列路由 |

### 生态类（3 个，不直接继承 ActorBase）

| 类 | 位置 | 模式 |
|----|------|------|
| `PersistentMailbox<T,T>` | foundation/AsyncLock/src/PersistentMailbox.cs:53 | 装饰器，包装 ActorBase |
| `PriorityMailbox<T>` | foundation/AsyncLock/src/PriorityMailbox.cs:41 | 独立基类，三通道优先级 |
| `BuildQueueRouter` | core/execution/Hands/.../BuildQueueRouter.cs:9 | 组合，内部用 BuildQueueRouterActor |

## 二、未覆盖清单（按优先级排序）

### P0 — 强烈推荐改（Timer + AsyncLock + 复杂状态）17 个

Timer 周期任务 → Actor 周期自消息；AsyncLock → 消除；状态 → Consumer 独占。

| # | 类名 | 文件路径 | 当前机制 | 难度 |
|---|------|----------|---------|------|
| 1 | ToolHealthMonitor | core/execution/McpToolDispatch/src/Core/Execution/ToolHealthMonitor.cs | Timer+AsyncLock+volatile+Dict | 中 |
| 2 | CronScheduler | core/execution/Scheduling/src/Cron/CronScheduler.cs | Timer+ConcurrentDict+volatile | 中 |
| 3 | SystemActuatorCommandContext | core/execution/Hands/.../SystemActuatorCommandContext.cs | 3×Timer+状态 | 中 |
| 4 | ToolHypergraphScorer | core/execution/McpToolDispatch/src/Core/Execution/ToolHypergraphScorer.cs | Timer+状态 | 中 |
| 5 | ProcessHealthMonitor | infrastructure/Infrastructure/Process/ProcessHealthMonitor.cs | Timer+AsyncLock | 中 |
| 6 | ShellProcessWatchdog | infrastructure/Infrastructure/Shell/ShellProcessWatchdog.cs | Timer+状态 | 低 |
| 7 | TokenRefreshScheduler | core/safety/Guard/src/OAuth/TokenRefreshScheduler.cs | ConcurrentDict<string,Timer> | 中 |
| 8 | BridgeTokenRefreshScheduler | infrastructure/Transport.Impl/.../BridgeTokenRefreshScheduler.cs | AsyncLock+Dict<string,Timer> | 中 |
| 9 | FlushGate | infrastructure/Transport.Impl/.../FlushGate.cs | AsyncLock+List+Timer | 低 |
| 10 | DiagnosticLogWatcher | core/ai/Agents/src/Doctor/DiagnosticLogWatcher.cs | Timer+AsyncLock+Dict | 中 |
| 11 | FastModeService | core/safety/Guard/.../FastModeService.cs | Timer+AsyncLock | 低 |
| 12 | RemoteCacheRefreshServiceBase | foundation/Abstractions/.../RemoteCacheRefreshServiceBase.cs | Timer+AsyncLock+ConcurrentDict | 中 |
| 13 | TeamMemorySyncService | core/safety/Vault/src/Memdir/Sync/TeamMemorySyncService.cs | Timer+AsyncLock+ConcurrentDict | 中 |
| 14 | AwaySummaryService | core/execution/Brain/.../AwaySummaryService.cs | Timer+AsyncLock+ConcurrentQueue | 低 |
| 15 | GoalHeartbeat | composition/Clock/src/Goal/Core/GoalHeartbeat.cs | AsyncLock+PeriodicTimer | 低 |
| 16 | HookEventBroadcaster | core/safety/Guard/.../HookEventBroadcaster.cs | Timer+状态 | 低 |
| 17 | DebounceTracker | infrastructure/Infrastructure/Utils/IO/DebounceTracker.cs | ConcurrentDict<string,Timer> | 低 |

### P1 — 推荐改（状态机 + AsyncLock，天然 = Actor）5 个

| # | 类名 | 文件路径 | 难度 | 备注 |
|---|------|----------|------|------|
| 18 | **StateMachine<T>** | foundation/Abstractions/00-core/Core/Utils/State/StateMachine.cs | 低 | **通用基类，改一处全局受益** |
| 19 | AgentStateMachine | core/ai/Agents/.../AgentStateMachine.cs | 中 | ConcurrentDict+AsyncLock+StateMachine |
| 20 | UnifiedCircuitBreaker | infrastructure/Infrastructure/Utils/Resilience/UnifiedCircuitBreaker.cs | 低 | 熔断器=经典 Actor |
| 21 | ConnectionManager | infrastructure/Transport.Impl/.../ConnectionManager.cs | 中 | 连接生命周期=命令 |
| 22 | NetworkConnectivityService | infrastructure/Infrastructure/Network/NetworkConnectivityService.cs | 中 | 网络事件=命令 |

### P2 — 推荐改（已有 Channel 雏形，离 Actor 一步之遥）7 个

| # | 类名 | 文件路径 | 难度 |
|---|------|----------|------|
| 23 | **InProcessMailbox** | core/ai/Agents/.../InProcessMailbox.cs | 低 |
| 24 | BuildQueueService | core/execution/Hands/.../BuildQueueService.cs | 低 |
| 25 | DoctorTcpServer | core/ai/Agents/src/Doctor/DoctorTcpServer.cs | 中 |
| 26 | **LoopDiagnosticJournal** | core/execution/Brain/.../LoopDiagnosticJournal.cs | 低 |
| 27 | AgentOutputChannelManager | core/ai/Agents/.../AgentOutputChannelManager.cs | 低 |
| 28 | InProcessTeammateTask | core/execution/Scheduling/.../InProcessTeammateTask.cs | 中 |
| 29 | GoalConflictMessenger | composition/Clock/src/Goal/Core/GoalConflictMessenger.cs | 低 |

### P3 — 适合改（AsyncLock 保护复杂共享状态）45 个

高价值代表：
- TeamManager（7 张表+AsyncLock，高难度高收益）
- TaskRuntime（AsyncLock+ConcurrentDag，任务运行时）
- ChatContextManager（对话上下文表）
- **PluginManager（已是 Actor 但内部还有 ConcurrentDictionary，半吊子）**
- DynamicPluginRegistry（ConcurrentDict+lock+Interlocked）
- ConcurrentDag（AsyncLock+Dag，拓扑排序）
- McpServerStateManager / McpClientToolHandlers
- TokenBudgetManager / UsdBudgetManager（串行扣减无锁）
- AgentWorktreeService / AgentTranscriptService
- LocalToolRegistry / SkillService
- 其余 36 个（见原始探索记录）

### P4 — 可选改（lock/Monitor）6 个

部分适合，UI 线程同步的不适合（如 OutputBufferImpl/AnsiResponseParserBase）。

### P5 — 不推荐改（volatile 不可变快照交换）17 个

已是无锁最佳实践，Actor 化收益小、改造成本高。包括：
- ToolTemplateService、SandboxManager、ModelConfigLoader、FileContextTracker、SearchScopeValidator、MtlsService、RateLimitTracker 等

### 不适合改 6+ 个

纯限流（SemaphoreSlim 无共享状态）、无锁 CAS（MemorySearchHistory）、Dispose 幂等模式。

## 三、独立用 Channel 但未封装 Actor（13 处）

| # | 文件 | 当前用途 | 改造潜力 |
|---|------|----------|---------|
| 1 | AnalyticsFileSink.cs | 遥测事件缓冲 | 高（与 PersistencePipeline 同构） |
| 2 | LoopDiagnosticJournal.cs | 循环诊断日志 | 高（命令模式+单消费者） |
| 3 | DoctorTcpServer.cs | TCP 诊断事件流 | 中 |
| 4 | DoctorStdioTransport.cs | stdio 诊断事件流 | 中 |
| 5 | BuildQueueService.cs | 串行编译队列 | 已有 Router 替代 |
| 6 | InProcessMailbox.cs | Agent 消息邮箱 | 高（典型 Actor 邮箱） |
| 7 | InProcessTeammateTask.cs | 队友任务消息 | 中 |
| 8 | TeammateRegistrationMiddleware.cs | 队友注册中间件 | 中 |
| 9 | GoalConflictMessenger.cs | 目标冲突消息 | 中 |
| 10 | AgentOutputChannelManager.cs | Agent 输出通道管理 | 高 |
| 11 | AgentInputForwardQueue.cs | Agent 输入转发 | 中 |
| 12 | McpStdioClient.cs | MCP stdio 写通道 | 低（底层传输） |
| 13 | SandboxIpcClient.cs | 沙箱 IPC 写通道 | 低（底层传输） |

## 四、不舒服 / 想升级（主观判断）

### 最不舒服 Top 5

1. **PluginManager 半吊子 Actor** — 已是 `Actor<PluginManagerCommand,PluginManagerOutput>` 但内部残留 ConcurrentDictionary。Consumer 线程本应独占状态，现在却还有并发字典。建议：彻底移入 Consumer 独占。

2. **StateMachine<T> 通用基类用 AsyncLock** — 状态机数学上就是 Actor。被 AgentStateMachine/UnifiedCircuitBreaker 等多处继承，改一处全局受益。

3. **ToolHealthMonitor 三重并发组合** — Timer+AsyncLock+volatile+Dict，时序推理极难。Timer 周期衰减和外部健康报告交错访问同一批状态。

4. **InProcessMailbox 离 Actor 一步之遥** — 已用 ConcurrentDictionary<string,Channel> 做消息路由，但路由表本身是并发字典。

5. **独立用 Channel 但没封装 Actor 的 13 处** — AnalyticsFileSink/LoopDiagnosticJournal 与 PersistencePipeline 同构，却没复用 ActorBase 的背压+水位线+Id 监控。

### 想升级的 3 处

1. **CronScheduler** — 6 个并发字段可全部收敛到 Consumer 独占。
2. **TeamManager 7 张表** — Actor 化后所有团队操作变成串行命令，推理复杂度骤降。
3. **ConcurrentDag** — Actor 化后消除 AsyncLock，且与 TaskRuntime 联动。

### 架构级建议

当前 Actor 基类缺少 `IActor` 接口和 `ActorRef`。大规模改造（80 个候选）时，外部调用方需要类型安全的"发消息不关心具体 Actor 类型"能力。建议补最小 `IActor<TCommand>` 接口（只暴露 SendAsync/TrySend/Id），不需要完整 ActorRef 位置透明性。

## 五、改造计划（基建先行）

### 阶段 0：基建（补 IActor 接口）

- [x] 在 foundation/AsyncLock/src/ 新增 IActor<TCommand> 接口
- [x] ActorBase<TCommand,TOut> 实现 IActor<TCommand>
- [x] 编译 + 单元测试 + git 提交

### 阶段 1：P1 状态机（改一处全局受益）

- [ ] StateMachine<T> 改为 ActorBase 风格 — **评估后跳过：单字段保护，AsyncLock 已足够**
- [ ] AgentStateMachine / UnifiedCircuitBreaker 跟进
- [ ] 编译 + 测试 + 提交

### 阶段 2：P0 Timer+锁（消除时序 bug 高发区）

- [x] ToolHealthMonitor（三重并发组合）— 混合方案: AsyncLock改Actor命令, _records保留ConcurrentDictionary
- [x] CronScheduler（调度器天然消息驱动）
- [x] ProcessHealthMonitor — Timer+AsyncLock(未使用) → Actor
- [x] ShellProcessWatchdog — Timer+ConcurrentDict → Actor
- [x] FlushGate — AsyncLock+List+Timer → Actor
- [x] DiagnosticLogWatcher — Timer+AsyncLock(未正确使用) → Actor
- [x] AwaySummaryService — Timer+AsyncLock+ConcurrentQueue → Actor
- [x] GoalHeartbeat — AsyncLock+PeriodicTimer → Actor
- [ ] TokenRefreshScheduler — **跳过：无 AsyncLock，ConcurrentDictionary 使用合理**
- [x] BridgeTokenRefreshScheduler — AsyncLock+Dict<string,Timer> → Actor+Consumer 独占三个字典+TCS
- [ ] ToolHypergraphScorer — **跳过：读多写少评分器，原子引用交换更合适**
- [x] RemoteCacheRefreshServiceBase — AsyncLock+Timer+ConcurrentDict → Actor+volatile ticks, _cache 保留 ConcurrentDictionary
- [x] TeamMemorySyncService — AsyncLock+Timer+文件监听 → Actor+TrySend 命令, ServiceEntity 改 ActorBase
- [x] SystemActuatorCommandContext — 拆分为 4 小类: ProcessOutputCollector+CwdTracker+OutputPersister+ProcessKillHelper
- [ ] DebounceTracker — **跳过：ConcurrentDictionary 使用合理**
- [ ] HookEventBroadcaster — **跳过：无 Timer，使用简单**
- [ ] FastModeService — **跳过：简单状态，AsyncLock 已足够**

### 阶段 3：P2 已有 Channel 雏形

- [x] LoopDiagnosticJournal
- [ ] InProcessMailbox — **跳过：ConcurrentDictionary 是路由表**
- [ ] AnalyticsFileSink — **跳过：批量+定时 flush 模式**
- [x] BuildQueueService — 拆分为 3 小类: CrossProcessBuildLock+SourceFingerprintCache(消除AsyncLock)+BuildResultBuffer
- [ ] DoctorTcpServer — **跳过：锁极短+瓶颈在 TCP I/O，AsyncLock 已最优**
- [ ] AgentOutputChannelManager — **跳过：Channel+ConcurrentDictionary+volatile 已线程安全，无锁可消除**
- [x] InProcessTeammateTask — 拆分为 2 小类: TeammateLoopRunner(循环逻辑)+TeammateCleanupHelper(清理逻辑), 主类 ActorBase 消除 AsyncLock
- [ ] GoalConflictMessenger — **跳过：ConcurrentDictionary<string,Channel> 路由表**

### 阶段 4：P3 AsyncLock 复杂状态

- [ ] PluginManager 半吊子清理 — **跳过：已是 Actor**
- [ ] TeamManager / TaskRuntime / ConcurrentDag 等

### 不改：P5 volatile 快照 + 不适合（纯限流/CAS/Dispose）

## 六、已完成改造汇总（19 个）

| # | 类名 | 原机制 | 改造内容 | commit |
|---|------|--------|----------|--------|
| 1 | IActor 接口 | 无 | 新增 IActor<TCommand> + InputCount 修复 | d301a1d57 |
| 2 | CronScheduler | Timer+ConcurrentDict+volatile | ActorBase+Timer→TrySend+Consumer独占 | 03241ae1f |
| 3 | LoopDiagnosticJournal | AsyncLock | ActorBase+Consumer独占 | cd627b675 |
| 4 | ProcessHealthMonitor | Timer+AsyncLock(未使用) | ActorBase+Timer→TrySend | ec26be7dc |
| 5 | ShellProcessWatchdog | Timer+ConcurrentDict | ActorBase+Timer→TrySend+Consumer独占 | 42d32505b |
| 6 | FlushGate<T> | AsyncLock+List+Timer | ActorBase+Timer→TrySend+TCS | a77db1aed |
| 7 | AwaySummaryService | Timer+AsyncLock+ConcurrentQueue | ActorBase+Timer→TrySend+volatile ticks | 532b3791a |
| 8 | GoalHeartbeat | AsyncLock+PeriodicTimer | ActorBase+Timer→TrySend+volatile ticks | dfd53736a |
| 9 | DiagnosticLogWatcher | Timer+AsyncLock(未正确使用) | ActorBase+Timer→TrySend+Consumer独占 | 3d604d3a0 |
| 10 | ToolHealthMonitor | Timer+AsyncLock+volatile+Dict | ActorBase+Timer→TrySend+ConcurrentDict保留+TCS | 37c23f61c |
| 11 | TokenBudgetManager | AsyncLock | ActorBase+TCS, 所有方法已async | d453ebd |
| 12 | UsdBudgetManager | AsyncLock | ActorBase+TCS, 所有方法已async | 6c19d2ab3 |
| 13 | BridgeTokenRefreshScheduler | AsyncLock+Dict<string,Timer> | ActorBase+Consumer独占三个字典+Timer→TrySend+TCS | 58b949cd3 |
| 14 | RemoteCacheRefreshServiceBase | AsyncLock+Timer+ConcurrentDict | ActorBase+volatile ticks, _cache 保留 ConcurrentDictionary | 71ed932ae |
| 15 | TeamMemorySyncService | AsyncLock+Timer+文件监听 | 拆分4小类+ActorBase, 757→280行 | 54130d27d |
| 16 | SystemActuatorCommandContext | 3×Timer+进程管理 | 拆分4小类: ProcessOutputCollector+CwdTracker+OutputPersister+ProcessKillHelper, 579→280行 | 3b39b4baa |
| 17 | BuildQueueService | Channel+3×ConcurrentDict+AsyncLock+跨进程锁 | 拆分3小类: CrossProcessBuildLock+SourceFingerprintCache+BuildResultBuffer, 676→300行 | 5ffe5d3e3 |
| 18 | InProcessTeammateTaskExecutor | AsyncLock+ConcurrentDict×2 | 拆分2小类: TeammateLoopRunner+TeammateCleanupHelper, ActorBase+7命令+读操作直接读, 944→680行 | c0d503f79 |

## 七、评估后跳过的候选（4 个）

| 类名 | 原因 |
|------|------|
| UnifiedCircuitBreaker | 锁持有极短(纯内存操作)+属性频繁同步读取+无定时器，AsyncLock 已最优 |
| AgentOutputChannelManager | 无锁可消除（Channel+ConcurrentDictionary+volatile 已线程安全） |
| DoctorTcpServer | 锁持有极短(Dictionary操作)+瓶颈在 TCP I/O 而非锁竞争 |
| InProcessMailbox/GoalConflictMessenger | ConcurrentDictionary 路由表模式，Channel 已线程安全 |

## 六、规模估算

| 优先级 | 数量 | 收益 |
|--------|------|------|
| P0 强烈推荐 | 17 | 消除 Timer+锁双重复杂度 |
| P1 推荐 | 5 | StateMachine 改一处全局受益 |
| P2 推荐 | 7 | 离 Actor 一步之遥 |
| P3 适合 | 45 | 消除 AsyncLock，状态独占 |
| P4 可选 | 6 | 部分适合 |
| P5 不推荐 | 17 | volatile 快照已是最优，别动 |
| 不适合 | 6+ | 纯限流/CAS/Dispose，别动 |

<!-- Auto Decision: 2026-09-12 -->
<!-- 决策: 先补 IActor 接口基建，再按 P1->P0->P2->P3 顺序改造 -->
<!-- 原因: 80 个候选改造需要类型安全的"发消息不关心具体 Actor 类型"能力；P1 StateMachine 是通用基类改一处全局受益 -->
<!-- 替代方案: 直接从 P0 ToolHealthMonitor 开始（放弃: 缺少 IActor 接口会导致大规模改造时调用方类型不安全）-->
<!-- 验证: 待基建完成后编译验证 -->

<!-- Auto Decision: 2026-09-12 -->
<!-- 决策: UnifiedCircuitBreaker/AgentOutputChannelManager/DoctorTcpServer 跳过 Actor 改造 -->
<!-- 原因: 锁持有时间极短或无锁可消除，改 Actor 增加复杂度但收益为零 -->
<!-- 替代方案: 保持 AsyncLock/ConcurrentDictionary 现有实现 -->
<!-- 验证: 编译通过，282 个 Scheduling 测试全部通过 -->

<!-- Auto Decision: 2026-09-12 -->
<!-- 决策: InProcessTeammateTaskExecutor 拆分 2 小类再改 Actor -->
<!-- 原因: 944 行大类，先提取 TeammateLoopRunner(循环逻辑)+TeammateCleanupHelper(清理逻辑)，主类 680 行改 Actor -->
<!-- 替代方案: 直接改 Actor（放弃: 违反 400+行先拆小类规则）-->
<!-- 验证: 编译通过，282 个 Scheduling 测试全部通过 ✅ -->
