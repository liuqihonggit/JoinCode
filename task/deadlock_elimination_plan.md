# 大型工程消除死锁和偶发异常 — 改造计划

> 创建时间：2026-09-27
> 目标：补齐"双 Tell 无 Ask 架构"差距，消除死锁和偶发异常
> 策略：先基建+压测 → 再业务；高危先处理 → 中危次之

---

## 一、工程现状总结（三个探索报告）

### 1.1 锁与同步原语（高度成熟）

| 指标 | 数量 | 说明 |
|------|------|------|
| `lock` 语句 | 5 处（生产） | 全在 `ErrorConsole.cs` 同步方法，保护 Console 输出 |
| `Monitor.Enter/Exit` | 3 处 | `OrderedLockManager.cs` 有序锁管理器 |
| `SemaphoreSlim` | 136 处 | 核心封装在 `AsyncLock.cs`，Actor 限流/邮箱信号/下载限流 |
| `Interlocked.` | 811 处 | 大量使用，CAS 原子操作 |
| `volatile` | 148 处 | `volatile ImmutableDictionary` 无锁读 |
| `ConcurrentDictionary` 等 | 271 处 | 并发容器 |
| `ImmutableInterlocked.Update` | 183 处 | **核心无锁更新模式** |
| `Channel<>` | 100+ 处 | Actor 邮箱/传输/构建队列/背压通道 |
| `AsyncLock`（自研） | 277 处 | 核心异步锁 |
| `.GetAwaiter().GetResult()` | ~35 处（真实） | 同步入口点适配异步 API |
| `.Result`（Task.Result） | ~5 处 | 同步阻塞 |
| `.Wait()` | ~3 处 | 测试/基准测试 |

**关键结论**：
- 无 async over lock 反模式（源码生成器 JCC4003 编译期拦截）
- `ReaderWriterLockSlim` 已全部移除，改用无锁 CAS
- `lock` 近乎消除，主流已迁移到 AsyncLock/Actor/无锁 CAS
- 同步阻塞主要在"同步入口点适配异步 API"场景，有源码生成器守卫

### 1.2 通讯模型与 Actor（高度成熟）

**已有基础设施**：
- `ActorBase<TCommand, TOut>` — 全双工 Actor 基类，双 Channel（输入有界+输出无界）
- `SupervisedActor` — 监督 Actor，树形父子 + 监督策略
- `RouterActor` — 路由 Actor，多 Worker 负载分发
- `GatewayActor` — 网关 Actor，限流/重试/熔断
- `MailboxBase` — 统一邮箱基类（进程内/有名管道/文件/网络四种）
- `PriorityMailbox` — 优先级邮箱（高/普通/低三通道）
- `BackpressureChannel<T>` — 背压通道，不丢弃消息，高水位反向通知
- 60+ 个 ActorBase 派生类遍布各层

**Tell/Ask 双模式**：
- Tell：`SendAsync`/`TrySend`/`TellAsync`（射后不理）
- Ask：`AskAwait`/`AskWithRetryAsync`（等待回复，超时 10s + 等待图环检测）
- Ask 死锁防护：`ActorAskDeadlockException`（超时）+ `ActorCyclicAskException`（环检测）
- `AsyncFlowIdentity`：统一传递 FlowId（死锁检测）+ ActorId（循环 Ask 检测）

**背压机制**：
- 四档预设：CodingAgentTask(2000) / LlmGateway(200) / Router(1000) / Build(100)
- 水位线：High(容量×0.8) / Critical(容量×0.95)
- 反向通知 + 死信队列（SendFailed 事件）

**重试机制**：
- `AskWithRetryAsync`：16 次 + 指数退避 + 全图环检测
- `RetryPolicy` + `ExponentialBackoff` + 熔断器三态

**幂等机制**：
- `IIdempotent` 标记接口（重试安全）
- **无显式 IdempotencyKey/BusinessFlowId 键控去重**

### 1.3 数据结构与属性暴露（85%+ 达标）

| 数据结构 | 数量 | 用途 |
|----------|------|------|
| `Dictionary<` | 1932 处 | 80%+ 为局部临时变量，~15 处为字段级（隐患） |
| `FrozenDictionary<` | 323 处 | 静态配置表，构建一次冻结 |
| `FrozenSet<` | 399 处 | 静态集合，O(1) 查找 |
| `Immutable*` | 459 处 | 不可变容器 + CAS 无锁更新 |
| `ConcurrentDictionary<` | 156 处 | 并发容器 |
| `BitMask` | 43 处 | 状态机转移表，O(1) 位运算 |
| `Span<`/`ReadOnlySpan<` | 170 处 | 零 GC 字符串处理 |
| `HashMap`/`HAMT` | 0 处 | 未使用自定义 HAMT |

