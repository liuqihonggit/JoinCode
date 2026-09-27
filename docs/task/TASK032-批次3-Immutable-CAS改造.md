# TASK032 — 批次3: ConcurrentDictionary → ImmutableDictionary + CAS 改造

## 任务概述

将工程中所有适合改造的 `ConcurrentDictionary` 改为 `ImmutableDictionary` + 无锁 CAS 更新,消除锁竞争,提高并发读取性能。

## 分析报告(2026-09-26 定位)

### 总体统计

| 分类 | 数量 | 说明 |
|------|------|------|
| **A类(适合改造)** | **63 处** | 低频写入+高频读取,适合改为 ImmutableDictionary+CAS |
| **B类(需评估)** | **58 处** | 高频并发写入/多线程同时修改,需逐个评估 |
| **C类(不适合)** | **19 处** | DTO/上下文对象/管道传递引用,保持现状 |
| **合计** | **140 处** | 已排除测试文件、生成器、MapRegistry/SecondaryIndex |

## CAS 模式模板(复用 MapRegistry 范式)

```csharp
// 字段声明: ImmutableDictionary + 非volatile(ImmutableInterlocked.Update不支持volatile字段)
private ImmutableDictionary<TKey, TValue> _dict = ImmutableDictionary<TKey, TValue>.Empty;

// 读取(无锁,获取快照)
var current = Volatile.Read(ref _dict);
current.TryGetValue(key, out var value);

// 添加(CAS循环)
ImmutableInterlocked.Update(ref _dict, d => d.Add(key, value));

// 添加或更新(CAS循环)
ImmutableInterlocked.Update(ref _dict, d => d.SetItem(key, value));

// 删除(CAS循环)
ImmutableInterlocked.Update(ref _dict, d => d.Remove(key));

// 计数
public int Count => Volatile.Read(ref _dict).Count;

// 遍历(获取快照)
foreach (var kvp in Volatile.Read(ref _dict)) { ... }

// Keys/Values 快照
public IReadOnlyCollection<TKey> GetAllKeys() => Volatile.Read(ref _dict).Keys;
```

### 注意事项

1. `ImmutableInterlocked.Update` 不接受 `volatile` 字段(CS0420),用非volatile字段 + `Volatile.Read`
2. `ImmutableArray<T>` 是值类型不能用 `volatile`,改用 `ImmutableList<T>`(引用类型)
3. CAS 循环在高竞争下可能重试多次,但 A 类(低频写入)重试概率极低
4. `ConcurrentDictionary.TryAdd` → `ImmutableInterlocked.Update(ref _dict, d => d.ContainsKey(key) ? d : d.Add(key, value))`
5. `ConcurrentDictionary.AddOrUpdate` → `ImmutableInterlocked.Update(ref _dict, d => d.SetItem(key, value))`
6. `ConcurrentDictionary.GetOrAdd` → 需先读快照检查,不存在再CAS添加

## 改造策略

### 按消费方数量升序(先改影响范围小的建立模式)

#### P0: 消费方1处(6个,建立模式) ✅
- [x] `RgEngine.GitignoreCache` — 静态gitignore缓存
- [x] `BuddyService._cache` — Buddy信息缓存
- [x] `DeferredMailService._locks` — AsyncLock缓存
- [x] `IntentCollector._locks` — AsyncLock缓存
- [x] `RangeDownloader._proxiedClients` — HttpClient缓存
- [x] `GlobMatcher.Cache` — 静态glob正则缓存

#### P1: 消费方2处(5个) ✅
- [x] `SkillSearchService._tagIndex` — 标签索引
- [x] `SkillSearchService._nameIndex` — 名称索引
- [x] `VariableResolver._parseCache` — 解析缓存
- [x] `StructuredOutputToolHandler._validationCache` — 验证缓存
- [x] `BridgeServerRegistries._routes` — 路由表

