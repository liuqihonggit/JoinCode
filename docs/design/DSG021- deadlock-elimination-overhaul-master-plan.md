# DSG021 — 消除死锁设计全面改造总纲

> 状态: **草案(待用户确认例外清单后生效)**
> 日期: 2026-09-25
> 依据: 用户"消除死锁设计"提示词(通讯模型Actor化 / 数据结构不可变+CAS / 单数据源 / 属性字段约束)
> 审查报告: 通讯模型(60+ Actor 已建立,9处高风险) / 数据结构(MapRegistry 典范,server/code_index 遗留锁) / 属性字段(71处违规,20+良好范例)
> 单数据源方向: 由其他人审查,本总纲暂缺,后续补充

---

## 一、四大方向现状摘要

### 方向1:通讯模型(Actor 邮箱)

| 维度 | 现状 | 评价 |
|------|------|------|
| Actor 基础设施 | `lib/async_lock/actor/` 完整体系:ActorBase/IActor/SupervisedActor/RouterActor/GatewayActor,60+ Actor 实现 | ✅ 已建立 |
| 邮箱体系 | `lib/async_lock/mailbox/`:MailboxBase/PriorityMailbox/PersistentMailbox,4种通道 | ✅ 已建立 |
| Tell 模式 | `SendAsync`/`TrySend` fire-and-forget,有界通道满时背压等待 | ✅ 已建立 |
| Ask 模式 | `AskAwait` 带超时(默认10s)+死锁异常+直接环检测 | ⚠️ 不完整(无重试16次/指数退避/幂等,入参是总超时非单次超时,只检测直接环) |
| 背压机制 | `ActorBackpressure` 4档预设,有界容量+水位线 | ✅ 已建立,但24处无界通道无背压 |
| 单消费者 | ActorBase.ConsumerTask 单消费者线程,SingleReader=true | ✅ 已建立 |
| 重试16次 | MailboxBase.DrainBarrier 有16次×500ms;GatewayActor 有指数退避但默认3次 | ⚠️ 不统一 |
| 死锁检测 | LockRegistry wait-for graph + ActorCyclicAskException 直接环 | ⚠️ 只检测直接环 |

**结论**:Actor 邮箱体系已相当完整,核心缺口在 `AskAwait` 不满足用户要求(无重试16次+指数退避+幂等,入参语义不符,环检测不全),以及9处高风险同步阻塞异步点。

### 方向2:数据结构(不可变+CAS)

| 维度 | 数量 | 评价 |
|------|------|------|
| 可变集合实例化 | ~2567 处 | 基数大,多数为局部临时变量 |
| 不可变集合(ImmutableXXX) | ~331 处 | 已广泛使用 |
| Frozen 集合 | ~772 处 | 编译期确定性数据首选,已大量使用 |
| CAS/无锁(Interlocked/Volatile) | ~676 处 | MapRegistry 是典范 |
| 锁(lock/Monitor/AsyncLock/RWLockSlim) | ~410 处 | 以 AsyncLock 为主 |
| Channel | ~38 处 | Actor 邮箱已建立 |
| 二分法 | ~5 处 | 极少,ADR 0041 Fsm 范式 |
| 并发集合(ConcurrentXXX) | ~253 处 | 已大量使用 |
| 线性检索可优化 | ~10 处 | 可立即优化为 O(1) |

**典范**:`MapRegistry<TKey,TValue>`(`lib/abstractions/abs_core/core_utils/core/registry/MapRegistry.cs`)实现"ImmutableDictionary + Volatile.Read + ImmutableInterlocked.Update + Interlocked.Exchange"完整无锁 CAS 范式,是改造模板。

**主要遗留**:`server/code_index/` 3处 ReaderWriterLockSlim+mutable Dictionary(其中 InMemoryIndexStore 13个 Dictionary+递归锁),`lib/vault/` 部分嵌套锁。

### 方向3:单数据源

> 由其他人审查,本总纲暂缺,后续补充。

### 方向4:属性字段约束

| 违规类别 | 数量 | 优先级 |
|----------|------|--------|
| 属性做 LINQ 聚合(Count/Sum/Any/All) | 23 处 | P0-P1 |
| 属性做查找(FirstOrDefault) | 4 处 | P0-P1 |
| 属性做过滤/映射/物化(Where/Select/ToList/ToArray) | 14 处 | P0-P1 |
| 属性暴露 .Keys | 6 处 | P2 |
| 属性暴露 .Values | 5 处 | P2 |
| 字段直接暴露(ConcurrentDictionary/Dictionary) | 9 处 | P3 |
| 可变 List 直接暴露 | 7 处 | P3 |
| 属性有副作用 | 3 处 | P4 |
| **总计** | **71 处** | |

