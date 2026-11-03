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
| 10 | `modules/storage/scheduling/tasks/core/WorkflowTask.cs` | 已用 `ConcurrentDictionary`，锁改 CAS | 1 处 | ⏸️ 保留（保护 runState 两字段原子性） |
| 11 | `modules/abstractions/infrastructure/utils/resilience/UnifiedCircuitBreaker.cs` | FSM 状态→`volatile` 不可变快照+CAS | 10 处 | ⏸️ 保留（FSM 状态机复合操作，需重构 Fsm 为不可变，工作量过大） |
| 12 | `modules/cognition/hands/system/PreventSleepService.cs` | `_isSleepPrevented`→`volatile`+`Interlocked` | 2 处 | ✅ 完成 |

### P2 架构调整（需评估，Actor 模型）

| # | 位置 | 方案 | 风险 | 状态 |
|---|------|------|------|------|
| 13 | `modules/llm/agents/Coordinator/Swarm/SwarmPermissionBridge.cs` | 改 Actor 模型 | 中 | ⏳ |
| 14 | `modules/storage/clock/goal/core/goal_engine/GoalEngine.cs` | 改 Actor 模型，13 处 `_stateLock` 全消除 | 高 | ⏳ |
| 15 | `modules/llm/agents/Coordinator/Core/services/AgentStateMachine.cs` | `Lock` 改 `internal` 或改 Actor | 中 | ⏳ |

### 必须保留锁（CAS 无法替代，不改造）

- `ConsoleSuppressionGate.cs` — Console 全局副作用，需原子替换多个 writer
- `HnswAnn.cs` — HNSW 多层邻接表图，不可变复制代价过高
- `PithosDb.cs` + `LeveledCompactor.cs` — LSM-Tree 核心数据结构，RW 锁合理
- `LruBlockCache.cs` + `S3FifoBlockCache.cs` — LRU/S3-FIFO 链表结构复杂
- `BridgeDeviceTokenService.cs` + `McpPkceAuthProvider.cs` + `McpAuthToolHandlers.cs` — HTTP/文件 IO 互斥
- `HighWaterMarkManager.cs` — KV 读-改-写原子性
- `CrossProcessLock.cs` + `NamedMutexMailboxLock.cs` + `PithosKvStoreLeaseFactory.cs` + `LeasedKvStoreAdapter.cs` — 跨进程互斥必须用 Mutex
- `AsyncLockedDictionary.cs` — per-key 异步 factory 串行化
- 所有限流锁（`AgentCoordinator`/`ForkSubAgentManagerActor`/`GoalGraphEngine`/`AgentBase`/GH 下载/Downloader/`AgentExecutionEngine`） — 限流非互斥
- `OrderedLockManager.cs` — 已用排序预防死锁

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
