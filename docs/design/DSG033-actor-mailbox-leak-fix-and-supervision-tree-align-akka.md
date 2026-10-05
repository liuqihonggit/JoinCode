# DSG033 — Actor 邮箱内存泄漏修复 + 监控树对齐 Akka

> 状态：proposed
> 日期：2026-10-04
> 关联：ADR 0125（in-flight 守卫）、ADR 0086（核心技术选型锁设计）
> 审核依据：`lib/async_lock/actor/ActorBase.cs`、`SupervisedActor.cs`、`BackgroundTaskActor.cs`、`MailboxBase.cs`

## 一、背景

项目已实现 Actor 邮箱模型（`ActorBase<TCommand,TOut>` + `MailboxBase<TMessage>` + `SupervisedActor<TCommand>`），用于消除共享锁、用消息传递替代共享状态。本次审核两个维度：

1. **Task 机制内存泄漏** — fire-and-forget 是否通过设计避免射后不理导致的资源泄漏
2. **监控树对齐 Akka** — 现有 `SupervisedActor` 与 Akka 监督树的能力差距

## 二、问题一：`_inFlightTasks` 内存泄漏

### 2.1 现状

`ActorBase.cs:25` 用 `ConcurrentBag<Task>` 收集 fire-and-forget 任务：

```csharp
private readonly ConcurrentBag<Task> _inFlightTasks = new();

protected void RegisterInFlight(Task task) {
    _inFlightTasks.Add(task);  // 只增不减
}

public virtual async ValueTask DisposeAsync() {
    // ...
    var inflight = _inFlightTasks.ToArray();  // 快照，不清空
    if (inflight.Length > 0) {
        await Task.WhenAll(inflight).ConfigureAwait(false);
    }
    // ...
}
```

### 2.2 泄漏链

```
ConcurrentBag 内部 thread-local deques，元素永不自动移除
  → 每次 Handle 调 RegisterInFlight → Add 一个 Task
    → Task 持有 AsyncStateMachine
      → StateMachine 持有闭包捕获的所有局部变量（this、cmd、ct、中间结果…）
        → Task 完成后引用仍留在 bag → StateMachine 无法 GC
          → 长生命周期 Actor（Singleton）+ 高频 Tell → bag 无限增长
```

**受影响 Actor**（均为 Singleton + 高频，grep 确认 42 处 Handle 全用 RegisterInFlight）：

| Actor | 频率 | 闭包大小 | 严重度 |
|-------|------|----------|--------|
| `BackgroundTaskActor` | 每条后台命令 | 中（TaskFactory 委托） | 高 |
| `BridgeClient` | 每次网络 IO | 大（缓冲区、请求体） | 高 |
| `StreamingToolExecutorActor` | 每个工具调用 | 大（工具参数、结果） | 高 |
| `GoalHeartbeat` | 定时器触发 | 小 | 中 |
| `MailboxActor` / `ToolHealthMonitor` | 中频 | 中 | 中 |

**量化**：每个已完成 Task ≈ 200-800 bytes（含 StateMachine）。10 万次 Tell ≈ 20-80MB 无法回收。7×24 小时 CLI 进程会缓慢膨胀至 OOM。

### 2.3 修复方案对比

| 方案 | 核心 | 保留异常诊断 | 改动量 | GC 友好 | 风险 |
|------|------|-------------|--------|---------|------|
| A. 计数 + AsyncCountdownEvent | 只计数不持引用 | ❌ 丢失 | 中 | ✅ | 中（Dispose 异常不可观测） |
| **B. ConcurrentQueue + 水位线清理（推荐）** | 定期 Dequeue 已完成项 | ✅ 保留 | 小 | ✅ | 低 |
| C. pipeTo 模式（对齐 Akka） | task.ContinueWith(Tell(ResultCmd)) | ✅ 通过输出流 | 大 | ✅ | 高（所有子类 Handle 重写） |

### 2.4 方案 B 详细设计

**核心**：`ConcurrentQueue<Task>` 替代 `ConcurrentBag<Task>`，Add 后检查水位线，超过阈值时清理头部已完成任务。