#### P2: 消费方3-4处(15个) ✅
- [x] `UsageTracker._sessionIndex`
- [x] `PeerDiscoveryService._peers`
- [x] `ShellSedInterceptMiddleware._fallbackEdits`
- [x] `MethodNameCache._cache`
- [x] `SystemActuatorBase._capabilityCache`
- [x] `GoalConflictMessenger._channels`
- [x] `GoalGraphTemplateRegistry._templates`
- [x] `ContractChangeNotificationRouter._queues`
- [x] `CrashSnapshotStore._byId`
- [x] `PluginResourceBase._consumers`
- [x] `BridgeWorkCompletionTracker._completed`
- [x] `LspServerRegistry._extensionMap`
- [x] `CodeSessionRepo._store`
- [x] `VcrService._cassetteCache`
- [x] `StructuredOutputToolHandler._schemas`

#### P3: 消费方5-8处(20个) ✅ (19/20, TeamMemorySyncService._remoteEntries推迟到P4)
- [x] `CommitCommand.ReadConfirmedSessions`
- [x] `SessionScope._typeIndex`
- [x] `BotNameGenerator.s_usedNames`
- [x] `RemoteCacheRefreshServiceBase._cache`
- [x] `DeferredMailService._pending`
- [x] `IntentCollector._intentsByFile`
- [x] `TelemetryService._metrics`
- [x] `TelemetryService._activeSpans`
- [x] `DebounceTracker._timers`
- [x] `ServiceInterceptRegistry._interceptors`
- [x] `ThinkingStore._entries`
- [ ] `TeamMemorySyncService._remoteEntries` — 推迟到P4(需与_localEntries一起改,影响Scanner/Transfer/Resolver)
- [x] `ReferenceIndex._keywordIndex`
- [x] `ResourceReferenceGraph._byTarget`
- [x] `ReferenceIndex._references`
- [x] `PluginApprovalRegistry._requests`
- [x] `UiResourceTable._resources`
- [x] `TrustedDevice._devices`
- [x] `WorkSecret._secrets`
- [x] `PeerSessions._routes`

#### P4: 消费方6-12处(12个) ✅ (10/12, TeamMemorySyncService+ResourceReferenceGraph已在P3完成)
- [x] `McpSkillProvider._clients`
- [x] `DebounceTracker._internalWriteTimestamps` (P3已改)
- [x] `LspServerRegistry._servers`
- [x] `McpSkillProvider._adapters`
- [x] `SessionCache._entries`
- [x] `ServiceHost._services`
- [x] `UnmanagedResourceTable._resources`
- [x] `McpSkillProvider._mcpSkills`
- [x] `SystemActuatorRegistry._tasks`
- [ ] `TeamMemorySyncService._localEntries` + `_remoteEntries` — C类:Actor邮箱内单线程,引用被Scanner/Transfer/Resolver持有,改Immutable需重构所有消费方
- [x] `PipeRegistry._pipes`
- [x] `ResourceReferenceGraph._references` / `._byConsumer` (P3已改)

#### P5: 消费方21处(1个,最大) ✅
- [x] `MemoryStore._memories` — 核心存储(21消费方)

### B类(需评估,31处字段) — 2026-09-27 评估完成

#### B1类(适合改Immutable+CAS,低频写入+高频读取,4处) ✅
- [x] `InvariantRegistry._registrations` — 启动时注册,运行时只读
- [x] `SessionRouter._scopes` — 静态会话路由,低频创建/销毁
- [x] `LockRegistry._locks` — 静态锁注册表,低频写入
- [x] `SshPortForwardManager._forwards` — SSH端口转发,低频写入

#### B2类(可能适合,中频写入,需进一步分析,12处) — 8处完成,4处跳过
- [x] `BusTransport._clientConnections` — 连接管理,连接建立/断开时写入
- [x] `MeshTransport._peerConnections` — peer连接缓存
- [x] `NamedPipeTransport._connections` — 命名管道连接
- [x] `SshSessionManager._sessions` — SSH会话管理
- [x] `SessionHookManager._sessionStores` — 会话钩子存储
- [x] `StateService._fallbackStorage` — 状态服务回退存储
- [x] `PluginHookInjector._injectedHooks` — 插件钩子注入
- [x] `SessionScope._entities` — 会话实体表
- [ ] `MailboxBase._agentMailboxes` — 跳过:Actor邮箱路由表高频读写,CAS重试+GC压力退化
- [ ] `SupervisedActor._children` — 跳过:同上,Actor子Actor管理高频读写
- [ ] `RouterActor._children` — 跳过:同上
- [ ] `PluginRuntimeRegistry._entries` — 跳过:已用TryUpdate CAS循环,改ImmutableDictionary lambda副作用问题