**统一数据源**：
- `DangerousCommandCatalog`（26 处引用）— 委托模式，单数据源
- `RetainedDeviceNames`（23 处引用）— 委托模式，单数据源

---

## 二、差距清单（按危险等级分类）

### 🔴 高危（可能导致死锁/数据竞争/确定性 bug）

#### H1：字段级可变 Dictionary（~15 处）

并发访问时线程不安全，可能导致数据竞争。

| 文件:行 | 字段 | 类型 | 风险 |
|---------|------|------|------|
| `llm\core\Adapters\LLM\core\ChatClient.cs:24` | `_plugins` | `Dictionary<string, IToolGroup>` | 多线程注册插件不安全 |
| `llm\reasoning\Weight\Graph\EvidenceGraph.cs:52-53` | `_nodes`/`_edges` | `Dictionary<...>` | 证据图并发访问不安全 |
| `llm\agents\services\Support\AgentRoleProfileRegistry.cs:17` | `_roleIndex` | `Dictionary<AgentRole, List<...>>` | 可变 Dictionary 字段 |
| `lib\guard\hooks\configuration\HookMatcher.cs:93` | `Groups` | `Dictionary<...>` (public) | 公开可变字典暴露 |
| `lib\guard\permission\configuration\PermissionConfig.cs:11,16,64` | 3 个 | `Dictionary<...>` (public get;set) | 配置类可变状态 |
| `kit\mcp\core\management\ToolInterventionManager.cs:11` | `_rules` | `Dictionary<string, InterventionRule>` | 可变 Dictionary 字段 |
| `lib\vault\state\store\AppStateSettingSyncService.cs:19` | `s_appStateKeyMappers` | `static Dictionary<...>` | static 可变（初始化后不写，风险低） |

**修复方案**：改 `ImmutableDictionary` + `ImmutableInterlocked.Update` CAS，或 `ConcurrentDictionary`（若高频并发写）

#### H2：同步阻塞异步操作（~40 处）

sync-over-async 死锁隐患。

| 类别 | 数量 | 代表位置 |
|------|------|----------|
| `.GetAwaiter().GetResult()` | ~35 处 | `AgentRoleProfileRegistry.cs:138`、`PsAstParser.cs:82`、`ReplService.cs:148`、`GoalEngine.cs:977`、`BridgeMainCommand.cs:189`、`Program.cs:171`、`TuiModeRunner.cs:339` |
| `.Result`（Task.Result） | ~5 处 | `SystemActuatorRegistry.cs:129`、`SettingsLoader.cs:45`、`WorkflowTask.cs:593` |

**修复方案**：全异步化改造，同步入口点改为 `async Task Main`，内部全异步

#### H3：3 个核心模块未 Actor 化

并发状态访问无保护。

| 模块 | 文件 | 当前方案 | 风险 |
|------|------|----------|------|
| `QueryEngine` | `kit\brain\query\query2\token_budget\core\QueryEngine.cs` | ServiceEntity + MiddlewarePipeline + AsyncLock | `_currentOptions`/`_pipeline` 字段并发访问无保护 |
| `AgentCoordinator` | `llm\agents\Coordinator\Core\services\AgentCoordinator.cs` | ServiceEntity + AsyncLock + ImmutableDictionary | spawn/dispose 并发控制靠 `_spawnSemaphore` |
| `McpHttpServer` | `kit\mcp\mcp_protocol\McpHttpServer.cs` | ServiceEntity + HttpListener + fire-and-forget | `_ = HandleRequestAsync` fire-and-forget，`_sessions` 注册表并发访问 |

**修复方案**：改为 `ActorBase<XxxCommand, XxxResult>` 派生，状态串行化

### 🟡 中危（封装泄漏/性能瓶颈，不直接导致崩溃）

#### M1：公开属性直接暴露可变集合（~30 处）

| 类别 | 数量 | 代表位置 |
|------|------|----------|
| 暴露可变 `Dictionary` | ~24 处 | `ThinkingStore.Entries`、`MemoryStore.TypeCounts/TagCounts`、`PermissionConfig.AutoApprovedTools` 等 |
| 暴露 `ConcurrentDictionary` | ~6 处 | `TeammateRegistry.ActiveTeammates/PendingMessages`、`MemoryScanner.ByType/ByTag/BySource` 等 |