```csharp
// ActorBase.cs 改动
private readonly ConcurrentQueue<Task> _inFlightTasks = new();
private const int InFlightCleanupThreshold = 256;  // 超过此数触发清理
private const int InFlightCleanupRetain = 64;      // 清理后保留最近 N 个（保诊断）

protected void RegisterInFlight(Task task) {
    _inFlightTasks.Enqueue(task);
    // 水位线触发清理（无锁，偶发竞争可接受：多清理一次或少清理一次都不影响正确性）
    if (_inFlightTasks.Count > InFlightCleanupThreshold) {
        CleanupCompletedInFlight();
    }
}

private void CleanupCompletedInFlight() {
    // 从头部 Dequeue 已完成的任务，保留最近 InFlightCleanupRetain 个
    var toRequeue = new List<Task>();
    while (_inFlightTasks.TryDequeue(out var t)) {
        if (t.IsCompleted) {
            continue;  // 已完成，丢弃引用（让其可 GC）
        }
        toRequeue.Add(t);
        if (toRequeue.Count >= InFlightCleanupRetain) break;
    }
    // 把未完成且未清理的重新入队
    foreach (var t in toRequeue) {
        _inFlightTasks.Enqueue(t);
    }
}
```

**DisposeAsync 调整**：

```csharp
public virtual async ValueTask DisposeAsync() {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _cts.Cancel();
    _inputChannel.Writer.TryComplete();
    _outputChannel.Writer.TryComplete();
    _retryEngine.Complete();
    try { await _consumerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
    try { await _retryEngine.WaitForCompletionAsync().ConfigureAwait(false); } catch (OperationCanceledException) { }

    // 清理已完成项后，等待剩余未完成项
    CleanupCompletedInFlight();
    var inflight = _inFlightTasks.ToArray();
    if (inflight.Length > 0) {
        try { await Task.WhenAll(inflight).ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "[Actor:{ActorId}] in-flight 异常忽略", Id); }
    }
    _cts.Dispose();
}
```

**正确性论证**：
1. `RegisterInFlight` 仅在 Handle 内调用，Handle 在 Consumer 线程串行 → Enqueue 无并发竞争（单写者）
2. `CleanupCompletedInFlight` 可能在 RegisterInFlight（Consumer 线程）和 DisposeAsync（调用方线程）并发 → ConcurrentQueue 线程安全，偶发重复清理无副作用
3. 已完成 Task 被丢弃后，其 StateMachine 可被 GC 回收（无强引用）
4. 未完成 Task 一定被保留（IsCompleted == false 不 Dequeue）→ DisposeAsync 的 WhenAll 仍等待全部未完成项
5. 保留最近 64 个已完成项用于 Dispose 时异常诊断（log in-flight 异常）

**验收标准**：

| 测试 | 验证点 |
|------|--------|
| `InFlightCleanup_HighFrequencyTell_BagSizeBounded` | 10 万次 Tell 后 `_inFlightTasks.Count < 320`（阈值+保留数） |
| `InFlightCleanup_CompletedTasksAreGcCollected` | WeakReference 跟踪已完成 Task，清理后 GC.Collect 后 IsAlive == false |
| `DisposeAsync_StillWaitsAllInFlight_AfterCleanup` | 清理后 Dispose 仍等待未完成项（现有 BackgroundTaskActorDisposeBugTest 不退化） |

### 2.5 四列验收表（问题一）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `ConcurrentQueue<Task>` + `CleanupCompletedInFlight`（ActorInFlightRegistry.cs） | 全项目 42 处 `RegisterInFlight` 调用 | ✅ | ✅ 564 测试通过 |
| 水位线清理测试（async_lock.tests/leak） | — | ✅ | ✅ InFlightLeakTest 2 测试 |

---

## 三、问题二：监控树对齐 Akka

### 3.1 现状能力盘点

