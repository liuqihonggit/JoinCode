# TASK079 锁改造为只读+不可变+CAS — 杜绝死锁

## 背景

项目死锁问题困扰，目标：将显式锁改造为"只读类型 + 不可变容器 + CAS(Compare-And-Swap) 更新"模式，完全杜绝死锁。

项目已建立完整的无锁基础设施（`ActorBase` + `Channel<T>` + `ImmutableHamT` + `Interlocked` + `volatile`，20+ Actor 类，1066 处 ImmutableHamT，757 处 Interlocked），剩余显式锁分 4 类。

## 改造清单

### P0 立即可改（低难度，高收益）

| # | 位置 | 方案 | 消除锁数 | 状态 |
|---|------|------|---------|------|
| 1 | `modules/services/bridge/core/services/BridgeFaultInjection.cs` | `List`→`ImmutableList`+`Interlocked.Exchange`；`_handle`→`volatile`；`BridgeFault`改 record+init | 6 处静态锁 | ✅ 完成 |
| 2 | `modules/guard/guard/permission/permission2/tool_handlers/core/PermissionManager.cs` | `_currentMode`→`volatile PermissionMode` | 3 处 | ✅ 完成 |
| 3 | `modules/storage/vault/state/store/StoreSelector.cs` | `_currentValue`→`volatile`+不可变值 | 3 处 | ✅ 完成 |
| 4 | `modules/abstractions/abstractions/abs_ai/llm/chat/chat_core/SubAgentEventChannel.cs` | `List _buffer`→`Channel<ChatStreamEvent>`+`volatile bool` | 5 处 | ✅ 完成 |
| 5 | `modules/storage/scheduling/core/AgentTaskContext.cs` | `Dictionary`→`ConcurrentDictionary` 或 `ImmutableHamT`+CAS | 6 处 | ✅ 完成 |
| 6 | 6 处初始化双检查锁（LspService/FileBasedTaskService/Puppeteer/SandboxIpc/ConfigPersistent/CodeIndexer） | 统一改 `Interlocked.CompareExchange` | 6 处 | ⏸️ 保留（5 处 async 需等待语义，1 处 sync 需失败重试语义，AsyncLock/lock 合理） |

### P1 中难度

| # | 位置 | 方案 | 消除锁数 | 状态 |
|---|------|------|---------|------|
| 7 | `modules/perception/code_index/vector/BruteForceAnn.cs` | `Dictionary`→`ImmutableHamT`+`Interlocked.Exchange` | 1 处 RW 锁 | ✅ 完成 |
| 8 | `PersistentDreamTaskRegistry.cs` + `IDreamTaskRegistry.cs` + `PersistentGoalRegistry.cs` | `Dictionary`→`ImmutableHamT`+CAS | 17 处 | ✅ 完成 |
| 9 | `modules/cognition/hands/skills/search/SkillSearchService.cs` | 已用 `ImmutableHamT`，锁改 CAS | 1 处 | ⏸️ 保留（async 初始化需等待语义） |
| 10 | `modules/storage/scheduling/tasks/core/WorkflowTask.cs` | 已用 `ConcurrentDictionary`，锁改 CAS | 1 处 | ✅ 完成（CancelWorkflowAsync 去锁：Cts.Cancel 线程安全 + State 赋值幂等） |
| 11 | `modules/abstractions/infrastructure/utils/resilience/UnifiedCircuitBreaker.cs` | FSM 状态→`CbState` record+`Volatile.Read`+`Interlocked.CompareExchange` CAS | 10 处 | ✅ 完成（去掉 Fsm 源码生成器+AsyncLock，不可变快照+CAS 无锁） |
| 12 | `modules/cognition/hands/system/PreventSleepService.cs` | `_isSleepPrevented`→`volatile`+`Interlocked` | 2 处 | ✅ 完成 |

### P2 架构调整（需评估，Actor 模型）

| # | 位置 | 方案 | 风险 | 状态 |
|---|------|------|------|------|
| 13 | `modules/llm/agents/Coordinator/Swarm/SwarmPermissionBridge.cs` | 改 Actor 模型 | 中 | ✅ 完成（去锁即可，_permissionStates 已无锁 CAS，复合操作最终一致） |
| 14 | `modules/storage/clock/goal/core/goal_engine/GoalEngine.cs` | 改 Actor 模型，13 处 `_stateLock` 全消除 | 高 | ✅ 完成（Channel<GoalEngineCommand> + 单 Consumer 线程串行化，属性 volatile 读，接口不变） |

### GoalEngine Actor 改造设计

**范围**：`GoalEngine.cs`（1010 行，13 处锁，13 个接口成员）。接口 `IGoalEngine` 不变，调用方无感知。

