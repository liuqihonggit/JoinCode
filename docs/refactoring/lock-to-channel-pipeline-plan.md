# 锁 → 管道通讯重构方案

> **状态**:accepted(P0 完全 Actor 化,P1/P2 最小修复消除死锁点) | proposed(P3-P8 第二轮分析,待执行)
> **日期**:2026-09-05
> **实现日期**:2026-09-05(P0/P1/P2) | 2026-09-05(P3-P8 方案产出)
> **范围**:StreamingToolExecutor / ForkSubAgentManager / GoalGraphEngine(P0-P2 已完成) | McpStdioClient / McpClientToolHandlers / McpAuthToolHandlers / McpTransportFallbackChain / McpServerStateManager / ToolInterventionManager(P3-P8 待执行)
> **目标**:将高风险状态锁改为 Actor+Channel 管道通讯,消除死锁与持锁等外部 IO 风险
> **验证**:1740 个单元测试通过(AsyncLock 10 + Brain.Context 785 + Agents 513 + Clock 432)

---

## 一、背景与动机

### 1.1 问题

项目中 `AsyncLock`(SemaphoreSlim 封装)广泛使用(582 处匹配)。其中三类用法:
1. **(N,N) 并发限流** — 资源池语义,合理保留
2. **(1,1) 短临界区互斥** — 保护内存字段,多数低风险
3. **(1,1) 锁内 await 外部调用** — **高风险**,持锁等 IO/回调,易死锁与争用

本方案针对第 3 类,将"有状态对象 + 锁"重构为"Actor + Channel 管道通讯",单消费者串行处理,消除锁。

### 1.2 已有管道典范(参考实现)

| 文件 | 模式 |
|------|------|
| `LoopDiagnosticJournal.cs:27` | `BoundedChannel(256)` + `IJournalCommand` 接口 + record 命令 + 单 Consumer Task |
| `InProcessMailbox.cs:8` | `ConcurrentDictionary<agentId, Channel<Message>>` 无锁消息投递 |
| `AgentOutputChannelManager.cs:11` | 单汇聚 `Channel<AgentOutputChunk>` + `ReadAllAsync` |
| `GoalConflictMessenger.cs:10` | 按 nodeId 分 Channel 解耦冲突通知 |

### 1.3 不重构的锁(合理用法,保留)

| 位置 | 理由 |
|------|------|
| `AgentCoordinator._spawnSemaphore` | (N,N) 纯限流,Interlocked.Exchange 热重载已处理 Dispose |
| `IOThrottleService` 三 Semaphore | 分类型限流 + TokenBucket,限流器标准模式 |
| `DownloadSession` 局部 SemaphoreSlim | 限流分片下载,用完即弃 |
| `ExecutionContext.ConcurrencyLock` | (N,N) 任务并发限流 |

---

## 二、总体设计原则

| 原则 | 说明 |
|------|------|
| **Actor 模型** | 有状态对象变为单消费者 Channel,操作转为命令消息,串行处理,无锁 |
| **命令模式** | `IXxxCommand` 接口 + record 命令类型,参考 `LoopDiagnosticJournal.IJournalCommand` |
| **双 Channel** | 输入 Channel(命令) + 输出 Channel(结果),或 `TaskCompletionSource<T>` 回调 |
| **背压控制** | `BoundedChannel` + `DropOldest`/`Wait` 策略 |
| **保留限流器** | (N,N) 并发限流 `AsyncLock` 保留(资源池,管道化无收益) |
| **渐进式** | 新类并行运行 → 特性开关切流量 → 验证稳定 → 删旧锁 |

---

## 三、P0:`StreamingToolExecutor` → Actor+Channel(最高优先级)

### 3.1 现状问题

**文件**:`core/execution/Brain/src/Context/Services/Chat/StreamingToolExecutor.cs`

7 处 `TryLock` 保护 `_queue`/`_completedBuffer`/`_executingCount`/`_nonSafeExecutingCount`:

```
AddToolAsync ──┐
GetCompletedResultsAsync ──┤── TryLock(_semaphore) ─── 保护四个共享字段
GetRemainingResultsAsync ──┤
Discard ──┤
ProcessQueueAsync ──┤   ← 锁内 await IsConcurrencySafeAsync(外部IO) + RunFireAndForget(再抢锁)
ExecuteToolAsync ──┘
```

**死锁/争用根因**:
- 锁内 `await FindNextExecutableAsync` → `IsConcurrencySafeAsync`(外部 IO/LLM,耗时不定)→ 持锁等外部 = 阻塞所有 AddTool/GetCompleted/Discard
- `ProcessQueueAsync` 锁内 `RunFireAndForget(ProcessQueueAsync)` 再次抢同一把锁 → 自等自(已用 Task.Run 缓解,ADR 0060,治标不治本)

### 3.2 Channel 拓扑设计

```
                    ┌─────────────────────────────────────┐
 AddToolAsync ────► │  _commandCh: Channel<IToolCommand>  │ ──► 单 Consumer Loop ──► ExecuteToolAsync (并发,无锁)
 GetCompleted ────► │  (Unbounded, SingleReader=true)     │
 Discard ────►      └─────────────────────────────────────┘
                              │
                              ▼ 回调结果
                    ┌─────────────────────────────────────┐
                    │  _resultCh: Channel<StreamingToolResult> │ ──► GetCompletedResultsAsync 读
                    │  (Unbounded, SingleReader=true)     │
                    └─────────────────────────────────────┘
```

Consumer Loop 是唯一访问 `_queue`/`_executingCount`/`_nonSafeExecutingCount` 的线程 → **零锁**。
`ExecuteToolAsync` 仍并发执行(工具本身无共享状态),完成后向 `_commandCh` 投递 `ToolCompletedCommand`。

### 3.3 核心类型签名

```csharp
// 命令接口 — 对齐 LoopDiagnosticJournal.IJournalCommand
private interface IToolCommand;

private sealed record AddToolCommand(ToolCallEntry Entry, int OriginalIndex) : IToolCommand;
private sealed record DiscardCommand() : IToolCommand;
private sealed record ToolCompletedCommand(StreamingToolResult Result, bool IsConcurrencySafe) : IToolCommand;
private sealed record GetCompletedQueryCommand(TaskCompletionSource<IReadOnlyList<StreamingToolResult>> Tcs) : IToolCommand;
private sealed record GetRemainingQueryCommand(TaskCompletionSource<IReadOnlyList<StreamingToolResult>> Tcs) : IToolCommand;

// 新版执行器字段
private readonly Channel<IToolCommand> _commandCh = Channel.CreateUnbounded<IToolCommand>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
private readonly Channel<StreamingToolResult> _resultCh = Channel.CreateUnbounded<StreamingToolResult>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
private readonly Task _consumerTask;
// _queue/_executingCount/_nonSafeExecutingCount 仅 Consumer 访问,无需锁
```

### 3.4 关键改动对照

| 旧 | 新 |
|----|----|
| `AddToolAsync` 内 `TryLock` + `_queue.Add` + `RunFireAndForget(ProcessQueueAsync)` | `_commandCh.Writer.TryWrite(new AddToolCommand(...))` + 触发调度 |
| `ProcessQueueAsync` while 循环抢锁 | Consumer Loop `await _commandCh.Reader.ReadAllAsync` 串行处理 |
| `ExecuteToolAsync` 完成后 `TryLock` 更新计数 | 完成后 `_commandCh.Writer.TryWrite(new ToolCompletedCommand(...))` 回报 |
| `GetCompletedResultsAsync` 抢锁读 buffer | 发 `GetCompletedQueryCommand(TCS)` → Consumer 从 `_resultCh` 批量读 → Tcs.SetResult |
| `Discard` 抢锁 | `_commandCh.Writer.TryWrite(new DiscardCommand())` |
| 7 处 `TryLock` | **0 处锁**(全部消除) |

### 3.5 渐进式迁移步骤