**良好范例**:20+ 处友好查询函数(MapRegistry/Dag/SessionRegistry/ConnectionDropdownManager/SecondaryIndex 等)。

---

## 二、风险点分级(高优先级改造目标)

### 🔴 高风险(必须优先改造)

| 编号 | 位置 | 问题 | 方向 |
|------|------|------|------|
| H1 | `lib/async_lock/actor/ActorBase.cs:246` | AskAwait 无重试16次+指数退避+幂等,入参是总超时非单次超时 | 通讯 |
| H2 | `lib/async_lock/actor/ActorBase.cs:249-251` | 等待图只检测直接环,A→B→C→A 间接环检测不到 | 通讯 |
| H3 | `lib/infrastructure/hot_spot/HotSpotTracker.cs:115` | `.Wait()` 同步阻塞异步,线程池饥饿时死锁 | 通讯 |
| H4 | `lib/async_lock/transport/NamedPipeTransport.cs:263-265` | 3处 Dispose 中 `.Wait()` 同步等待异步释放 | 通讯 |
| H5 | `lib/async_lock/transport/BusTransport.cs:252-254` | 3处 Dispose 中 `.Wait()` | 通讯 |
| H6 | `lib/async_lock/transport/MeshTransport.cs:213` | Dispose 中 `.Wait()` | 通讯 |
| H7 | `lib/vault/memdir/memdir2/services/core/ConfigPersistentServiceBase.cs:76` | `GetAwaiter().GetResult()` 同步阻塞异步 | 通讯 |
| H8 | `app/gui/view_models/main_view_model/MainViewModel.ModelConfig.cs:56` | UI 线程 `.Wait(Timeout)` 同步阻塞 | 通讯 |
| H9 | `server/code_index/indexing/InMemoryIndexStore.cs:10` | `ReaderWriterLockSlim(SupportsRecursion)` 递归锁+13个 mutable Dictionary | 数据 |
| H10 | `lib/vault/memdir/memdir2/services/core/SessionTagService.cs:35,80` | `lock(tags)`+`lock(kvp.Value)` 嵌套锁死锁风险 | 数据 |
| H11 | `lib/abstractions/abs_core/core_exceptions/ExceptionContext.cs:28` | `Data` 属性每次访问 ToImmutableDictionary 物化 O(n) | 属性 |
| H12 | `lib/plugins.contracts/session/SessionEvent.cs:143` | `Events` 属性每次访问 lock+ToArray O(n) | 属性 |
| H13 | `lib/abstractions/abs_ai/llm/chat/cache/ImmutablePrefix.cs:12` | `ToolSpecs` 属性每次访问创建迭代器 O(n) | 属性 |

### 🟡 中风险(中期改造)

| 编号 | 位置 | 问题 | 方向 |
|------|------|------|------|
| M1 | `lib/async_lock/actor/GatewayActor.cs:25` | MaxRetries 默认3次,不是16次 | 通讯 |
| M2 | 24处 `Channel.CreateUnbounded` | 无背压,OOM 风险 | 通讯 |
| M3 | `server/code_index/indexing/CodeIndexerRegistry.cs:13` | ReaderWriterLockSlim+mutable Dictionary | 数据 |
| M4 | `lib/plugins.infrastructure/services/PluginManager.cs:801,806` | `lock(_diagnostics)`+mutable List | 数据 |
| M5 | `lib/vault/memdir/memdir2/operations/core/ThinkingStore.cs:48,64,76` | `lock(entries)`+mutable List | 数据 |
| M6 | `kit/hands/api/core/UsageTracker.cs:241,295` | `lock(sessionList)`/`lock(records)` | 数据 |
| M7 | `lib/plugins.infrastructure/services/PluginManager.cs:59,62,65` | 3处 Where+Select+ToList 属性物化 | 属性 |
| M8 | `app/gui/view_models/main_view_model/MainViewModel.Messages.cs:29,55,212` | TotalChars/AllMessagesText/ExportSessionText 属性 O(n) | 属性 |
| M9 | 10处线性检索 FirstOrDefault on List | 可优化为 O(1) 字典查找 | 数据 |