#### B3类(不适合改,高频并发写入/值类型可变/Actor内部状态,15处)
- `GraphExecutionContext._nodeStates` — 高频写入(图执行过程中频繁更新)
- `TeammateRegistry._activeTeammates` + `_pendingMessages` — 高频写入+public暴露ConcurrentDictionary
- `ActorBase._askWaitGraph` — 静态ask等待图,高频写入(每次ask写入+删除)
- `ExecutionContext._completedTaskIds` — 高频写入(任务完成时写入)
- `ParallelTaskScheduler._scheduledTasks` + `_taskDependencies` + `_reverseDependencies` — 高频写入(任务调度)
- `TaskService._tasks` — 高频写入(任务CRUD)
- `WorkflowTask._activeWorkflows` — 高频写入(工作流CRUD)
- `MonitorMcpTask._sessions` — 高频写入(监控会话CRUD)
- `ParallelExecutionEngine._agentExecutionRecords` — 高频写入(执行记录)
- `TaskRuntime._tasks` — 高频写入(运行时任务CRUD)
- `AgentTaskContext._metadata` — 中频写入但值类型JsonElement
- `InMemoryFileSystem._files` + `_directories` — 文件系统操作,高频写入
- `ServiceMessageBus._subscribers` + `_messageHistory` — 事件总线,高频写入
- `AppEventBus._subscribers` — 事件总线,高频写入
- `GlobalBuildQueue._pending` — 构建队列,高频写入+值类型TaskCompletionSource

### C类(不适合,19处) — 保持现状

## 进度跟踪

| 日期 | 批次 | 改造数 | 累计 | 状态 |
|------|------|--------|------|------|
| 2026-09-26 | P0 | 6 | 6/63 | ✅ 完成 |
| 2026-09-26 | P1 | 5 | 11/63 | ✅ 完成 |
| 2026-09-26 | P2 | 15 | 26/63 | ✅ 完成 |
| 2026-09-26 | P3 | 19 | 45/63 | ✅ 完成(_remoteEntries推迟到P4) |
| 2026-09-27 | P4 | 10 | 55/63 | ✅ 完成(TeamMemorySyncService标C类) |
| 2026-09-27 | P5 | 1 | 56/63 | ✅ 完成(MemoryStore._memories改ImmutableDictionary+CAS) |
| 2026-09-27 | B1 | 4 | 60/63 | ✅ 完成(InvariantRegistry+SessionRouter+LockRegistry+SshPortForwardManager) |
| 2026-09-27 | B2 | 8 | 68/63 | ✅ 8处完成,4处跳过(MailboxBase/SupervisedActor/RouterActor高频读写+PluginRuntimeRegistry已用CAS) |

## 批次3完成总结

- **A类(适合改造)**: 56/63处完成(P0-P5),剩余7处为C类(TeamMemorySyncService等)
- **B1类(低频写入+高频读取)**: 4处完成
- **B2类(中频写入)**: 8处完成,4处跳过(MailboxBase/SupervisedActor/RouterActor高频读写不适合,PluginRuntimeRegistry已用CAS循环)
- **B3类(高频并发写入/Actor内部)**: 15处保持现状(不适合Immutable+CAS)
- **C类(不适合)**: 7处保持现状(DTO/上下文对象/管道传递引用)
- **总计**: 68处改造完成,剩余保持现状

## 验收标准

1. 每个 P 批次编译通过(增量编译)
2. 每个 P 批次对应单元测试通过
3. 每个 P 批次 git 提交
4. 全部完成后全量编译 + 全量单元测试通过
5. PR 创建并 auto-merge 启用

## 决策记录

<!-- 🤖 Auto Decision: 2026-09-26 -->
<!-- 决策: 按消费方数量升序改造,先改1-2消费方的建立CAS模式 -->
<!-- 原因: 影响范围小,建立模式后再推广到大消费方 -->
<!-- 替代方案: 按文件路径分组改造(会跳过消费方少的,不利于建立模式) -->