1. **新建** `StreamingToolExecutorActor.cs`(同目录),实现上述 Channel 拓扑,不动旧类
2. **单元测试**:对 Actor 版写与旧版相同的行为测试(AddTool→执行→GetCompleted 按序返回),红→绿
3. **特性开关**:在 `ChatMiddlewareContext` 加 `UseActorToolExecutor` 配置开关,默认 false
4. **切换**:开关 true 时用 Actor 版,false 时用旧版 → 灰度验证
5. **E2E**:跑流式工具执行 E2E(多工具并发 + 非并发安全工具 + Discard 场景)
6. **删除旧版**:验证稳定后移除旧 `StreamingToolExecutor`(移到 `.xxx/`),开关默认 true
7. **每步**:编译 → 单元测试 → commit

### 3.6 风险与回滚

- **风险**:Consumer Loop 异常退出 → 命令堆积无响应。**对策**:Consumer 包 try-catch,异常写 `_resultCh` 错误事件 + 日志,不退出循环
- **风险**:`GetCompletedQueryCommand` 用 TCS 同步等待 → 若 Consumer 阻塞则调用方挂。**对策**:TCS 加超时(`Task.WaitAsync(5s)`),超时返回空列表
- **回滚**:特性开关 false 即回旧版,零成本

---

## 四、P1:`ForkSubAgentManager` → Actor 消息驱动

### 4.1 现状问题

**文件**:`core/ai/Agents/src/Coordinator/Fork/ForkSubAgentManager.cs`

- `_lock` 保护 `_entries`+`_sharedCache`,但两者已 `ConcurrentDictionary` → **双重保护冗余**(违反分析器规则)
- `CancelForkAsync:322` 锁内 `await LifecycleManager.CancelAgentAsync` + `FireForkCompletedAsync`(触发外部 event)→ **持锁回调重入风险**
- `MergeForkAsync:265` 锁内遍历 + 修改 cache → 持锁做重活

### 4.2 Channel 拓扑

```
ForkAsync ──►┐
CancelForkAsync ──►┐
MergeForkAsync ──►┐── _commandCh: Channel<IForkCommand> ──► 单 Consumer ──► 串行处理
GetActiveForksAsync ──►┘                                          (无 _lock,仅 _forkSemaphore 限流保留)
UpdateConcurrencyOptions ──►┘
```

**`_forkSemaphore` 保留**:(N,N) 并发限流器,不是状态锁。`ForkAsync` 入队前先抢信号量(限流),入队后释放(转移给后台任务)

### 4.3 核心命令类型

```csharp
private interface IForkCommand;

private sealed record ForkCommand(ForkOptions Options, string ForkId, CancellationToken Ct, TaskCompletionSource<ForkResult> Tcs) : IForkCommand;
private sealed record CancelForkCommand(string ForkId, CancellationToken Ct, TaskCompletionSource Tcs) : IForkCommand;
private sealed record MergeForkCommand(string ForkId, CancellationToken Ct, TaskCompletionSource<ForkResult> Tcs) : IForkCommand;
private sealed record GetActiveForksQuery(TaskCompletionSource<IReadOnlyList<ForkSubAgent>> Tcs) : IForkCommand;
private sealed record ForkBackgroundCompletedCommand(string ForkId, bool Success, string? Output) : IForkCommand;
```

### 4.4 关键改动

| 旧 | 新 |
|----|----|
| 5 处 `_lock.TryLock` | 0 处(Consumer 单线程访问 `_entries`/`_sharedCache`) |
| `ConcurrentDictionary` + `_lock` 双重 | 普通 `Dictionary` + Consumer 单访问(或保留 ConcurrentDictionary 不加锁) |
| 锁内 `FireForkCompletedAsync`(event) | Consumer 处理完命令后,锁外触发 event |
| 锁内 `await CancelAgentAsync` | Consumer 内分发(方案B,见下) |

**⚠️ 关键决策 — Consumer 是否串行 await 慢操作?**
- **方案A(纯串行)**:简单,但 Cancel 慢阻塞 Merge → 不可行
- **方案B(并发分发)**:Consumer 只做状态读写(快),慢操作(CancelAgent/Execute)分发到 `Task.Run` → 状态一致性靠命令入队序保证。**推荐方案B**

### 4.5 迁移步骤