### 🟢 低风险(可保留或长期优化)

- Actor 内部锁(SupervisedActor L129)— Actor 已串行化
- IOThrottleService AsyncLock — 限流语义,非状态保护
- HookConfigurationManager per-file AsyncLock — 文件 IO 锁
- Fsm 二分法 — ADR 0041 已选,数据量小

---

## 三、四大方向依赖关系

```
方向1(通讯模型 Actor化)
  │
  ├── 提供 ActorBase/Channel 基础设施
  │     ↓
  ├── 方向2(数据结构) 依赖 Actor 消除锁内长 await
  │     └── Immutable+CAS 替代 lock+mutable
  │
  ├── 方向4(属性字段) 依赖 Actor 串行化消除属性副作用
  │     └── 属性 O(n) → 查询函数/计数器维护
  │
  └── 方向3(单数据源) 独立,但改造后数据源应为 Immutable+CAS
```

**关键依赖**:
1. **方向1 必须先做** — AskAwait 升级是其他方向的基础(查询路径用 Ask,Ask 不稳定则数据结构改造也无法验证)
2. **方向2 紧随其后** — 消除锁后,属性暴露的集合才是不可变的,属性约束才有意义
3. **方向4 可与方向2 并行** — 属性改查询函数不依赖数据结构改造(但字典暴露改查询函数依赖方向2的不可变化)
4. **方向3 独立** — 可并行,但统一数据源应为 Immutable+CAS(依赖方向2的范式)

---

## 四、迁移顺序(分批次)

### 批次0:AskAwait 升级(方向1核心,阻塞项)

- [ ] H1: `ActorBase.AskAwait` 改造:入参改单次超时,内部重试16次+指数退避,总超时=单次×16+退避和
- [ ] H2: 等待图升级为全图环检测(DFS),检测间接环
- [ ] M1: `GatewayActor.MaxRetries` 与 AskAwait 重试机制对齐
- [ ] 幂等:AskAwait 加命令 ID 去重(或由命令实现 IIdempotent)

### 批次1:同步阻塞异步消除(方向1高风险)

- [ ] H3: `HotSpotTracker.cs:115` `.Wait()` → Actor 化或 await
- [ ] H4-H6: transport 3处 Dispose `.Wait()` → 评估改 await(注意 Dispose 死锁)
- [ ] H7: `ConfigPersistentServiceBase.cs:76` `GetResult()` → Actor 化
- [ ] H8: `MainViewModel.ModelConfig.cs:56` UI `.Wait()` → 异步化

### 批次2:无界通道加背压(方向1中风险)

- [ ] M2: 24处 `Channel.CreateUnbounded` 评估加有界背压
  - Agent 输出流(LLM 不可控)→ 有界+DropOldest?
  - Sandbox IPC / MCP Stdio 写通道 → 有界
  - 编译队列 → 有界

### 批次3:数据结构 Immutable+CAS(方向2高风险)

- [ ] H9: `InMemoryIndexStore` 13个 Dictionary → ImmutableDictionary+CAS(需 GC 评估,用 SetItems 批量)
- [ ] H10: `SessionTagService` 嵌套锁 → `ImmutableDictionary<string, ImmutableHashSet<string>>`+CAS
- [ ] M3: `CodeIndexerRegistry` → ImmutableDictionary+CAS
- [ ] M4: `PluginManager._diagnostics` → ImmutableList+ImmutableInterlocked.Update
- [ ] M5: `ThinkingStore` → ImmutableList+CAS
- [ ] M6: `UsageTracker` → ImmutableList+CAS

### 批次4:属性字段约束(方向4,可与批3并行)

- [ ] H11-H13: 3处热路径属性物化 → 查询函数/缓存(P0)
- [ ] M7: PluginManager 3处 LoadedXxxPluginNames → 单一查询函数
- [ ] M8: MainViewModel 3处 O(n) 属性 → 计数器维护
- [ ] 23处 Count/Sum/Any 属性 → 查询函数或计数器(P1)
- [ ] 11处 .Keys/.Values 暴露 → 查询函数(P2)
- [ ] 16处字段直接暴露 → 查询函数(P3)
- [ ] 3处属性副作用 → 方法(P4)

### 批次5:检索优化(方向2低风险)

- [ ] M9: 10处线性检索 → O(1) 字典查找

