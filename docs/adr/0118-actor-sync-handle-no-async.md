# 0118. Actor 邮箱模型同步 Handle — 消除 async/await 状态机，确定性信号管理

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-09-28
- 决策者：用户（liuqihonggit）+ AI

## 背景

当前 `ActorBase<TCommand, TOut>` 的消息处理管线为：

```
SendAsync (async ValueTask) → Channel → ConsumeLoopAsync (async) → HandleAsync (async ValueTask)
```

其中 `HandleAsync` 是 `protected abstract ValueTask HandleAsync(TCommand command, CancellationToken ct)`，72 个派生类必须实现。

### 问题

1. **async/await 状态机泛滥** — 大量 Actor 的 Handle 逻辑是纯状态转换 + Tell（同步操作），却被迫声明为 `async ValueTask`，产生无谓的状态机分配和代码复杂度
2. **信号管理不确定性** — async Handle 内 `await` 可能跳到线程池其他线程执行，导致 Actor 串行处理保证被打破（状态可见性依赖 Continuation 调度）
3. **背压检测时序不确定** — `NotifyBackpressureIfNeeded` 在 `HandleAsync` 之前调用，但 Handle 内的 async 操作可能改变通道状态，水位线检测与实际状态不一致
4. **调试困难** — async 断点跳转，调用栈断裂，Actor 消息处理流程难以追踪
5. **测试不稳定** — 背压管道测试因 async 调度时序不确定而偶发超时（见 `BackpressurePipelineTest.WatermarkRecovery`）

### 当前异步分布

| 方法 | 当前签名 | 是否必须异步 | 原因 |
|------|---------|-------------|------|
| `SendAsync` | `async ValueTask` | ❌ 否 | `TryWrite` 同步，成功时返回 `CompletedTask` |
| `HandleAsync` | `async ValueTask` | ❌ 否（纯信号 Actor） | 状态转换 + Tell 均同步 |
| `ConsumeLoopAsync` | `async Task` | ✅ 是 | `Channel.ReadAllAsync` 必须 await |
| `AskWithRetryAsync` | `async Task` | ✅ 是 | 等待回执 TCS 必须 await |

## 决策

### 决策1：Handle 改为同步 `void Handle(TCommand, CancellationToken)`

```csharp
// 旧
protected abstract ValueTask HandleAsync(TCommand command, CancellationToken ct);

// 新
protected abstract void Handle(TCommand command, CancellationToken ct);
```

ConsumeLoop 内调用从 `await HandleAsync(cmd, ct)` 改为 `Handle(cmd, ct)`。

### 决策2：SendAsync 改为同步 `void Tell(TCommand)`

```csharp
// 旧
public ValueTask SendAsync(TCommand cmd, CancellationToken ct = default);

// 新
public void Tell(TCommand cmd);
```

`Tell` 内部用 `TryWrite`，写入失败时走 `RetrySend`（fire-and-forget，不阻塞调用方）。取消 `SendAsync` 的 `ValueTask` 返回——Tell 语义本就是 fire-and-forget。

### 决策3：I/O 通过委托模式，不在 Handle 内 await

Handle 内禁止直接 `await` I/O 操作。需要 I/O 时：

```csharp
void Handle(TCommand cmd) {
    switch (cmd) {
        case ReadFileCmd r:
            _ = Task.Run(async () => {
                var data = await File.ReadAllTextAsync(r.Path);
                Tell(new FileLoadedCmd(data, r.OnSuccess));
            });
            return;
        case FileLoadedCmd f:
            _state = UpdateState(_state, f.Data);
            f.OnSuccess(Unit.Value);
            return;
    }
}
```

或委托给专门 I/O Actor：

```csharp
void Handle(TCommand cmd) {
    if (cmd is ReadFileCmd r)
        _ioActor.Tell(new IoReadCmd(r.Path, result => Tell(new FileLoadedCmd(result))));
}
```

### 决策4：保留 AskWithRetryAsync 异步（仅查询路径）

Ask 是请求-应答模式，调用方必须等待回执。保留 `async Task<TReply> AskWithRetryAsync<TReply>(...)` 用于查询路径，但内部通过双 Tell + TCS 桥接实现，不依赖 Handle 的异步性。

### 决策5：ConsumeLoop 仍异步（Channel 读取要求）