1. 新建 `ForkSubAgentManagerActor.cs`,实现命令 Channel + Consumer(方案B)
2. 单元测试:Fork→Cancel→Merge→GetActive 全路径
3. 特性开关 `UseActorForkManager`
4. E2E:多 Fork 并发 + 后台 Fork 完成 + Cancel 级联
5. 删除旧 `_lock` 版本

---

## 五、P2:`GoalGraphEngine` → 事件驱动 Channel

### 5.1 现状问题

**文件**:`composition/Clock/src/Goal/Core/GoalGraphEngine.cs`

- `StateLock` 保护 `goalState.Status`(2 行赋值,锁本身低风险)
- **真实问题**:`DrainReadyBatch:149` 对 `ReadyQueue`/`CompletedNodes` 复合判断 `AreAllUpstreamsCompleted` 非原子 → 并发竞态(非死锁,但状态不一致)
- `Task.Delay(50)` 轮询空队列(第 120 行)→ 浪费 + 延迟

### 5.2 Channel 拓扑

```
节点完成 ──► _nodeCompletedCh: Channel<NodeCompletedEvent> ──► 调度器 Consumer ──► 推进后继节点
                                                                          │
                                                                          ▼
                                                              _readyCh: Channel<string> ──► 并发执行(限流器)
```

**设计**:
- `_readyCh`:就绪节点 Channel,多个 Consumer 并发执行(受 `concurrencyLimiter` 限流)
- `_nodeCompletedCh`:节点完成事件 Channel,单 Consumer 调度:检查后继节点上游是否全完成 → 入 `_readyCh`
- 消除 `Task.Delay(50)` 轮询:节点完成即触发调度,事件驱动

### 5.3 核心类型

```csharp
private readonly Channel<string> _readyCh = Channel.CreateUnbounded<string>(
    new UnboundedChannelOptions { SingleReader = false });
private readonly Channel<NodeCompletedEvent> _completedCh = Channel.CreateUnbounded<NodeCompletedEvent>(
    new UnboundedChannelOptions { SingleReader = true });

private sealed record NodeCompletedEvent(string NodeId, GoalNodeStatus Status, string[] Routes);
```

### 5.4 关键改动

| 旧 | 新 |
|----|----|
| `while(true)` + `DrainReadyBatch` + `Task.Delay(50)` | 事件驱动:Consumer 从 `_completedCh` 读,推进后继 |
| `StateLock` 保护 `goalState.Status` | `goalState` 改为不可变快照 + `Interlocked.Exchange` 更新(或保留锁,临界区极短) |
| `ConcurrentQueue ReadyQueue` + 手动入队 | `_readyCh` Channel,Writer.TryWrite |
| `Task.WhenAll(batch)` 同步等所有完成 | 各节点独立执行,完成即投递 `_completedCh` |

### 5.5 迁移步骤

1. 新建 `GoalGraphEngineEventDriven.cs`(partial 或新类)
2. 单元测试:线性图 + 菱形并行 + 失败回退 + 循环终止
3. 特性开关
4. E2E:复杂目标图执行
5. 删除旧轮询版

---

## 六、公共基础设施(三个目标共用)

### 6.1 Actor 基类(减少重复)

```csharp
/// <summary>Actor 基类 — 单消费者 Channel + 命令处理</summary>
public abstract class ActorBase<TCommand> : IAsyncDisposable
{
    private readonly Channel<TCommand> _channel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();

    protected ActorBase(int? boundedCapacity = null)
    {
        _channel = boundedCapacity is null
            ? Channel.CreateUnbounded<TCommand>(new UnboundedChannelOptions { SingleReader = true })
            : Channel.CreateBounded<TCommand>(boundedCapacity.Value);
        _consumerTask = Task.Run(ConsumeLoopAsync);
    }

    protected ValueTask SendAsync(TCommand cmd, CancellationToken ct) => _channel.Writer.WriteAsync(cmd, ct);
    protected bool TrySend(TCommand cmd) => _channel.Writer.TryWrite(cmd);
    protected abstract ValueTask HandleAsync(TCommand command, CancellationToken ct);

    private async Task ConsumeLoopAsync()
    {
        await foreach (var cmd in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            try { await HandleAsync(cmd, _cts.Token).ConfigureAwait(false); }
            catch (Exception ex) { OnConsumerError(ex); }
        }
    }
    partial void OnConsumerError(Exception ex);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        await _consumerTask.ConfigureAwait(false);
        _cts.Dispose();
    }
}
```