**修复方案**：返回 `IReadOnlyDictionary<...>` 接口，隐藏可变实现；或提供查询函数

#### M2：公开属性 O(n) LINQ 重算（~15 处）

| 文件 | 属性 | 操作 | 复杂度 |
|------|------|------|--------|
| `PlanEntity.cs:63,65` | `ApprovedStepsCount`/`CompletedStepsCount` | `Steps.Count(predicate)` | O(n) |
| `PlanState.cs:66,71` | 同上 | 同上 | O(n) |
| `TodoResults.cs:24,26` | `PendingCount`/`CompletedCount` | `Todos?.Count(predicate)` | O(n) |
| `AgentExecutionRecord.cs:41,46,51` | `SuccessCount`/`FailureCount`/`AllSuccess` | `AgentResults?.Count(predicate)` | O(n) |
| `ForegroundTaskRegistry.cs:56` | `HasForegroundTasks` | `Where(...).Any()` | O(n) |
| `BridgeSessionRegistry.cs:63,72` | `GetActiveHandles()`/`GetAllWorkIds()` | `_sessions.Values.Where(...).Select(...)` | O(n) |

**修复方案**：改为 `init` 时计算 + 缓存只读字段，或 `OnPropertyChanged` 触发重算

#### M3：静态 HashSet 未冻结（~5 处）

| 文件 | 字段 |
|------|------|
| `CodeIndexExcludedDirCatalog.cs:17` | `ExcludedDirs` (HashSet) |
| `MicrocompactService.cs:38` | `CompactableTools` (HashSet) |

**修复方案**：改 `FrozenSet<string>`

#### M4：幂等无显式键控去重

当前只有 `IIdempotent` 标记接口（重试安全），无显式 `IdempotencyKey`/`BusinessFlowId` 键控去重。

**修复方案**：新增显式幂等键 + 去重表 + 幂等守卫中间件（基建层）

---

## 三、改造计划

### 阶段 0：文档（当前阶段）
- [x] 记录探索报告
- [x] 差距清单分类
- [x] 改造计划制定

### 阶段 1：补齐基建 + 压测

#### 1.1 幂等键控去重机制（基建）
- 新增 `IdempotencyKey` record（业务流水号 + 请求 ID）
- 新增 `IIdempotencyStore` 接口 + `ImmutableIdempotencyStore` 实现（ImmutableDictionary + CAS）
- 新增 `IdempotencyGuardMiddleware`（中间件管道，校验幂等键）
- 集成到 `ActorBase.AskWithRetryAsync`（重试时携带幂等键）
- ADR 记录决策

#### 1.2 基建压测
- 基准测试：幂等去重表并发读写性能
- 基准测试：CAS 更新 vs ConcurrentDictionary 性能对比
- 放入 Benchmark csproj

### 阶段 2：高危业务处理

#### 2.1 字段级可变 Dictionary → ImmutableDictionary + CAS（H1）
- 逐文件改造，每改一个编译+测试+提交
- 优先级：`ChatClient._plugins` > `EvidenceGraph._nodes/_edges` > `AgentRoleProfileRegistry._roleIndex` > `ToolInterventionManager._rules` > `PermissionConfig` > `HookMatcher.Groups`

#### 2.2 同步阻塞消除（H2）
- `.GetAwaiter().GetResult()` → 全异步化
- `.Result` → `await`
- 优先级：`SettingsLoader`（批量加载）> `SystemActuatorRegistry` > `WorkflowTask` > 其余

#### 2.3 3 核心模块 Actor 化（H3）
- `QueryEngine` → `QueryEngineActor : ActorBase<QueryCommand, QueryResult>`
- `AgentCoordinator` → `AgentCoordinatorActor`
- `McpHttpServer` → session 管理 Actor 化（请求处理仍可并行）

### 阶段 3：中危业务处理

#### 3.1 公开属性暴露可变集合 → 只读接口（M1）
#### 3.2 公开属性 O(n) LINQ → 缓存字段（M2）
#### 3.3 静态 HashSet → FrozenSet（M3）

---

## 四、架构规约（双 Tell 无 Ask）

### 通讯层基础规则
1. 底层唯一通讯原语：Tell。每个 Actor 独立 SPSC 环形邮箱，自带背压水位控制
2. 请求-应答用 Request SDK（双 Tell 协议），禁止手动拼装双 Tell
3. Request 强制入参：目标 Actor、请求报文、onSuccess 委托、onFailure 委托（两个必填）
4. Request SDK 自动处理：RequestId、会话注册表、指数退避重试、背压繁忙回执、超时、会话回收

