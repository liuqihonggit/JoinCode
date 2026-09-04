# 锁 → 管道通讯重构方案

> **状态**:proposed
> **日期**:2026-09-05
> **范围**:StreamingToolExecutor / ForkSubAgentManager / GoalGraphEngine
> **目标**:将高风险状态锁改为 Actor+Channel 管道通讯,消除死锁与持锁等外部 IO 风险

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