### 批次6:单数据源(方向3,待其他人报告补充)

---

## 五、例外清单(需用户批量协商)

> 以下例外/不舒服点需用户决策,决策后写入本总纲作为改造约束。

### 5.1 通讯模型例外(20项)

| 编号 | 例外 | 协商点 |
|------|------|--------|
| E1 | 查询路径(ChatContextManager/TeamManager/PlanModeManager 等20+处)必须 Ask 返回数据 | 查询路径是否统一加"重试16次+指数退避+幂等"? |
| E2 | 沙箱生命周期 Enter/Exit/Switch 必须 Ask 返回 SandboxInfo,可能 >10s | 默认10s 超时是否够?单次超时入参取多少? |
| E3 | Bridge 客户端 GetState/Start/Stop 必须 Ask 确认,StopAsync 可能 >5s | Ask 超时是否够? |
| E4 | MCP 跨进程请求必须 Ask | 跨进程超时是否需要更长(30s)?是否需要幂等去重? |
| E5 | Fork 子代理 CalculateForkDepth 等5处必须 Ask 返回 ForkResult | Fork 长时间运行,Ask 超时是否够? |
| E6 | 每 teammate 独立通道(InProcessTeammateTask:657)多消费者 | 设计如此,无法改单消费者,是否接受? |
| E7 | 每 goal 节点独立通道(GoalConflictMessenger:24)多消费者 | 设计如此,是否接受? |
| E8 | Agent 输出流 `Channel.CreateUnbounded` LLM 流式无法背压 | 接受无界?或加有界+DropOldest? |
| E9 | Sandbox IPC 写通道无界 | 加有界? |
| E10 | MCP Stdio 写通道无界 | 加有界? |
| E11 | 编译队列无界 | 加有界? |
| E12 | AskAwait 无重试16次 | 在 AskAwait 内部加?还是调用方包一层? |
| E13 | AskAwait 入参是总超时 | 改 `singleTimeoutMs` 语义为单次超时? |
| E14 | AskAwait 无幂等 | 加命令 ID 去重?还是命令本身实现幂等? |
| E15 | 等待图只检测直接环 | 升级全图环检测(DFS)? |
| E16 | GatewayActor MaxRetries 默认3次 | 统一改16次?或保留3次用于 LLM? |
| E17 | AgentServiceImpl 等10处 fire-and-forget+WaitAsync(10s) 异常被吞 | 改为 Tell+Actor 监督? |
| E18 | ConfigPersistentServiceBase `GetResult()` 同步阻塞 | Actor 化? |
| E19 | HotSpotTracker `.Wait()` 同步阻塞 | Actor 化? |
| E20 | transport 3处 Dispose `.Wait()` | 改 await?但 Dispose 不能 await(死锁)? |

### 5.2 数据结构例外(7项)

| 编号 | 例外 | 协商点 |
|------|------|--------|
| D1 | InMemoryIndexStore 数据量大(数万条),ImmutableDictionary 重建 GC 压力 | (A)去递归锁保留RWLockSlim (B)Immutable+CAS批量SetItems (C)分片热冷 |
| D2 | SessionTagService 内层 HashSet 必须可变(标签频繁增删) | (A)ImmutableHashSet+CAS (B)ConcurrentBag (C)外层Concurrent+内层Immutable CAS |
| D3 | 字符串语义 Contains(`command.Contains('$')`)无法提升 O(1) | 保留线性扫描(真正例外)? |
| D4 | PowerShell AST FindAll 是引擎调用,非 LINQ | 保留? |
| D5 | Actor 邮箱 Channel 无需改 CAS | 保留(SPSC/MPSC 设计)? |
| D6 | Fsm 二分法 vs FrozenDictionary(ADR 0041 已选二分) | 保留(数据量小)? |
| D7 | 253处 ConcurrentDictionary 读多写少,可改 Immutable 获零同步读 | 对读多写少的 ConcurrentDictionary 也改 Immutable+CAS? |

### 5.3 属性字段例外(3类)

| 编号 | 例外 | 协商点 |
|------|------|--------|
| P1 | O(1) 属性例外(路径计算/条件判断/算术/Volatile.Read/锁读取) | 这些是合理例外,全局接受? |
| P2 | UI 绑定场景(WPF/Avalonia MVVM 要求属性触发 PropertyChanged,方法无法绑定) | O(n) UI 属性用计数器维护(类似 CanRegenerate 模式)? |
| P3 | 必须暴露整个字典(Dag.Nodes/Edges, TeammateRegistry.ActiveTeammates) | 改查询函数(TryGetNode/GetAllNodes)?还是保留? |