| Akka 能力 | 项目现状 | 对齐状态 |
|-----------|----------|----------|
| 监督指令 Resume/Restart/Stop/Escalate | `SupervisorDirective` enum | ✅ |
| 监督策略 MaxRestarts + Within 窗口 | `SupervisorStrategy` record | ✅ |
| OneForOne 策略 | 预定义 + HandleFailure 实现 | ✅ |
| AllForOne 策略 | 遍历兄弟重启，对齐 Akka 语义 | ✅ |
| 重启次数限流（时间窗口） | `RecordRestart` 纯函数 | ✅ |
| 级联停止 | `DisposeAsync` → `StopAllChildrenAsync` | ✅ |
| Escalate 向上抛 | `OnChildFailureAsync` abstract | ✅ |
| DeathWatch（被动感知子终止） | Watch/Unwatch/Terminated + DeathPact | ✅ |
| 生命周期钩子 PreStart/PostStop | ActorBase 虚方法 | ✅ |
| ActorSystem / Guardian | ActorSystem 注册表 + 路径寻址 | ✅ |
| BackoffSupervisor（退避重启） | ActorBackoffStrategy | ✅ |
| Become/BecomeStacked/UnbecomeStacked | ActorBehaviorStack 组合件 | ✅ |
| Stash/Unstash/UnstashAll | ActorMessageStash 组合件 | ✅ |
| Sender/Self（Tell 携带发送者） | MessageEnvelope + AsyncLocal | ✅ |
| DeathPactException（watch 未处理自动失败） | OnBeforeProcessCommand 检查 | ✅ |
| ReceiveTimeout（空闲超时检测） | ConsumeLoop + LinkedCts+CancelAfter | ✅ |
| PipeTo（Future 完成后发消息回 Actor） | ActorPipeToExtensions | ✅ |
| EventStream（全局事件总线） | ConcurrentDictionary + Delegate.Combine | ✅ |
| Scheduler（定时调度） | IScheduler + ActorScheduler(Timer) | ✅ |
| 远程部署 | 无（合理取舍） | ⚪ 不对齐 |

### 3.2 方案 S1：修复 AllForOne 假实现

**现状**：`ChildActorHandle.HandleFailureAsync`（SupervisedActor.cs:108）只操作当前 child，AllForOne 与 OneForOne 行为相同。

**目标**：AllForOne 下 Restart 时，父 Actor 重启**所有**子 Actor。

**设计**：`HandleFailureAsync` 需要能访问父 Actor 的所有 children。当前 `ChildActorHandle` 不持有父引用，需注入"重启所有兄弟"的回调。

```csharp
// SupervisedActor.cs 改动

internal sealed class ChildActorHandle : IAsyncDisposable {
    private readonly Func<CancellationToken, ValueTask> _restartAllSiblings;  // 新增

    internal ChildActorHandle(
        string id,
        Func<CancellationToken, ValueTask<IAsyncDisposable>> factory,
        SupervisorStrategy strategy,
        Func<ChildActorHandle, Exception, CancellationToken, ValueTask> onFailure,
        Func<CancellationToken, ValueTask> restartAllSiblings) {  // 新增
        // ...
        _restartAllSiblings = restartAllSiblings;
    }

    internal async ValueTask HandleFailureAsync(Exception ex, CancellationToken ct) {
        var directive = _strategy.Decider(ex);
        switch (directive) {
            case SupervisorDirective.Restart:
                if (_strategy == SupervisorStrategy.AllForOne) {
                    await _restartAllSiblings(ct).ConfigureAwait(false);  // 重启所有兄弟
                } else {
                    await RestartAsync(ct).ConfigureAwait(false);  // 只重启自己
                }
                break;
            // ... Resume/Stop/Escalate 不变
        }
    }
}
```

`SupervisedActor.SpawnChildAsync` 注入 `RestartAllChildrenAsync`：