### 两个 ID 区分
1. RequestId：会话 ID，单次重试可更换
2. 业务流水号：业务唯一标识，重试不变，用于幂等校验

### 上下文漂移
- 调用 Request 后当前消息帧立刻结束，回调在未来独立消息帧执行
- 禁止捕获当前消息栈临时变量用于回调

### 状态层规范
1. 共享状态用不可变快照 + HAMT，CAS 替换引用
2. 业务域禁止 lock/Monitor/Mutex/Semaphore（底层基础设施白名单放行）
3. 一次原子业务变更合并为单次 CAS 快照提交
4. 领域实体声明 readonly record

### 消息与守卫层
1. 所有消息携带全局业务流水号
2. Actor 内单线程串行处理，守卫层用流水号做幂等判断
3. 消息增加 TTL 跳数，检测 Actor 调用环路，环路消息丢弃

---

## 五、数据结构选型规约

```
并发安全的集合
    ├── 读多写极少 → ImmutableXXX（读零同步，写 CAS 发布新版本）
    ├── 读写都频繁 → ConcurrentXXX（原地 CAS，无 GC 压力）
    ├── 需要一致性快照 → ImmutableXXX
    ├── 高性能 SPSC → 手写 SPSC Ring Buffer
    └── 特定数据结构 → Interlocked + Volatile 手写无锁
```

- 不可变容器：写时复制，局部/全量更新
- 冻结字典：查询最快，不可插入，CAS 替换整个容器
- 推荐组合：只读类型 + BCL 不可变容器 + CAS + 版本号

### 属性和字段规范
- 禁止属性直接提供数据转换，消费者自己处理
- 禁止属性 O(1) 以外行为，O(n) 以上封装成函数
- 字典直接暴露不可变字典，不提前过滤，不预设消费者
- 提供友好查询函数（GetXXValue，O(1)/二分等）

---

## 六、进度跟踪

| 阶段 | 任务 | 状态 | 备注 |
|------|------|------|------|
| 0 | 文档 | ✅ 完成 | 本文档 |
| 1.1 | 幂等键控去重基建 | ✅ 完成 | 3文件+10测试，commit b292b832 |
| 1.2 | 幂等守卫中间件 | ✅ 完成 | IIdempotentContext<T>+中间件+5测试，commit 77aad453 |
| 1.3 | ActorBase 双Tell集成 | ✅ 完成 | IRequestCommand<TOut>+ConsumeLoop守卫+6测试，commit 0673a949 |
| 1.4 | 基建压测 | ✅ 完成 | IdempotencyStoreBench，commit 0b90a5cf |
| 2.1 | H1 可变 Dictionary | ✅ 完成 | PR #327 已合并 |
| 2.2 | H2 同步阻塞 | ✅ 完成 | PR #330，27处改async+await |
| 2.3 | H3 核心模块 Actor 化 | ✅ 完成 | PR #324 已合并 |
| 3.1 | M1 暴露可变集合 | ⏳ 待开始 | ~30处 |
| 3.2 | M2 O(n) 属性 | ⏳ 待开始 | ~15处 |
| 3.3 | M3 HashSet 冻结 | ⏳ 待开始 | ~5处 |

---

## 七、已完成工作详情

### 阶段 1：基建 + 压测（全部完成）

#### 1.1 幂等键控去重存储（commit b292b832）
- **新增文件**：
  - `lib/async_lock/idempotency/IdempotencyKey.cs` — 幂等键 readonly record struct（BusinessFlowId + OperationId）
  - `lib/async_lock/idempotency/IIdempotencyStore.cs` — 接口（TryRegister/TryGetResult/IsRegistered/Evict/EvictExpired）
  - `lib/async_lock/idempotency/IdempotencyStore.cs` — 无锁实现（ImmutableDictionary + ImmutableInterlocked.Update CAS）
- **测试**：`lib/async_lock.tests/idempotency/IdempotencyStoreTest.cs` — 10 个测试全部通过
  - 首次注册/重复注册、类型不匹配、null 结果、TTL 过期、并发安全（32 线程竞争同一 key）
- **测试目录重组**：async_lock.tests 按主题分组到 actor/mailbox/transport/host/lock/idempotency 子文件夹

#### 1.2 幂等守卫中间件（commit 77aad453）
- **新增文件**：
  - `lib/async_lock/idempotency/IIdempotentContext.cs` — 幂等上下文接口（泛型 `IIdempotentContext<T>`，用 T 代替 object）
  - `lib/infrastructure/pipeline/middlewares/IdempotencyGuardMiddleware.cs` — 守卫中间件（命中缓存短路/未命中执行并缓存）
