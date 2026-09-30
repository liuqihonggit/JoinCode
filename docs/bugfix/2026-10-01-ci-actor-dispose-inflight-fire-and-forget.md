# Bug 日志:CI 偶发测试失败 — Actor Dispose 不等待 in-flight fire-and-forget 任务

**日期**:2026-10-01
**PR**:#352
**触发**:CI #691 run 36767118269 job `unit-tests / Unit - BrainOther` 失败

---

## 已修复 Bug

### Bug: BackgroundTaskActor.DisposeAsync 不等待 in-flight 任务导致 AnalyticsService 历史加载丢失

- **CI**:run 36767118269,job `unit-tests / Unit - BrainOther`
- **症状**:`Core.Tests.CostTracking.AnalyticsServiceTests.Constructor_WithStoragePath_LoadsHistoryFromFile [FAIL]`,`Assert.True() Failure Expected: True Actual: False`,Passed: 405/406
- **复现**:偶发性 — mock 的 `ReadFileAsync` 是 `Task.FromResult`(同步完成),大多数时候 in-flight 任务在 Consumer 退出前跑完;CI 线程池压力大时 `await ... .ConfigureAwait(false)` 切线程池调度延迟,Consumer 退出后任务仍未完成

#### 根因

`BackgroundTaskActor.Handle`（`lib/async_lock/actor/BackgroundTaskActor.cs:66`）用 `_ = ExecuteTaskAsync` fire-and-forget 启动任务,但 `ActorBase.DisposeAsync`（`lib/async_lock/actor/ActorBase.cs:604`）只 `await _consumerTask`（等 Consumer 退出）,不等待 in-flight 的 `ExecuteTaskAsync`。

```
T+0    Tell(LoadHistory) → 命令入输入通道
T+ε    ConsumeLoopAsync 取出命令 → Handle → _ = ExecuteTaskAsync（fire-and-forget）
T+ε    Handle 返回 → ConsumeLoopAsync 继续循环 → 通道空 → ReadAllAsync 等待
T+δ    DisposeAsync → _cts.Cancel + TryComplete → Consumer 退出
T+δ    await _consumerTask 完成 → DisposeAsync 返回
       ↑ 但 ExecuteTaskAsync（LoadHistoryAsync）可能仍在跑！
T+δ+   GetEventHistory() → 拿不到 LoadHistory 加载的数据 → Assert.True(history.Count >= 2) 失败
```

#### 影响范围:55 个 Actor 全部有同样隐患

全量扫描 82 个 `override Handle`,发现 55 个 Actor 在 Handle 里 fire-and-forget 启动任务但 DisposeAsync 未等待:

| 模式 | 数量 | 示例 |
|------|------|------|
| `_ = HandleAsyncImpl(cmd, ct)` | 47 | ChatContextManager/ThinkingStore/SkillService/... |
| 直接 `_ = SomeAsync()` | 5 | BackgroundTaskActor/GatewayActor/MailboxBase/GlobalBuildQueue/StreamingToolExecutorActor |
| 其他 `_ = SomeAsync()` | 3 | CommandExecutionAuditor/RouterActor/ToolHealthMonitor |
| 同步操作无需修复 | 27 | PlanModeManager/TokenBudgetManager/ConsoleActor/... |

#### 修复:ActorBase.RegisterInFlight 统一守卫基础设施

在 `ActorBase` 加 `protected void RegisterInFlight(Task task)`,子类 Handle 里 fire-and-forget 启动任务后调 `RegisterInflight(task)` 注册。`ActorBase.DisposeAsync` 在 `await _consumerTask` 后自动 `await Task.WhenAll(_inFlightTasks)` 等待所有 in-flight 任务完成。

```csharp
// ActorBase 基础设施
private readonly ConcurrentBag<Task> _inFlightTasks = new();

protected void RegisterInFlight(Task task) {
    ThrowIfDisposed();
    _inFlightTasks.Add(task);
}

public virtual async ValueTask DisposeAsync() {
    // ... 等 Consumer 退出 ...
    var inflight = _inFlightTasks.ToArray();
    if (inflight.Length > 0) {
        await Task.WhenAll(inflight).ConfigureAwait(false);
    }
    _cts.Dispose();
}
```

子类用法（统一模式,不需要自己维护 ConcurrentBag + override DisposeAsync）:

```csharp
// BackgroundTaskActor
protected override void Handle(BackgroundTaskCommand command, CancellationToken ct) {
    RegisterInFlight(ExecuteTaskAsync(command, ct));
}

// HandleAsyncImpl 模式（47 个 Actor）
protected override void Handle(SomeCmd cmd, CancellationToken ct) {
    RegisterInFlight(HandleAsyncImpl(cmd, ct).AsTask());
}
```

#### 确定性复现测试

`lib/async_lock.tests/dispose_bug/BackgroundTaskActorDisposeBugTest.cs` — 2 个用例,用 `TaskCompletionSource` 构造确定性时序:

- `DisposeAsync_ShouldWaitForInFlightTask_NotReturnBeforeTaskCompletes` — 单个 in-flight 任务
- `DisposeAsync_ShouldWaitForAllInFlightTasks_WhenMultiple` — 多个 in-flight 任务

修复前:DisposeAsync 在 200ms 内返回（未等待 in-flight）→ 断言失败
修复后:DisposeAsync 阻塞等待 in-flight → delayTask 先完成 → 断言通过

#### 验证

- 全量编译:0 警告 0 错误
- async_lock.tests:521 通过（含 2 个复现测试）
- brain.other.tests:406 通过（含原 CI 失败的 Constructor_WithStoragePath_LoadsHistoryFromFile）
- agents.tests:628 通过