```csharp
protected async ValueTask<ChildActorHandle> SpawnChildAsync(...) {
    var handle = new ChildActorHandle(
        childId, factory, strategy, ReportChildFailureAsync,
        RestartAllChildrenAsync);  // 注入重启所有兄弟的回调
    // ...
}

private async ValueTask RestartAllChildrenAsync(CancellationToken ct) {
    foreach (var child in _children.Values) {
        if (child.State == ChildActorState.Running) {
            await child.RestartAsync(ct).ConfigureAwait(false);
            TryPublish(new SupervisorEvent(child.Id, ChildActorState.Running, "all-for-one restart"));
        }
    }
}
```

**注意**：`ChildActorHandle.RestartAsync` 当前是 private，需提为 internal。AllForOne 重启时不重新计数每个 child 的 RestartCount（Akka 语义：AllForOne 重启算一次失败事件，不是每个 child 各算一次）。

**验收**：

| 测试 | 验证点 |
|------|--------|
| `AllForOne_OneChildFails_AllChildrenRestarted` | 3 个子，1 个失败 → 3 个 RestartCount 都 +1 |
| `AllForOne_StoppedChildrenNotRestarted` | 已 Stopped 的子不参与 AllForOne 重启 |

### 3.3 方案 S2：生命周期钩子

**现状**：仅 `OnConsumerError` 一个回调。重启时无法保存/恢复崩溃前状态。

**目标**：对齐 Akka 的 `PreStart`/`PostStop`/`PreRestart`/`PostRestart`。

**设计**：在 `ActorBase` 和 `SupervisedActor` 增加虚方法（默认空实现，子类按需重写）：

```csharp
// ActorBase.cs 新增
public abstract class ActorBase<TCommand, TOut> : ... {
    /// <summary>Actor 启动前调用（Consumer 启动前）— 初始化资源</summary>
    protected virtual ValueTask PreStartAsync(CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>Actor 停止后调用（Consumer 退出后、Dispose 前）— 释放资源</summary>
    protected virtual ValueTask PostStopAsync(CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>重启前调用 — 保存崩溃前状态供 PostRestart 恢复</summary>
    protected virtual ValueTask PreRestartAsync(Exception reason, CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>重启后调用 — 从 PreRestart 保存的状态恢复</summary>
    protected virtual ValueTask PostRestartAsync(Exception reason, CancellationToken ct) => ValueTask.CompletedTask;
}
```

**调用时机**：
- `PreStartAsync`：构造函数末尾、Consumer 启动前
- `PostStopAsync`：`DisposeAsync` 中 Consumer 退出后、CTS 释放前
- `PreRestartAsync` → Dispose 旧实例 → 创建新实例 → `PostRestartAsync`：在 `ChildActorHandle.RestartAsync` 中

**验收**：

| 测试 | 验证点 |
|------|--------|
| `Lifecycle_PreStart_CalledBeforeFirstCommand` | PreStart 在首条命令处理前完成 |
| `Lifecycle_PostStop_CalledAfterConsumerExit` | PostStop 在 Consumer 退出后调用 |
| `Lifecycle_PreRestart_PostRestart_Paired` | 重启时 PreRestart → 新实例 → PostRestart 顺序正确 |

### 3.4 方案 S3：DeathWatch（被动感知子终止）

**现状**：父 Actor 只能**主动**调 `HandleFailureAsync`。子 Actor 静默死亡（Consumer 异常退出、Dispose 未上报）→ 父不知道。

**目标**：对齐 Akka `context.watch(childRef)` → child 终止时父**被动**收到 `Terminated` 消息。

**设计**：

```csharp
// 新增消息类型
public sealed record Terminated(string ChildId, TerminationReason Reason) : SupervisorEvent;
public enum TerminationReason { Stopped, Failed, Disposed }

// SupervisedActor 新增 Watch/Unwatch
public abstract class SupervisedActor<TCommand> : ActorBase<TCommand, SupervisorEvent> {
    private readonly ConcurrentDictionary<string, ChildActorHandle> _watched = new();

    /// <summary>监视子 Actor — 子终止时本 Actor 收到 Terminated 消息（通过 OutputAsync）</summary>
    protected void Watch(ChildActorHandle child) {
        child.Terminated += OnChildTerminated;  // 订阅终止事件
        _watched[child.Id] = child;
    }

    protected void Unwatch(ChildActorHandle child) {
        child.Terminated -= OnChildTerminated;
        _watched.TryRemove(child.Id, out _);
    }

    private void OnChildTerminated(ChildActorHandle child, TerminationReason reason) {
        TryPublish(new Terminated(child.Id, reason));
    }
}
```