- **设计决策**：用泛型 `TResult` 代替 `object?`（用户反馈：优先用泛型 T 代替 object）
- **测试**：`test/unit/infra.tests/pipeline/IdempotencyGuardMiddlewareTests.cs` — 5 个测试全部通过

#### 1.3 ActorBase 双Tell集成（commit 0673a949）
- **修改文件**：
  - `lib/async_lock/idempotency/IIdempotentCommand.cs` — 新增 `IRequestCommand<TOut>` 泛型接口
    - `TryRestoreFromCache(IIdempotencyStore store, Action<TOut> publish)` — 命中缓存时通过 publish 委托发布回执
    - 泛型 `<TOut>` 避免 object 装箱（用户要求：尽可能用泛型 T 代替 object）
  - `lib/async_lock/actor/ActorBase.cs` — ConsumeLoop 幂等守卫
    - 新增 `protected IIdempotencyStore? IdempotencyStore` 属性
    - ConsumeLoop 检查命令是否实现 `IRequestCommand<TOut>`，命中缓存→TryRestoreFromCache→跳过 HandleAsync
    - `AskWithRetryAsync` 恢复原签名（移除 Ask 模式幂等参数，幂等改为 Consumer 端双 Tell）
- **设计决策**：双 Tell 协议（用户纠正：不要在 Ask 模式集成幂等，改为 Consumer 端双 Tell）
- **测试**：`lib/async_lock.tests/idempotency/DoubleTellIdempotencyTest.cs` — 6 个测试全部通过
  - 首次发送执行 HandleAsync+缓存、重复发送命中缓存跳过 HandleAsync、不同键不互扰
  - 无 store 时走正常路径、普通命令不受影响、并发相同键至多执行一次

#### 1.4 基建压测（commit 0b90a5cf）
- **新增文件**：`test/benchmarks/async_lock.benchmarks/IdempotencyStoreBench.cs`
- **覆盖场景**：TryRegister 首次/重复、TryGetResult 命中/未命中、并发注册不同键
- **对比**：IdempotencyStore（ImmutableDictionary+CAS）vs ConcurrentDictionary
- **配置**：`[ShortRunJob]` 降低迭代次数，`[MemoryDiagnoser]` 内存诊断

### 基建产出物清单

| 类型 | 文件 | 说明 |
|------|------|------|
| 接口 | `lib/async_lock/idempotency/IdempotencyKey.cs` | 幂等键（业务流水号+操作标识） |
| 接口 | `lib/async_lock/idempotency/IIdempotencyStore.cs` | 去重存储接口 |
| 接口 | `lib/async_lock/idempotency/IIdempotentContext.cs` | 幂等上下文接口（泛型 T） |
| 接口 | `lib/async_lock/idempotency/IIdempotentCommand.cs` | 幂等命令接口 |
| 实现 | `lib/async_lock/idempotency/IdempotencyStore.cs` | 无锁 CAS 实现 |
| 中间件 | `lib/infrastructure/pipeline/middlewares/IdempotencyGuardMiddleware.cs` | 管道守卫中间件 |
| 集成 | `lib/async_lock/actor/ActorBase.cs` | ConsumeLoop 双Tell幂等守卫 |
| 测试 | `lib/async_lock.tests/idempotency/IdempotencyStoreTest.cs` | 10 个单元测试 |
| 测试 | `lib/async_lock.tests/idempotency/DoubleTellIdempotencyTest.cs` | 6 个双Tell集成测试 |
| 测试 | `test/unit/infra.tests/pipeline/IdempotencyGuardMiddlewareTests.cs` | 5 个中间件测试 |
| 压测 | `test/benchmarks/async_lock.benchmarks/IdempotencyStoreBench.cs` | 基准测试 |

**总测试数**：21 个（10+6+5），全部通过
**总 commit 数**：4 个（b292b832, 77aad453, 0673a949, 0b90a5cf）

---

<!-- 🤖 Auto Decision: 2026-09-27 -->
<!-- 决策: 工程已高度成熟，采用"补齐差距"而非"全面重构"策略 -->
<!-- 原因: 85%+ 已达标，Actor 模型 60+ 派生类已落地，无锁 CAS 183 处，全面重构风险大于收益 -->
<!-- 替代方案: 全面审查重构（已放弃，风险过高）-->
<!-- 验证: 三个探索子代理报告交叉验证 ✅ -->