### 6.2 涉及文件目录树

```
core/execution/Brain/src/Context/Services/Chat/
├── StreamingToolExecutor.cs          (旧,最终移到 .xxx/)
├── StreamingToolExecutorActor.cs     (新,P0)
└── ...
core/ai/Agents/src/Coordinator/Fork/
├── ForkSubAgentManager.cs            (旧,最终移到 .xxx/)
├── ForkSubAgentManagerActor.cs       (新,P1)
└── ...
composition/Clock/src/Goal/Core/
├── GoalGraphEngine.cs                (旧,最终移到 .xxx/)
├── GoalGraphEngineEventDriven.cs     (新,P2)
└── ...
foundation/AsyncLock/src/
└── ActorBase.cs                      (新,公共基类,可选)
```

---

## 七、优先级与依赖

```
P0 (StreamingToolExecutor) ── 独立,可先做
P1 (ForkSubAgentManager)   ── 独立,可与 P0 并行
P2 (GoalGraphEngine)       ── 依赖 GoalConflictMessenger(已 Channel 化),可并行
公共 ActorBase             ── 三个都可用,先建
```

**推荐顺序**:先建 `ActorBase` → P0 → P1 → P2,每步编译+测试+commit。

---

## 八、验证策略

| 层级 | 方法 |
|------|------|
| 单元 | 对每个 Actor 版重写旧版所有测试用例(行为等价) |
| 并发 | 1000 次 AddTool/Fork 并发压测,断言无死锁(全局超时 10s) |
| E2E | 现有流式/Fork/GoalGraph E2E 全量跑通 |
| 诊断 | `LockRegistry.DumpAll()` 在新版本下应显示 0 个状态锁实例(P0/P1),仅剩限流器 |

---

## 九、风险总结

| 风险 | 影响 | 对策 |
|------|------|------|
| Consumer Loop 异常退出 | 命令堆积无响应 | try-catch 不退出循环,异常转事件 |
| TCS 查询阻塞 | 调用方挂起 | `Task.WaitAsync(5s)` 超时兜底 |
| 命令乱序 | 状态不一致 | 单 Consumer 保证 FIFO;慢操作用方案B 分发但状态更新回 Consumer |
| Channel 未 Complete | 资源泄漏 | DisposeAsync 中 Writer.Complete() + await consumerTask |
| 热重载信号量 Dispose | ObjectDisposedException | 保留现有 try-catch ObjectDisposedException 模式 |

---

<!-- 🤖 Auto Decision: 2026-09-05 -->
<!-- 决策: 产出锁→管道重构方案文档,不落代码 -->
<!-- 原因: 用户要求先考察高风险点并出方案,验证方向后再动手 -->
<!-- 替代方案: 直接开始 P0 重构(用户选择暂缓) -->
<!-- 验证: 文档已归档至 docs/refactoring/lock-to-channel-pipeline-plan.md ✅ -->

---

## 十、第二轮分析:MCP 层持锁 await IO 风险点(P3-P8)

> **产出日期**:2026-09-05
> **状态**:proposed(方案已定,待执行)
> **动机**:P0-P2 完成后,全量扫描剩余 669 处 TryLock/TryLockAsync,识别出 6 个新的"持锁 await 外部 IO"高风险点,集中在 MCP 客户端/服务端层

### 10.1 扫描结果分类

| 类别 | 数量 | 处理 |
|------|------|------|
| (N,N) 并发限流 | 3 处 | 保留(资源池语义) |
| (1,1) 锁内纯内存快操作 | 多处 | 保留(低风险,无 await IO) |
| **(1,1) 锁内 await 外部 IO** | **6 处** | **本次重构目标(P3-P8)** |
| lock 语句保护 List/Dict(同步路径) | 多处 | 保留(per-key lock 合理模式) |

### 10.2 低风险保留清单(不重构)

