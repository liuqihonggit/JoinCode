# 0112. Dispose 体内禁止 fire-and-forget + ContinueWith→await 统一释放规范

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-09-17
- 决策者：用户（liuqihonggit）+ AI

## 背景

Dispose/DisposeAsync 方法体内存在三类 fire-and-forget 异步调用，导致释放路径不完整：

1. **模式A（8处）**: `Task.WhenAll(tasks).ContinueWith(cleanup, ...)` + `return ValueTask.CompletedTask`
   - 意图：取消信号 → 等后台任务退出 → Cleanup
   - 问题：ContinueWith 返回 Task 不 await，立即返回 ValueTask.CompletedTask，不等后台任务完成
   - 分布：BusTransport/MeshTransport/NamedPipeTransport/HostElectionService/HostContextSyncService/PriorityMailbox

2. **模式B（2处）**: `_ = xxxAsync()` discard
   - 意图：触发异步操作但不等完成
   - 问题：fire-and-forget 掩盖异常，释放未完成即返回
   - 分布：SupervisedActor/RouterActor

3. **模式C（3处）**: `_writeLoop.ContinueWith(static (t, state) => ...DisposeAsync().AsTask().Wait(), ...)`
   - 意图：等 writeLoop 完成后同步 Dispose stream
   - 问题：ContinueWith fire-and-forget + 内部 .Wait() 阻塞（违反"释放函数禁止超时阻塞"）
   - 分布：BusTransport/MeshTransport/NamedPipeTransport

根因：这些方法都是非 async `ValueTask DisposeAsync()`，无法用 await，只能用 ContinueWith 或 fire-and-forget。

## 决策

### 决策1：JCC9200 分析器重写为禁止 fire-and-forget（Error 级别）

| 检测模式 | 报告 | 正确做法 |
|---------|------|----------|
| `_ = xxxAsync()` discard（返回 Task/ValueTask） | JCC9200 Error | `await xxxAsync().ConfigureAwait(false)` |
| `xxxAsync()` 裸调用不 await（返回 Task/ValueTask） | JCC9200 Error | `await xxxAsync().ConfigureAwait(false)` |
| `xxx.ContinueWith(...)` 裸调用不 await（返回 Task） | JCC9200 Error | `await xxx.ConfigureAwait(false); cleanup(...)` |

分析器用 SemanticModel 检查方法返回类型是否为 Task/ValueTask，精确识别 fire-and-forget。

### 决策2：统一释放规范

| 规则 | 说明 |
|------|------|
| DisposeAsync 必须 async | 所有异步操作必须 await，禁止 fire-and-forget |
| ContinueWith → await | `Task.WhenAll(tasks).ContinueWith(cleanup)` → `await Task.WhenAll(tasks); cleanup()` |
| 同步 Dispose 禁止异步 | Dispose 体内不能有任何异步操作（包括 fire-and-forget） |
| 释放禁止阻塞 | 禁止 .Wait()/.GetAwaiter().GetResult()，释放是必须完成的操作 |
| 条件 return 保留 | `if (...) return ValueTask.CompletedTask` → `if (...) return`（async 方法内） |

### 决策3：三种模式的统一改法

**模式A**: ContinueWith → await Task.WhenAll + 直接调清理
```csharp
// Before
public ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
    _cts.Cancel();
    if (tasks.Count == 0) { Cleanup(...); return ValueTask.CompletedTask; }
    Task.WhenAll(tasks).ContinueWith(static (t, state) => Cleanup(...), ...);
    return ValueTask.CompletedTask;
}

// After
public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
    _cts.Cancel();
    if (tasks.Count > 0) await Task.WhenAll(tasks).ConfigureAwait(false);
    Cleanup(...);
}
```

**模式B**: discard → await
```csharp
// Before
public override ValueTask DisposeAsync()
{
    _ = StopAllChildrenAsync();
    return base.DisposeAsync();
}

// After
public override async ValueTask DisposeAsync()
{
    await StopAllChildrenAsync().ConfigureAwait(false);
    await base.DisposeAsync().ConfigureAwait(false);
}
```

**模式C**: ContinueWith + .Wait() → await + await DisposeAsync
```csharp
// Before
public ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
    _writeQueue.Writer.TryComplete();
    _writeLoop.ContinueWith(static (t, state) => ((Stream)state!).DisposeAsync().AsTask().Wait(), _stream, ...);
    return ValueTask.CompletedTask;
}

// After
public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
    _writeQueue.Writer.TryComplete();
    await _writeLoop.ConfigureAwait(false);
    await _stream.DisposeAsync().ConfigureAwait(false);
}
```