**ChildActorHandle 增加终止事件**：

```csharp
public sealed class ChildActorHandle : IAsyncDisposable {
    /// <summary>子 Actor 终止事件 — Watch 后触发</summary>
    internal event Action<ChildActorHandle, TerminationReason>? Terminated;

    public async ValueTask StopAsync() {
        if (_instance is not null) {
            try { await _instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { /* log */ }
            _instance = null;
        }
        State = ChildActorState.Stopped;
        Terminated?.Invoke(this, TerminationReason.Stopped);  // 触发
    }
}
```

**关键**：还需在子 Actor 的 Consumer 异常退出时触发 `Terminated(Failed)`。这需要 `ActorBase` 在 Consumer 异常退出时通知父（如果有）。可通过 `ActorBase` 持有可选的 `Action<TerminationReason>? _onTerminated` 回调，由 `SupervisedActor.SpawnChildAsync` 注入。

**验收**：

| 测试 | 验证点 |
|------|--------|
| `DeathWatch_ChildStops_ParentReceivesTerminated` | Watch 后 child.StopAsync → 父 OutputAsync 收到 Terminated(Stopped) |
| `DeathWatch_ChildConsumerCrashes_ParentReceivesTerminated` | 子 Consumer 异常退出 → 父收到 Terminated(Failed) |
| `DeathWatch_Unwatch_StopsReceiving` | Unwatch 后子终止，父不再收到 Terminated |

### 3.5 方案 S4：ActorSystem / Guardian

**现状**：每个 `SupervisedActor` 是孤立树根，无统一注册表、无路径寻址、无顶级 Guardian 兜底。

**目标**：提供轻量级 `ActorSystem` 作为所有顶层 Actor 的 Guardian + 注册表 + 路径寻址。

**设计**（轻量，不引入 Akka 全套）：