| 位置 | 理由 |
|------|------|
| `ExecutionContext._runningTasksLock` | 锁内 Add/ToList/RemoveAll/Count,纯内存快操作 |
| `WorkflowToolHandlers._historyLock` | 锁内 Add/Clear/new List,纯内存快操作 |
| `McpNetworkClient._requestLock` | 锁内只做字典 Put/Remove,SendMessageAsync 在锁外 |
| `CostTracker` per-key lock | lock(existing){Add} + lock(records){new List},纯内存 |
| `UsageTracker` per-key lock | lock(sessionList){Add},纯内存 |
| `AsyncLazy._gate` | lazy 初始化,单次 |
| `IOThrottleService` | (N,N) 限流器 |
| `AgentCoordinator._spawnSemaphore` | (N,N) 限流器 |
| `SessionTagService`/`ThinkingStore`/`ErrorConsole` | lock 纯内存 |
| `TextDocument`/`HighlightingManager`/`RopeNode` | Editor 库 UI 线程 |

---

## 十一、P3:`McpStdioClient` → Channel 串行写 stdin(高优先级)

### 11.1 现状问题

**文件**:`services/Mcp/src/Client/Transport/McpStdioClient.cs`

4 处 `TryLockAsync` 保护 `_pendingRequests` 字典和 `_stdinWriter`:

```
SendRequestAsync:
  锁1(254行): _pendingRequests[requestId] = tcs     ← 快操作,锁即释放
  锁2(269行): await _stdinWriter.WriteLineAsync(json) ← 持锁 await 写管道!
  锁3(290行): _pendingRequests.Remove(requestId)     ← catch 块,快操作
SendNotificationAsync:
  锁4(318行): await _stdinWriter.WriteLineAsync(json) ← 持锁 await 写管道!
```

**死锁场景**:
- stdin 管道缓冲区满(子进程不读/读得慢)→ `WriteLineAsync` 阻塞 → 持 `_requestLock` → 所有 SendRequest/SendNotification 排队等锁 → 整个 MCP 客户端死锁
- 即使缓冲区不满,持锁 await IO 也人为串行化了所有请求,丧失并发能力

### 11.2 Channel 拓扑设计

```
SendRequestAsync ──┐
                    ├──→ Channel<WriteCommand> ──→ 单 Consumer Task ──→ _stdinWriter.WriteLineAsync
SendNotificationAsync ─┘
```

**命令类型**:
```csharp
private interface IWriteCommand { }
private record WriteRequestCommand(int RequestId, TaskCompletionSource<JsonRpcResponse> Tcs, string Json) : IWriteCommand;
private record WriteNotificationCommand(string Json) : IWriteCommand;
private record RemoveRequestCommand(int RequestId) : IWriteCommand;
```

**关键设计**:
- `Channel.CreateUnbounded<IWriteCommand>()`(请求量不会爆炸,有 RequestTimeout 兜底)
- 单 Consumer Task:从 Channel 读命令,串行调 `_stdinWriter.WriteLineAsync`,无锁
- `SendRequestAsync`:`_pendingRequests[requestId] = tcs`(ConcurrentDictionary 无锁)→ 投递 WriteRequestCommand 到 Channel → 等 tcs.Task
- **消除所有 4 处 TryLockAsync**

### 11.3 迁移策略

1. 新建 `McpStdioClientChannel.cs`,实现同一基类 `McpClientBase`
2. 特性开关 `JCC_ACTOR_MCP_STDIO=1` 切换
3. 复用现有 `ReadLoopAsync`(读端不变,只改写端)
4. 单元测试:复现管道满场景(用慢读 Mock IInteractiveProcess),验证不死锁

---

## 十二、P4:`McpClientToolHandlers` — 持锁 await 多个外部 IO(高优先级)

### 12.1 现状问题

**文件**:`services/Mcp/src/Core/Handlers/McpClientToolHandlers.cs:51-190`

持 `_clientLock` 期间:
- `_clients.ContainsKey` 检查(快)
- `await _deps.OAuthService.StartAuthorizationFlowAsync`(网络 IO)
- `await _deps.OAuthService.GetAccessTokenAsync`(网络 IO)
- 创建 client + `await client.ConnectAsync`(网络连接)
- `_clients[connection_name] = client`(注册)