```csharp
private async Task ConsumeLoopAsync() {
    await foreach (var cmd in _inputChannel.Reader.ReadAllAsync(_cts.Token)) {
        Interlocked.Decrement(ref _inputCount);
        CheckInputWatermark();
        NotifyBackpressureIfNeeded(cmd);
        try {
            if (cmd is IRequestCommand req && IdempotencyStore is not null &&
                req.TryRestoreFromCache(IdempotencyStore))
                continue;
            Handle(cmd, _cts.Token);  // 同步调用
        } catch (OperationCanceledException) when (_cts.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            OnConsumerError(ex);
        }
    }
}
```

### 决策6：默认全局配置 + 双工管道无例外

```csharp
// 全局默认配置 — 命令构造时不传回调则用此默认值
public static class ActorDefaults {
    public static Action<Exception> OnFailure { get; set; } = static _ => { };
    public static Action<BackpressureSignal> OnBackpressure { get; set; } = static _ => { };
    public static Action<T> DefaultOnSuccess<T>() => static _ => { };
}
```

**设计原则**：
- **没有例外 ≠ 没有配置** — 默认配置保证双工管道畅通，不需要每次传参
- **命令自带回调，有默认值** — 从 `ActorDefaults` 取，不传也保证回执通道通
- **自定义用 `init` 属性覆盖** — `new ReadCmd(path) { OnSuccess = ... }`
- **Tell 签名不变** — `void Tell(TCommand cmd)`，回调不在 Tell 参数上
- **所有命令实现 `IRequestCommand<TReply>`** — ConsumeLoop 不检查类型，直接用回调

## 替代方案

### 方案A：双轨并行（放弃）

保留 `ActorBase`（异步 Handle），新增 `SyncActorBase`（同步 Handle），新 Actor 用同步版，旧 Actor 逐步迁移。

**放弃原因**：
- 两套基类增加维护负担，消费方需要区分
- 迁移期间类型不统一，泛型约束复杂化
- 用户明确要求"无后向兼容"，不需要双轨过渡

### 方案B：保留异步 Handle，用 ValueTask.CompletedTask 占位（放弃）

纯信号 Actor 的 HandleAsync 返回 `ValueTask.CompletedTask`，不实际异步。

**放弃原因**：
- 仍有 async/await 状态机开销（即使不 await 任何东西）
- 代码仍然有 `async` 关键字，不够清晰
- 没有解决信号管理确定性问题

### 方案C：全部同步包括 ConsumeLoop（放弃）

ConsumeLoop 也改为同步，用 `while (channel.TryRead(out var cmd))` 轮询。

**放弃原因**：
- 轮询消耗 CPU，不如 `ReadAllAsync` 高效
- 无法利用 Channel 的异步等待通知
- 阻塞 Consumer 线程，浪费线程池资源

## 影响范围

| 范围 | 影响 |
|------|------|
| `ActorBase<TCommand, TOut>` | `HandleAsync` → `Handle`，`SendAsync` → `Tell` |
| 72 个派生类 | `HandleAsync` → `Handle`，I/O 操作改为委托模式 |
| 调用方 | `await actor.SendAsync(cmd)` → `actor.Tell(cmd)` |
| 测试 | `await actor.SendAsync(cmd)` → `actor.Tell(cmd)` |
| `AskWithRetryAsync` | 保留异步，内部双 Tell + TCS 桥接不变 |

## 迁移策略

1. **ActorBase 核心改造**：`HandleAsync` → `Handle`，`SendAsync` → `Tell`，编译验证
2. **72 个派生类批量迁移**：`async ValueTask HandleAsync(...)` → `void Handle(...)`，移除 `await`，I/O 改为委托
3. **调用方迁移**：`await SendAsync` → `Tell`，移除不必要的 `async/await`
4. **测试迁移**：同步化后测试更稳定，移除 `WaitUntilAsync` 超时 workaround

## 验证标准

- [ ] ActorBase 编译通过
- [ ] 所有 72 个派生类编译通过
- [ ] 现有单元测试全部通过
- [ ] 背压管道测试稳定通过（无偶发超时）
- [ ] 无 async/await 在 Handle 方法体内（分析器校验）

## 参考

- [Erlang Actor 模型](https://www.erlang.org/docs/getting_started/concurrent_prog) — receive 是同步的
- [Akka Typed Actor](https://doc.akka.io/docs/akka/current/typed/actors.html) — Behaviors 是同步的
- ADR [0086](0086-core-tech-selection-lock-design.md) — Actor 模型 + 状态机死锁防护
- ADR [0093](0093-resource-management-exception-style.md) — 资源管理与异常控制