## 替代方案

### 方案A：保留 ContinueWith + 改为 async 方法 await ContinueWith 的结果

- 优点：改动最小，只加 await 不改逻辑结构
- 缺点：ContinueWith + ExecuteSynchronously 语义复杂，不如直接 await Task.WhenAll 清晰；仍有多余的 lambda 分配
- **放弃原因**：ContinueWith 是 Task 的旧式回调模式，async/await 是现代统一模式，统一到 await 更清晰

### 方案B：Warning 级别 + 逐步修复

- 优点：渐进式，不阻塞编译
- 缺点：TreatWarningsAsErrors=true 已启用，Warning 等效于 Error；用户要求强制条例
- **放弃原因**：项目已启用 TreatWarningsAsErrors，且用户要求 Error 级别编译期拦截

### 方案C：保留 fire-and-forget 但加异常日志

- 优点：改动最小，不改变执行语义
- 缺点：fire-and-forget 仍使 Dispose 在异步清理完成前返回，掩盖真实错误；日志不能替代正确等待
- **放弃原因**：用户明确要求释放必须完整完成，不接受 fire-and-forget

## 验证

- [ ] 全量编译：0 JCC9200 错误
- [ ] 分析器测试：20 个全绿
- [ ] async_lock 测试：全绿
- [ ] git 提交

## 影响范围

**整个工程所有 Dispose/DisposeAsync 方法**。JCC9200 为 Error 级别编译期拦截,任何项目违反即编译失败。

**第一批(当前报告 11 处,全在 lib/async_lock/)**:
- `gen/aot_safety.generator/DisposableConsistencyRules.cs` — JCC9200 规则重写
- `gen/aot_safety.tests/DisposableConsistencyRulesTests.cs` — 3 个新测试
- `lib/async_lock/BusTransport.cs` — 2处(模式A:292, 模式C:376)
- `lib/async_lock/MeshTransport.cs` — 2处(模式A:235, 模式C:316)
- `lib/async_lock/NamedPipeTransport.cs` — 2处(模式A:310, 模式C:558)
- `lib/async_lock/HostElectionService.cs` — 1处(模式A:311)
- `lib/async_lock/HostContextSyncService.cs` — 1处(模式A:213)
- `lib/async_lock/PriorityMailbox.cs` — 1处(模式A:308)
- `lib/async_lock/SupervisedActor.cs` — 1处(模式B:267)
- `lib/async_lock/RouterActor.cs` — 1处(模式B:174)

**后续批次**:第一批修复后全量编译,可能暴露依赖链下游项目新的 JCC9200(之前因依赖链断裂未编译到)。迭代修复直到 0 JCC9200。

## 关联

- 上游：[ADR 0108](0108-dispose-consistency-analyzer-rules.md) — Dispose 一致性分析器规则（JCC9105/9106/9107）
- 上游：[ADR 0093](0093-resource-management-exception-style.md) — 资源管理与异常控制风格规范
- 本 ADR：JCC9200 重写 + ContinueWith→await 统一释放规范，补充 ADR 0108 的分析器规则体系

## 后续待办

### 待办1：删掉 DisposableHelper，全部内联为 Interlocked

`lib/async_lock/DisposableHelper.cs` 是多余间接层，且 `ref bool` 重载有非原子竞态 bug。

| 方法 | 内联替换 |
|------|----------|
| `DisposableHelper.TryMarkDisposed(ref int x)` | `Interlocked.Exchange(ref x, 1) == 0` |
| `DisposableHelper.IsDisposed(ref int x)` | `Volatile.Read(ref x) != 0` |
| `DisposableHelper.ThrowIfDisposed(ref int x, obj)` | `ObjectDisposedException.ThrowIf(Volatile.Read(ref x) != 0, obj)` |
| `DisposableHelper.TryMarkDisposed(ref bool x)` | **删** — 非原子，调用方改用 `int` + `Interlocked.Exchange` |
| `DisposableHelper.IsDisposed(ref bool x)` | `x`（直接读，调用方应改用 `int`） |
| `DisposableHelper.ThrowIfDisposed(ref bool x, obj)` | `ObjectDisposedException.ThrowIf(x, obj)` |

**工作量**：57 处调用方，跨 35+ 文件。完成后移走 `DisposableHelper.cs` 到 `.xxx/`。