**死锁场景**:OAuth 流程慢/网络连接慢 → 长时间持锁 → 阻塞所有 McpClientConnect/McpClientDisconnect/McpClientList 操作

### 12.2 重构方案:锁内检查 + 锁外连接 + 锁内注册

```
原: lock { 检查 → OAuth → 连接 → 注册 }
新: lock { 检查 } → OAuth(锁外) → 连接(锁外) → lock { 二次检查 + 注册 }
```

**关键点**:
- 锁内只做 `_clients.ContainsKey` 存在性检查(快操作)
- OAuth 流程 + client.ConnectAsync 全部移到锁外
- 连接成功后再次 `lock { if (_clients.ContainsKey) return Error; _clients[name] = client; }`(二次检查防并发重复注册)
- **消除持锁 await 网络 IO**

### 12.3 风险与对策

| 风险 | 对策 |
|------|------|
| 两个并发 Connect 同名 client | 二次检查拦截,后到者返回"已存在"错误 |
| 连接成功但注册前 Dispose | try-catch ObjectDisposedException |
| 连接资源泄漏(注册失败) | using var client + 连接失败时 await client.DisposeAsync |

---

## 十三、P5:`McpAuthToolHandlers` — 持锁 await 网络 IO(中高优先级)

### 13.1 现状问题

**文件**:`services/Mcp/src/Core/Handlers/McpAuthToolHandlers.cs:207-219`

`McpAuthRefreshAsync` 持 `_authLock` 期间 `await provider.RefreshAsync(cancellationToken)`(HTTP 请求 token endpoint)。

**死锁场景**:token endpoint 慢 → 持锁 → 阻塞所有 ConfigureOAuth2/McpAuthRefresh 操作

### 13.2 重构方案:锁内取引用 + 锁外刷新

```csharp
// 原:
using var guard = await _authLock.TryLockAsync(ct);
var provider = _authProviders[name];
await provider.RefreshAsync(ct);  // 持锁 await 网络 IO

// 新:
OAuth2AuthProvider? provider;
using (var guard = await _authLock.TryLockAsync(ct))
    provider = _authProviders.GetValueOrDefault(name);
if (provider is null) return Error("not found");
await provider.RefreshAsync(ct);  // 锁外 await 网络 IO
```

**同理处理 `ConfigureOAuth2Async`**:锁内只做 `_authProviders[name] = provider`,PersistAuthConfigAsync 移到锁外。

---

## 十四、P6:`McpTransportFallbackChain` — 持锁 await 传输启停 IO(中高优先级)

### 14.1 现状问题

**文件**:`services/Mcp/src/Transports/Shared/McpTransportFallbackChain.cs:172-199`

`OnActiveTransportErrorAsync` 持 `_switchLock` 期间:
- `await _activeTransport.StopAsync`
- `await _transports[nextIndex].StartAsync`

**死锁场景**:传输启停慢(网络握手)→ 持锁 → 阻塞其他 fallback 切换

### 14.2 重构方案:锁内决策 + 锁外启停

```
原: lock { 选 nextIndex → StopAsync → StartAsync → 切换 _activeTransport }
新: lock { 选 nextIndex + 标记 switching } → StopAsync(锁外) → StartAsync(锁外) → lock { _activeTransport = next }
```

**关键点**:
- 锁内只做索引选择和 `_switching = true` 标记(防重入)
- 启停移到锁外
- 启停完成后 `lock { _activeTransport = next; _switching = false; }`

---

## 十五、P7:`McpServerStateManager` — 持锁 await 文件写(中优先级)

### 15.1 现状问题

**文件**:`services/Mcp/src/Core/Management/McpServerStateManager.cs:52-78`

`DisableAsync`/`EnableAsync` 持 `_lock` 期间 `await PersistAsync`(文件写)。

### 15.2 重构方案:锁内改内存 + 锁外持久化