---

## 六、改造原则(强制)

1. **主分支生长** — 所有代码围绕主分支生长,禁止发散分支
2. **例外全局化** — 例外不局部特例,看能否全局化规则
3. **渐进式迁移** — 每次移动一个功能模块,移动后立即编译验证+提交
4. **TDD 铁律** — 🔴E2E红→🔴单元红→🟢单元绿→🔵重构→🟢E2E绿
5. **复用 MapRegistry 范式** — Immutable+CAS 改造直接复用 MapRegistry/SecondaryIndex
6. **Actor 优先消除锁** — 锁内长 await 优先 Actor 化,而非换锁类型
7. **属性 O(1) 铁律** — 任何 O(1) 以外行为封装成函数,无例外(UI 绑定用计数器维护)

---

## 七、待补充

- [ ] 方向3(单数据源)审查报告 — 由其他人提供,补充后完善本总纲
- [ ] 用户对例外清单 E1-E20/D1-D7/P1-P3 的决策
- [ ] 决策后更新迁移顺序和改造约束

---

## 八、用户决策记录(2026-09-25 批量协商)

### 决策1:AskAwait 内部统一升级 ✅
- **选择**:在 `ActorBase.AskAwait` 内部统一升级
- **内容**:
  - 入参改 `singleTimeoutMs`(单次超时),废弃 `timeoutMs`(总超时)
  - 内部重试16次+指数退避(`delay = baseDelay * 2^attempt`)
  - 总超时 = `singleTimeoutMs * 16 + 退避总和`,超过抛 `ActorAskDeadlockException`
  - 等待图升级为全图环检测(DFS),检测间接环 A→B→C→A
  - 幂等:由命令实现 `IIdempotent` 接口,AskAwait 检测到幂等命令时去重
- **影响**:142处 Ask 调用自动获得重试+环检测
- **替代方案**:分层(环检测+重试中间件)— 未选,因分散风险

### 决策2:同步阻塞异步全部 Actor 化 ✅
- **选择**:9处 `.Wait()`/`.GetResult()` 全部 Actor 化
- **内容**:
  - 非 Dispose 的(HotSpotTracker/ConfigPersistent/MainViewModel)→ Actor 化或改 await
  - Dispose 内的(transport 3处)→ fire-and-forget 后台释放(`_ = DisposeAsync().AsTask()`),不阻塞 Dispose 返回
- **影响**:消除9处高风险死锁点
- **替代方案**:Dispose 保留 .Wait() — 未选,因死锁风险仍在

### 决策3:InMemoryIndexStore 去递归锁 ✅
- **选择**:去掉 `SupportsRecursion`,保留 `ReaderWriterLockSlim`(不可递归),13个 Dictionary 保持 mutable
- **内容**:
  - `new ReaderWriterLockSlim()`(默认 `LockRecursionPolicy.NoRecursion`)
  - 消除递归锁死锁根因,无 GC 风险
  - 13个 Dictionary 保持 mutable,读写锁保护
- **影响**:最小改动消除 H9 死锁根因
- **替代方案**:Immutable+CAS 批量 SetItems — 未选,因 GC 压力未验证

### 决策4:UI 绑定 O(n) 属性用计数器维护 ✅
- **选择**:O(n) UI 属性改为由集合变更事件维护的计数器字段
- **内容**:
  - 模式:属性返回 `_counter` 字段(O(1)),`OnCollectionChanged` 时重算 `_counter`
  - 范例:`TotalChars` → `_totalChars` 字段,`OnMessagesChanged` 时 `_totalChars = Messages.Sum(...)`
  - UI 绑定不变(仍绑定属性),PropertyChanged 正常触发
- **影响**:MainViewModel 等 ViewModel 的 O(n) 属性全部 O(1)
- **替代方案**:UI 场景全局例外 — 未选,因违反 O(1) 铁律

### 决策5:字典/字段暴露全局禁止 ✅
- **选择**:全局禁止属性暴露 `.Keys`/`.Values`,全局禁止字段直接暴露 Dictionary/List
- **内容**:
  - 11处 `.Keys`/`.Values` 属性 → `GetXxx()` 方法
  - 16处字段直接暴露 → `TryGet`/`GetAll` 查询函数
  - `Dag.Nodes`/`Edges` → `TryGetNode(id)`/`GetAllNodes()`/`TryGetEdge(from,to)`
  - `TeammateRegistry.ActiveTeammates`/`PendingMessages` → 查询函数(已有 `TryGetState`/`TryGetChannel`)
