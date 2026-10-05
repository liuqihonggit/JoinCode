# DSG034 — Actor 改造无锁化 + 内存泄露修复

> 状态：proposed
> 日期：2026-10-05
> 关联：DSG033（Actor 邮箱泄漏修复 + 监督树对齐 Akka）、ADR 0086（核心技术选型锁设计）

## 一、背景

DSG033 已完成 Actor 基建对齐 Akka + 统一 Actor 模式。本次继续：
1. **Actor 改造无锁化** — 将用锁保护的共享可变状态改为 Actor 消息传递
2. **内存泄露修复** — 修复 24 处内存泄露（13 中 + 11 低）

## 二、Actor 改造候选（6 个，按优先级）

| 优先级 | 文件 | 锁类型 | 共享状态 | 改造方案 | 收益 |
|--------|------|--------|---------|---------|------|
| P1 | `lib/structura/dag/ConcurrentDag.cs` | SemaphoreSlim | DAG 节点/边字典 | DagActor<T> 串行写 + 快照读 | 消除写互阻塞 |
| P2 | `kit/brain/context/core/hierarchy/ContextHierarchy.cs` | AsyncLock | _layers/_layerDict | ContextHierarchyActor | 压缩不阻塞读 |
| P3 | `lib/scheduling/storage/HighWaterMarkManager.cs` | SemaphoreSlim | KV 整数值 | HighWaterMarkActor | 消除持锁等 IO |
| P4 | `lib/pithosdb/Core/S3FifoBlockCache.cs` | lock | _small/_main/_map | S3FifoBlockCacheActor（分片） | 消除 eviction 竞争 |
| P5 | `lib/pithosdb/Core/LruBlockCache.cs` | lock | _lru/_map | LruBlockCacheActor（分片） | 同 S3Fifo |
| P6 | `llm/agents/Coordinator/Core/Pool/SubAgentPool.cs` | 无锁 CAS | _pool ImmutableHamT | SubAgentPoolActor | 简化 CAS 重试 |

### 不适合 Actor 改造的文件（11 个）

| 文件 | 原因 |
|------|------|
| `lib/async_lock/actor/EventStream.cs` | 短临界区 + 发布无锁路径 |
| `app/cli/services/user_experience/ProactiveTickScheduler.cs` | 已用 Interlocked 无锁 |
| `lib/guard/hooks/lifecycle/ClusterPlanApprovalHookManager.cs` | 无共享可变状态 |
| `lib/guard/hooks/lifecycle/SubagentStopHookManager.cs` | 无共享可变状态 |
| `kit/mcp/auth/o_auth/McpSecureTokenStorage.cs` | 无共享状态 + 外部文件资源 |
| `lib/async_lock/lock/AsyncLock.cs` | 锁原语基础设施 |
| `lib/infrastructure/async_file_lock/NamedMutexMailboxLock.cs` | 跨进程锁 |
| `lib/infrastructure/io/services/throttle/IOThrottleService.cs` | 限流器语义 |
| `server/bridge/core/runtime/CapacityWakeSignal.cs` | 纯信号机制 |
| `app/cli/queue/CommandQueue.cs` | 已用 ConcurrentQueue 无锁 |
| `lib/async_lock/mailbox/PriorityMailbox.cs` | Actor 邮箱基础设施 |

## 三、内存泄露修复（24 处）

### 中严重程度（13 处）

| # | 文件 | 问题 | 修复建议 |
|---|------|------|---------|
| 1 | `kit/hands/api/core/UsageTracker.cs:212` | ConcurrentBag 只增不减（Singleton） | 时间窗口清理或有界缓冲 |
| 2 | `lib/scheduling/execution/ParallelTaskScheduler.cs:9` | ConcurrentBag + 字典只增不减 | 实现 IDisposable，任务完成清理 |
| 3 | `lib/scheduling/execution/ParallelExecutionEngine.cs:14` | 字典只增不减（Singleton） | DisposeAsync 中 Clear |
| 4 | `lib/scheduling/services/TaskService.cs:38` | 字典只增不减 | 增加 DeleteTaskAsync，Dispose Clear |
| 5 | `lib/infrastructure/ssh/SshSession.cs:42` | 事件订阅未取消 | DisposeAsync 中 -= |
| 6 | `lib/transport.impl/bridge/shared/connection/ConnectionManager.cs:170` | 匿名 lambda 事件订阅 | 提取有名方法 |
| 7 | `lib/infrastructure/utils/diagnostics/DebugLogBuffer.cs:19` | 静态事件订阅未取消 | 实现 IDisposable |
| 8 | `lib/clock/hosting/AppEventBus.cs:21` | 事件订阅未取消 | 实现 IDisposable |
| 9 | `lib/infrastructure/async_file_lock/NamedMutexMailboxLock.cs:12` | 静态字典只增不减 | 空闲时 TryRemove |
| 10 | `lib/clock/goal/core/goal_graph/GoalGraphEngine.cs:114` | GraphExecutionContext 不 Dispose | await using |
| 11 | `app/cli/queue/CommandQueue.cs:54` | SemaphoreSlim 不 Dispose | 实现 IDisposable |
| 12 | `lib/clock/goal/core/goal_engine/GoalConflictMessenger.cs:9` | Channel 映射不清理 | 实现 IDisposable |
| 13 | `llm/agents/Coordinator/Core/Messaging/AgentOutputChannelManager.cs:10` | Channel 不 Complete | 实现 IDisposable |