```csharp
// 原:
using var guard = await _lock.TryLockAsync(ct);
_disabledServers.Add(name);
await PersistAsync(ct);  // 持锁 await 文件写

// 新:
bool changed;
using (var guard = await _lock.TryLockAsync(ct))
    changed = _disabledServers.Add(name);
if (changed) await PersistAsync(ct);  // 锁外 await 文件写
```

**风险**:两个并发 Disable,PersistAsync 串行写可能后写覆盖前写。**对策**:PersistAsync 内部用 `FileShare.ReadWrite` + 原子写(临时文件 + Move),或用 `AsyncLock` 专门的 `_persistLock` 串行化写操作(不阻塞读操作)。

---

## 十六、P8:`ToolInterventionManager` — 持锁同步文件写(中优先级)

### 16.1 现状问题

**文件**:`services/Mcp/src/Core/Management/ToolInterventionManager.cs:29-51`

`AddRuleAsync`/`RemoveRuleAsync` 持 `_lock` 期间 `SaveToDisk()`(同步文件写,阻塞线程池线程)。

### 16.2 重构方案:锁内改内存 + 锁外异步持久化

```csharp
// 原:
using var guard = await _lock.TryLockAsync(ct);
_rules[name] = rule;
SaveToDisk();  // 持锁 同步文件写

// 新:
using (var guard = await _lock.TryLockAsync(ct))
    _rules[name] = rule;
await SaveToDiskAsync(ct);  // 锁外 异步文件写
```

**额外改进**:`SaveToDisk` 同步 → `SaveToDiskAsync` 异步(用 `_fs.WriteAllTextAsync`),消除线程池阻塞。

---

## 十七、P3-P8 优先级与依赖

```
P3 (McpStdioClient)           ── 独立,Channel 管道化,最高收益
P4 (McpClientToolHandlers)    ── 独立,锁外连接,高收益
P5 (McpAuthToolHandlers)      ── 独立,锁外刷新,中收益
P6 (McpTransportFallbackChain)── 独立,锁外启停,中收益
P7 (McpServerStateManager)    ── 独立,锁外持久化,低收益
P8 (ToolInterventionManager)  ── 独立,锁外持久化,低收益
```

**推荐顺序**:P3 → P4 → P5 → P6 → P7 → P8,每步编译+测试+commit。

P3 收益最高(消除死锁根因),P4 次之(消除长时间持锁),P5-P6 中等,P7-P8 最低(只是持锁 IO 非死锁)。

---

## 十八、P3-P8 验证策略

| 层级 | 方法 |
|------|------|
| 单元 | 每个 P 复现"持锁 await IO 慢"场景(Mock 慢 IO),验证重构后不阻塞其他操作 |
| 并发 | 100 并发请求 + 1 慢 IO,断言其他 99 请求在 5s 内完成(旧版会超时) |
| E2E | 现有 MCP E2E 全量跑通(连接/断开/工具调用/认证) |
| 诊断 | `LockRegistry.DumpAll()` 确认 P3-P8 的状态锁实例数下降 |

---

## 十九、P3-P8 涉及文件目录树

```
services/Mcp/src/Client/Transport/
├── McpStdioClient.cs              (P3,加 Channel 写端)
└── McpStdioClientChannel.cs       (P3,新建,可选独立类)
services/Mcp/src/Core/Handlers/
├── McpClientToolHandlers.cs       (P4,锁外连接)
└── McpAuthToolHandlers.cs         (P5,锁外刷新)
services/Mcp/src/Transports/Shared/
└── McpTransportFallbackChain.cs   (P6,锁外启停)
services/Mcp/src/Core/Management/
├── McpServerStateManager.cs       (P7,锁外持久化)
└── ToolInterventionManager.cs     (P8,锁外持久化)
```

---

<!-- 🤖 Auto Decision: 2026-09-05 -->
<!-- 决策: 第二轮扫描产出 P3-P8 方案,追加到本文档,不落代码 -->
<!-- 原因: 用户选择"只出方案文档不改代码",P3-P8 方案归档待后续执行 -->
<!-- 替代方案: 直接开始 P3 重构(用户选择暂缓) -->
<!-- 验证: P3-P8 方案已归档至本文档第十至十九章 ✅ -->