- **影响**:封装最严,消除外部可修改风险
- **替代方案**:保留 DictionaryView/Dag — 未选,因不一致

### 决策6:无界通道全部有界 ✅
- **选择**:24处 `Channel.CreateUnbounded` 全部改有界背压
- **内容**:
  - LLM 输出流用 `BoundedChannelFullMode.DropOldest`(丢最旧消息,流式可容忍)
  - 其余(Sandbox IPC/MCP Stdio/编译队列等)用 `BoundedChannelFullMode.Wait`(阻塞生产者)
  - 容量用 `ActorBackpressure` 预设或按场景配置
- **影响**:消除24处 OOM 风险
- **替代方案**:LLM 保留无界 — 未选,用户要求全部有界

### 决策7:fire-and-forget 改 Tell+Actor 监督 ✅
- **选择**:10处 fire-and-forget+WaitAsync(10s) 改为 Tell 模式+Actor 监督
- **内容**:
  - AgentServiceImpl 4处 / CostTracker 3处 / AnalyticsService 3处
  - 异常不吞,由监督 Actor(SupervisedActor)处理失败
  - 后台任务失败可观测、可重试
- **影响**:消除10处异常吞没,后台任务可监督
- **替代方案**:保留但异常不吞 — 未选,因 Tell+监督更彻底

### 决策8:ConcurrentDictionary 全部改 Immutable+CAS ✅
- **选择**:253处 ConcurrentDictionary 全部改 ImmutableDictionary+CAS
- **内容**:
  - 用 `MapRegistry` 范式:`volatile ImmutableDictionary` + `Volatile.Read` + `ImmutableInterlocked.Update`
  - 读零同步(无锁),写 CAS 发布新版本
  - 读多写少场景获益最大;读写都频繁的也改(用户要求统一)
- **影响**:253处集合统一为 Immutable+CAS 范式,零同步读
- **替代方案**:只改高频读少写 — 未选,用户要求全部统一
- **⚠️ 风险**:GC 压力增加(每次写重建字典),需分批次渐进式迁移+性能基准验证

### 接受的例外(无需改造)

- **E6-E7 多消费者**:teammate/goal 节点独立通道是设计如此,接受
- **D3 字符串语义 Contains**:`command.Contains('$')` 等语义检测,无法 O(1),接受
- **D4 PowerShell AST FindAll**:引擎调用非 LINQ,接受
- **D5 Actor 邮箱 Channel**:SPSC/MPSC 设计,无需改 CAS,接受
- **D6 Fsm 二分法**:ADR 0041 已选,数据量小,接受
- **P1 O(1) 属性例外**:路径计算/条件判断/算术/Volatile.Read/锁读取是合理 O(1),接受

---

## 九、最终改造范围汇总(8决策落地后)

| 批次 | 内容 | 影响点数 | 风险 |
|------|------|----------|------|
| 批0 | AskAwait 内部升级(重试16次+指数退避+全图DFS+幂等+单次超时入参) | 142处 Ask | 高(核心通讯) |
| 批1 | 同步阻塞异步全部 Actor 化(9处 .Wait()/.GetResult()) | 9处 | 高(死锁消除) |
| 批2 | 无界通道全部有界背压(24处) | 24处 | 中(OOM 消除) |
| 批3 | fire-and-forget 改 Tell+Actor 监督(10处) | 10处 | 中(异常可观测) |
| 批4 | ConcurrentDictionary 全部改 Immutable+CAS(253处) | 253处 | 高(GC 压力,需基准验证) |
| 批5 | InMemoryIndexStore 去递归锁+server/code_index 遗留锁 | 3处 | 低(最小改动) |
| 批6 | 属性字段约束(71处违规:聚合/查找/物化/字典暴露/字段暴露/副作用) | 71处 | 中(封装) |
| 批7 | 检索优化(10处线性→O(1)) | 10处 | 低 |
| **合计** | | **~520处** | |

> ⚠️ 批4(253处 ConcurrentDictionary)工作量最大,建议分模块子批次:lib → kit → server → gen,每模块编译验证后提交