**方案**：内部 `Channel<GoalEngineCommand>` + 单 Consumer 线程
1. 定义 `GoalEngineCommand` 基类（含 `TaskCompletionSource<object?>` 回复）
2. 定义 13 个具体命令：`StartCmd`/`PauseCmd`/`ResumeCmd`/`ClearCmd`/`RehydrateCmd`/`MarkCompletedCmd`/`MarkUnmetCmd`/`WaitForCompletionCmd`/`SetGraphDefinitionCmd`/`SetSessionIdCmd`
3. `GoalEngine` 持有 `Channel<GoalEngineCommand>` + Consumer 线程（构造时启动，DisposeAsync 时停止）
4. 每个 `Task` 返回方法变成 `_channel.Writer.WriteAsync(cmd)` + `await cmd.Reply.Task`
5. `void` 方法（`SetGraphDefinition`/`SetSessionId`）同步发消息（fire-and-forget 或 WriteAsync + await Reply）
6. Consumer 串行处理命令（原方法体去锁），所有可变状态由 Consumer 独占
7. 属性（`CurrentState`/`IsRunning`/`HasGraphDefinition`）用 `volatile` 读（`_state`/`_goalGraph` 改 volatile）
8. `_stateLock` 删除

**为什么不能直接去锁**（与 SwarmPermissionBridge 不同）：
- 锁保护状态机生命周期（启动/暂停/恢复/心跳），有严格时序依赖
- `StartAsync`：检查 `_state` + 启动 `_engineLoop`，去锁导致重复启动
- `Pause`/`Resume` 并发导致状态混乱
- `_engineCts` 被覆盖导致资源泄漏

**实施步骤**：
1. 定义消息类型 + Channel + Consumer 骨架
2. 改造 13 个方法为发消息
3. Consumer 实现原方法体（去锁）
4. 属性改 volatile 读
5. 编译 + 测试 + 提交
| 15 | `modules/llm/agents/Coordinator/Core/services/AgentStateMachine.cs` | `Lock` 改 `internal` 或改 Actor | 中 | ✅ 完成（Lock 改 internal，收窄封装） |

### 必须保留锁（CAS 无法替代，不改造）

- `ConsoleSuppressionGate.cs` + `ErrorConsole.cs` — Console 全局副作用，需原子替换多个 writer
- `RgEngine.cs` — PLINQ 流式回调互斥（`onFileResult` 可能非线程安全）
- `CodeIndexer.cs` — 双检查锁懒初始化（#6 同类，初始化操作非简单赋值）
- `HnswAnn.cs` — HNSW 多层邻接表图，不可变复制代价过高
- `PithosDb.cs` + `LeveledCompactor.cs` — LSM-Tree 核心数据结构，RW 锁合理
- `LruBlockCache.cs` + `S3FifoBlockCache.cs` — LRU/S3-FIFO 链表结构复杂
- `BridgeDeviceTokenService.cs` + `McpPkceAuthProvider.cs` + `McpAuthToolHandlers.cs` — HTTP/文件 IO 互斥
- `HighWaterMarkManager.cs` — KV 读-改-写原子性
- `CrossProcessLock.cs` + `NamedMutexMailboxLock.cs` + `PithosKvStoreLeaseFactory.cs` + `LeasedKvStoreAdapter.cs` — 跨进程互斥必须用 Mutex
- `AsyncLockedDictionary.cs` — per-key 异步 factory 串行化
- 所有限流锁（`AgentCoordinator`/`ForkSubAgentManagerActor`/`GoalGraphEngine`/`AgentBase`/GH 下载/Downloader/`AgentExecutionEngine`） — 限流非互斥
- `OrderedLockManager.cs` — 已用排序预防死锁

### 补充改造（调研遗漏项）

| # | 位置 | 方案 | 消除锁数 | 状态 |
|---|------|------|---------|------|
| 16 | `modules/cognition/brain/context/services/chat/core/SteerInputMailbox.cs` | `Dictionary`+`lock`→`ConcurrentDictionary`+`ConcurrentQueue` | 3 处 | ✅ 完成 |
| 17 | `modules/async_primitives/async_lock/actor/EventStream.cs` | `ConcurrentDictionary`+`lock`→`ImmutableHamT`+`ImmutableInterlocked.Update` CAS | 2 处 | ✅ 完成 |
| 18 | `modules/storage/scheduling/core/ExecutionContext.cs` | `List<Task>`+`AsyncLock`→`ConcurrentDictionary<Task,byte>` 无锁 | 1 处 | ✅ 完成（AddRunningTask/GetSnapshot/Cleanup/GetCount/Dispose 全去锁） |
| 19 | `modules/abstractions/abstractions/abs_core/configuration/localization/L.cs` | `AsyncLock.LockOrCrash`→`Volatile.Write` 原子更新 | 1 处 | ✅ 完成（Initialize 仅启动时调用，引用类型赋值原子） |

## 验收标准

每项改造须：
1. 编译通过（跨域变更全仓编译 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`）
2. 现有单元测试全绿（行为不变）
3. `git commit` 单独提交
4. 无新增分析器警告

## 决策依据

- AGENTS.md "死锁处理用 Actor 邮箱模型"（消息传递替代共享锁）
- AGENTS.md "只读类型+不可变容器+CAS" 已是项目既定模式
- 参考实现：`AgentServiceImpl.cs:51,97-104`（`volatile ImmutableHamT` + `Interlocked.CompareExchange` CAS 循环）