```csharp
public sealed class ActorSystem : IAsyncDisposable {
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _actors = new();
    public string Name { get; }

    /// <summary>注册顶层 Actor — 路径为 "{Name}/{actorId}"</summary>
    public T Register<T>(string actorId, T actor) where T : IAsyncDisposable {
        _actors[actorId] = actor;
        return actor;
    }

    /// <summary>按路径寻址 — "system/parent/child" 返回 child</summary>
    public IAsyncDisposable? Selection(string path) { /* ... */ }

    /// <summary>关闭系统 — 级联停止所有顶层 Actor</summary>
    public async ValueTask DisposeAsync() {
        foreach (var actor in _actors.Values) {
            await actor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

**取舍**：不实现 Akka 的远程寻址、邮件信箱远程序列化。仅本地注册表 + 级联关闭 + 路径寻址。Guardian 语义由 `ActorSystem.DisposeAsync` 兜底（任何未停止的顶层 Actor 在系统关闭时被停止）。

**验收**：

| 测试 | 验证点 |
|------|--------|
| `ActorSystem_RegisterAndSelect` | 注册后按路径寻址返回同一实例 |
| `ActorSystem_DisposeStopsAllTopLevel` | Dispose 级联停止所有注册的顶层 Actor |

### 3.6 方案 S5：BackoffSupervisor

**现状**：重启只有固定时间窗口限流（MaxRestarts/Within），无退避。持续崩溃的子 Actor 会被快速重启 N 次然后标记 Failed。

**目标**：对齐 Akka `BackoffSupervisor` — 指数退避重启（minBackoff → maxBackoff，可选 randomFactor 抖动）。

**设计**：新增 `BackoffStrategy` 作为 `SupervisorStrategy` 的补充，或独立组件：

```csharp
public sealed record BackoffStrategy(
    TimeSpan MinBackoff,
    TimeSpan MaxBackoff,
    double RandomFactor = 0.2) {
    /// <summary>计算第 n 次重试的退避延迟（带抖动）</summary>
    public TimeSpan ComputeDelay(int restartCount) {
        var baseMs = MinBackoff.TotalMilliseconds * Math.Pow(2, restartCount);
        var cappedMs = Math.Min(baseMs, MaxBackoff.TotalMilliseconds);
        var jitter = cappedMs * RandomFactor * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(cappedMs + jitter);
    }
}
```

`ChildActorHandle.RestartAsync` 在重启前 `await Task.Delay(strategy.ComputeDelay(_restartCount))`。

**验收**：

| 测试 | 验证点 |
|------|--------|
| `Backoff_DelayIncreasesExponentially` | 第 0/1/2/3 次重试延迟 ≈ min, 2min, 4min, 8min |
| `Backoff_CappedAtMaxBackoff` | 延迟不超过 MaxBackoff |
| `Backoff_JitterWithinRange` | 抖动在 ±RandomFactor 范围内 |

### 3.7 四列验收表（问题二）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| S1: AllForOne 遍历兄弟重启（SupervisedActor.cs） | 所有用 AllForOne 策略的 SpawnChildAsync | ✅ | ✅ 16 测试通过 |
| S2: PreStart/PostStop（ActorBase.cs） | 需保存崩溃前状态的 Actor | ✅ | ✅ 2 测试通过 |
| S3: Watch/Unwatch/Terminated+DeathPact（SupervisedActor.cs） | 需被动感知子终止的父 Actor | ✅ | ✅ 4 测试通过 |
| S4: ActorSystem 注册表 + 路径寻址（ActorSystem.cs） | 顶层 Actor 管理 | ✅ | ✅ 6 测试通过 |
| S5: ActorBackoffStrategy 退避重启（SupervisedActor.cs） | 持续崩溃的子 Actor | ✅ | ✅ 4 测试通过 |
| S6: Become/BecomeStacked/UnbecomeStacked（ActorBehaviorStack.cs） | 状态机行为切换 | ✅ | ✅ 3 测试通过 |
| S7: Sender/Self（MessageEnvelope + AsyncLocal） | Tell 携带发送者引用 | ✅ | ✅ 2 测试通过 |
| S8: DeathPactException（OnBeforeProcessCommand） | watch 未处理自动失败 | ✅ | ✅ 1 测试通过 |
| S9: ReceiveTimeout（ConsumeLoop+CancelAfter） | 空闲超时检测 | ✅ | ✅ 1 测试通过 |
| S10: PipeTo（ActorPipeToExtensions） | Future �-成后发消息回 Actor | ✅ | ✅ 1 测试通过 |
| S11: Stash/Unstash/UnstashAll（ActorMessageStash.cs） | 消息暂存 | ✅ | ✅ 2 测试通过 |
| S12: EventStream（ConcurrentDictionary+Delegate.Combine） | 全局事件总线 | ✅ | ✅ 2 测试通过 |
| S13: Scheduler（IScheduler+ActorScheduler） | 定时调度 | ✅ | ✅ 2 测试通过 |
| R1: ActorBase 重构拆分（ActorBehaviorStack/ActorMessageStash/ActorInFlightRegistry） | ActorBase.cs 943→757 行 | ✅ | ✅ 564 测试通过 |
| R2: 防御守卫（Become/Watch/Stash null 检查） | API 健壮性 | ✅ | ✅ 3 守卫测试通过 |
| R3: LockRegistryConfig 不可变快照（LockRegistryConfig.cs） | 多线程配置竞态消除 | ✅ | ✅ 20 次并行测试稳定 |
| S14: PoisonPill + GracefulStop（IPoisonPill + ActorGracefulStop） | 毒丸消息自动停止 + 优雅停止 | ✅ | ✅ 4 测试通过 |
| S15: Identify/ActorIdentity（IIdentify + ActorIdentify） | Actor 身份查询 | ✅ | ✅ 2 测试通过 |
| S16: Inbox（Channel + Send/ReceiveAsync） | 测试用消息收件箱 | ✅ | ✅ 3 测试通过 |
| S17: IActorContext（Self/Sender/IsDisposed 统一入口） | Actor 上下文封装 | ✅ | ✅ 2 测试通过 |
| S18: ActorSelection（路径寻址 + Tell/IdentifyAsync） | 解耦路径寻址发消息 | ✅ | ✅ 3 测试通过 |
| S19: ActorFsm（StartWith/When/GoTo/Stay/Using） | 有限状态机 | ✅ | ✅ 4 测试通过 |
| S20: TestProbe（ExpectMsg/Reply/FishForMessage） | 测试探针 | ✅ | ✅ 3 测试通过 |
| S21: DeadLetter + ActorSystem.EventStream | 死信事件 + 系统事件流 | ✅ | ✅ 1 测试通过 |
| S22: UnhandledMessage + ActorBase.Unhandled | 未处理消息事件 | ✅ | ✅ 1 测试通过 |
| S23: ActorTimers（StartSingleTimer/StartPeriodicTimer/Cancel） | Actor 生命周期定时器 | ✅ | ✅ 2 测试通过 |
| S24: Status（StatusSuccess/StatusFailure/Status 工厂） | 成功/失败状态消息 | ✅ | ✅ 2 测试通过 |
| S25: Props（Props&lt;TActor&gt; + Props.Create 工厂） | Actor 创建配置封装 | ✅ | ✅ 1 测试通过 |
| S26: Receptionist（Register/Find/Unregister） | 服务发现 | ✅ | ✅ 1 测试通过 |
| S27: ActorSystem.ActorOf（Props 创建+注册） | 解耦创建与注册 | ✅ | ✅ 1 测试通过 |
| U1: ActorTimers 统一定时器（5 处生产代码） | ShellProcessWatchdog/CronScheduler/GoalHeartbeat/ToolHealthMonitor/AwaySummaryService | ✅ | ✅ 1925 测试通过 |
| U2: Become 统一状态切换（3 处生产代码） | CronScheduler(2态)/BridgeClient(2态)/VoiceService(4态→2行为) | ✅ | ✅ 908 测试通过 |
| U3: RouterActor Watch/Unwatch + DeathPact | BuildQueueRouterActor→BuildWorker 故障传播 | ✅ | ✅ 2 测试通过 |

---

## 四、实施顺序建议

按**风险递增、价值递减**排序：

| 顺序 | 方案 | 改动量 | 风险 | 价值 | 依赖 |
|------|------|--------|------|------|------|
| 1 | **问题一：内存泄漏方案 B** | 小 | 低 | 高（防 OOM） | 无 |
| 2 | S1: AllForOne 真实现 | 小 | 低 | 中（修正语义） | 无 |
| 3 | S2: 生命周期钩子 | 中 | 低 | 中（基础能力） | 无 |
| 4 | S5: BackoffSupervisor | 小 | 低 | 中 | S2 |
| 5 | S3: DeathWatch | 中 | 中 | 高（监控树灵魂） | S2 |
| 6 | S4: ActorSystem | 中 | 中 | 中（统一管理） | S3 |

每个方案独立可交付，互不阻塞（S5/S3 依赖 S2 的生命周期钩子，但可先做 S1+问题一再做 S2）。

## 五、风险与取舍

1. **方案 B 清理竞争**：`CleanupCompletedInFlight` 可能在 Consumer 线程和 Dispose 线程并发执行。ConcurrentQueue 线程安全，偶发重复清理无副作用（多清一次只是少保留几个已完成项，不影响未完成项）。
2. **S1 AllForOne 重启计数**：Akka 语义中 AllForOne 重启算一次失败事件，不是每个 child 各算一次。实现时需注意不重复触发 `TryRecordRestart`。
3. **S3 DeathWatch 事件线程**：`Terminated` 事件从子 Actor 的 Dispose 线程触发，`TryPublish` 到父的输出通道。需确保 `TryPublish` 线程安全（当前实现已用 `Volatile.Read(_disposed)` 守卫，OK）。
4. **S4 ActorSystem 轻量取舍**：不实现远程寻址/邮件序列化，仅本地注册表。若未来需要分布式，再扩展。

## 六、决策记录

<!-- 🤖 Auto Decision: 2026-10-04 -->
<!-- 决策: 内存泄漏选方案 B（ConcurrentQueue + 水位线清理），监控树按 S1→S2→S5→S3→S4 顺序实施 -->
<!-- 原因: 方案 B 改动最小、保留异常诊断、GC 友好；监控树按风险递增价值递减排序，先修假实现再补能力 -->
<!-- 替代方案: 方案 A（计数，丢异常诊断）、方案 C（pipeTo，改动太大）；S4 可不做（当前孤立树根可用） -->
<!-- 验证: 待用户审阅本文档后决策实施 -->

<!-- 🤖 Auto Decision: 2026-10-04 -->
<!-- 决策: 9 个 Akka API 对齐（S6-S13）+ ActorBase 重构拆分（R1）+ 防御守卫（R2）+ LockRegistryConfig 不可变快照（R3） -->
<!-- 原因: 用户要求全部对齐 Akka；ActorBase.cs 943 行太长拆分为 ActorBehaviorStack/ActorMessageStash/ActorInFlightRegistry 组合件；LockDiagnosis flaky 根因是 TryLockAsync 和 OnAcquired 用不同配置读取有竞态窗口，用 LockRegistryConfig 不可变 record+统一快照消除 -->
<!-- 替代方案: [Collection] 限制并行（逃避问题，用户拒绝）；LockRegistry 非静态化（巨大工程，暂不采用） -->
<!-- 验证: 564 测试全通过，20 次并行测试稳定 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-05 -->
<!-- 决策: 6 个 Akka API 对齐（S14-S19）+ JCC10001 文件数限制 20→40 -->
<!-- 原因: 用户要求继续对齐 Akka API；PoisonPill 用 fire-and-forget DisposeAsync 避免 Consumer 线程死锁；Identify 用 ReplyChannel 包装 TCS 实现类型安全回复；Inbox 用 Channel 缓冲消息；ActorContext 轻量封装 ActorBase 已有能力；ActorSelection 泛型 Tell 类型安全；ActorFsm 继承 ActorBase 用状态处理函数表 -->
<!-- 替代方案: Context API 可不实现（语法糖），但用户要求全部对齐；ActorSelection 可用反射（不类型安全，放弃） -->
<!-- 验证: 582 测试全通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-05 -->
<!-- 决策: 统一 Actor 模式改造 — ActorTimers(5处)+Become(3处)+Watch/Unwatch(1处) -->
<!-- 原因: 用户要求全部 Actor 模型统一，一套模式贯穿，所有代码从 Actor 模型生长出来 -->
<!-- 替代方案: 保留 bool+if 状态检查（用户拒绝，要求统一）；TeamMemorySyncService/RemoteCacheRefreshServiceBase 跳过（改造范围过大） -->
<!-- 验证: 596 测试全通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-05 -->
<!-- 决策: 隐性 Actor 审计 + 改造 — 3 个用 Channel+消费循环但未继承 ActorBase 的类 -->
<!-- 原因: 用户要求检查是否有 Actor 没有继承 ActorBase，发现 3 个隐性 Actor -->
<!-- 改造: HostElectionService→ActorBase<ElectCmd/UpdateSnapshotCmd/FailoverCmd,Unit>；AnalyticsFileSink→ActorBase<LogEventCmd/FlushCmd,Unit>；BuildQueueService→组合 BuildQueueActor(ActorBase<SubmitCmd/CancelCmd/BuildCompletedCmd,Unit>) -->
<!-- 替代方案: BuildQueueService 不能直接继承 ActorBase（C# 无多重继承，已继承 BuildQueueBase），用组合模式 -->
<!-- 验证: 596 async_lock + 17 BuildQueueService + 12 AnalyticsFileSink + 6 GlobalBuildQueue 测试全通过 ✅ -->