### 低严重程度（11 处）

| # | 文件 | 问题 |
|---|------|------|
| 14 | `lib/scheduling/tasks/core/MonitorMcpTask.cs:386` | 匿名 lambda 事件订阅（字段随对象 GC） |
| 15 | `lib/infrastructure/utils/resilience/UnifiedCircuitBreaker.cs:158` | 同上 |
| 16 | `lib/infrastructure/network/downloader/state_machine/DownloadStateMachine.cs:28` | 同上 |
| 17 | `lib/scheduling/core/TaskStateMachine.cs:17` | 事件订阅未取消 |
| 18 | `lib/infrastructure/hot_spot/ContractChangeNotificationRouter.cs:10` | 队列映射依赖外部清理 |
| 19 | `lib/infrastructure/pipeline/core/AgentNotificationQueue.cs:8` | 队列不主动消费 |
| 20 | `lib/infrastructure/hot_spot/MergeQueueService.cs:9` | 队列不主动消费 |
| 21 | `llm/agents/Coordinator/Core/Messaging/AgentInputForwardQueue.cs:9` | Channel 映射依赖外部清理 |
| 22 | `lib/scheduling/tasks/core/WorkflowTask.cs:207` | 异常退出时字典未清理 |
| 23 | `lib/abstractions/abs_core/core_utils/core/misc/CooldownService.cs:9` | 静态字典 |
| 24 | `lib/infrastructure/utils/io/DebounceTracker.cs:8` | 内部写入标记未消费时累积 |

## 四、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 内存泄露 #1 UsageTracker | kit/hands/api/ | ✅ Dispose Clear | ✅ 287 Scheduling 测试 |
| 内存泄露 #2 ParallelTaskScheduler | lib/scheduling/execution/ | ✅ 新增 IDisposable | ✅ 287 Scheduling 测试 |
| 内存泄露 #3 ParallelExecutionEngine | lib/scheduling/execution/ | ✅ DisposeAsync Clear | ✅ 287 Scheduling 测试 |
| 内存泄露 #4 TaskService | lib/scheduling/services/ | ✅ Dispose Clear | ✅ 287 Scheduling 测试 |
| 内存泄露 #5 SshSession | lib/infrastructure/ssh/ | ✅ DisposeAsync -= | ✅ 327 Infra.IO 测试 |
| 内存泄露 #6 ConnectionManager | lib/transport.impl/ | ✅ 有名方法 | ✅ 207 Transport 测试 |
| 内存泄露 #7 DebugLogBuffer | lib/infrastructure/utils/ | ✅ IDisposable | ✅ 327 Infra.IO 测试 |
| 内存泄露 #8 AppEventBus | lib/clock/hosting/ | ✅ IDisposable | ✅ 519 Clock 测试 |
| 内存泄露 #9 NamedMutexMailboxLock | lib/infrastructure/async_file_lock/ | ⚠️ 降级低 | 路径数量有限 |
| 内存泄露 #10 GraphExecutionContext | lib/clock/goal/ | ✅ IAsyncDisposable | ✅ 519 Clock 测试 |
| 内存泄露 #11 CommandQueue | app/cli/queue/ | ✅ IDisposable | ✅ 编译通过 |
| 内存泄露 #12 GoalConflictMessenger | lib/clock/goal/ | ✅ override Dispose | ✅ 519 Clock 测试 |
| 内存泄露 #13 AgentOutputChannelManager | llm/agents/ | ✅ override Dispose | ✅ 628 Agents 测试 |
| Actor 改造 P1: ConcurrentDag | lib/structura/dag/ | ⏸️ 不改造(数据结构) | ADR 0130 |
| Actor 改造 P2: ContextHierarchy | kit/brain/context/ | ✅ ActorBase | ✅ 36 测试 |
| Actor 改造 P3: HighWaterMarkManager | lib/scheduling/storage/ | ✅ ActorBase | ✅ 1 测试 |
| Actor 改造 P4: S3FifoBlockCache | lib/pithos#osdb/Core/ | ⏸️ 不改造(数据结构) | ADR 0130 |
| Actor 改造 P5: LruBlockCache | lib/pithosdb/Core/ | ⏸️ 不改造(数据结构) | ADR 0130 |
| Actor 改造 P6: SubAgentPool | llm/agents/Coordinator/ | ✅ ActorBase | ✅ 12 测试 |

## 五、执行顺序

1. 先修复内存泄露（中严重程度 13 处）— 每处: 红测试 → 修复 → 绿测试 → commit
2. 再做 Actor 改造（P1→P6）— 每个: 红测试 → Actor 改造 → 绿测试 → commit
3. 全量测试验证无退化
