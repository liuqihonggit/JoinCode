# 0051. Fork 并发上限

- 状态：accepted
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0048](docs/adr/0048-subagent-concurrency-unified-config.md) | [0050](docs/adr/0050-spawn-stage-concurrency-limit.md)
- 验证：Agents 编译 0 警告 0 错误，ForkSubAgentManager 测试 12 通过 ✅

## 背景

`ForkSubAgentManager`（`core/ai/Agents/src/Coordinator/Fork/ForkSubAgentManager.cs`）当前**无并发 fork 限制**：

### 1. _lock 仅保护字典，不限制并发 fork 数

```csharp
// ForkSubAgentManager.cs:50
private readonly SemaphoreSlim _lock;  // SemaphoreSlim(1, 1)

// 构造函数
_lock = new SemaphoreSlim(1, 1);  // ❌ 互斥锁，保护 _entries 字典操作，不限制并发 fork 数
```

`_lock.WaitAsync` 仅在 `_entries[forkId] = ...` 赋值时持有，赋值完即释放。fork 的实际执行（`_pipeline.ExecuteAsync`）在锁外，可无限并发。

### 2. 后台 fork 无限流

```csharp
// ForkSubAgentManager.cs:176-187
if (context.IsBackground && context.Agent is not null)
{
    _ = RunBackgroundForkAsync(forkId, context.Agent, ..., forkToken)
        .WaitAsync(TimeSpan.FromSeconds(10), forkToken).ConfigureAwait(false);
    // ❌ fire-and-forget，无并发计数，可同时启动无限个后台 fork
}
```

每个后台 fork 启动一个 `RunBackgroundForkAsync` 任务，无全局并发上限。

### 3. fork 的资源开销

fork 涉及：
- **子代理 spawn**（`ForkSpawnMiddleware` → `SpawnSubAgentAsync`）— 虽然 ADR 0050 给 spawn 加了限流，但 fork 还涉及：
- **共享缓存复制**（`_sharedCache`）
- **后台任务长期占用**（`RunBackgroundForkAsync` 直到子代理执行完成）
- **事件通道写入**（`SubAgentEventChannel.Emit`）

### 4. fork 深度已有保护，广度没有

`CalculateForkDepth` 限制了 fork 嵌套深度（防止递归 fork），但**广度**（同一 parent 同时 fork 多少个）无限制。

## 决策

**在 `ForkSubAgentManager` 引入 fork 并发信号量**，限制同时活跃的 fork 数。

### 1. 新增 fork 并发信号量

```csharp
// ForkSubAgentManager.cs
private readonly SemaphoreSlim _forkSemaphore;  // fork 并发上限

public ForkSubAgentManager(
    MiddlewarePipeline<ForkContext> pipeline,
    ForkManagerDependencies deps,
    SubAgentConcurrencyOptions concurrencyOptions,  // 新增依赖
    ILogger<ForkSubAgentManager>? logger = null,
    IClockService? clock = null)
{
    ...
    var maxForks = concurrencyOptions.MaxConcurrentForks;
    _forkSemaphore = maxForks > 0
        ? new SemaphoreSlim(maxForks, maxForks)
        : null;  // 0 = 不限
}
```

### 2. ForkAsync 加限流

```csharp
public async Task<ForkResult> ForkAsync(ForkOptions options, CancellationToken ct = default)
{
    if (_forkSemaphore is not null)
    {
        await _forkSemaphore.WaitAsync(ct).ConfigureAwait(false);
    }
    try
    {
        // 原有 fork 逻辑
        ...
    }
    finally
    {
        _forkSemaphore?.Release();
    }
}
```

### 3. 信号量在 fork 终态时释放

fork 完成（成功/失败/取消）后释放信号量，保证终态正确释放：
- 同步 fork：`ForkAsync` 返回时释放
- 后台 fork：`RunBackgroundForkAsync` 完成时释放（需调整释放时机，后台 fork 的信号量持有时间 = fork 生命周期）

### 4. 配置来源

`MaxConcurrentForks` 来自 `SubAgentConcurrencyOptions`（ADR 0048），默认 12，`0` 表示不限。

### 5. 与 ADR 0050 的关系

ADR 0050 限制 `SpawnSubAgentAsync` 的并发；ADR 0051 限制 `ForkAsync` 的并发。fork 内部会调用 spawn，因此 fork 信号量是外层限流，spawn 信号量是内层限流，形成两层防护：
```
ForkAsync (ADR 0051, MaxConcurrentForks=12)
  └─ ForkSpawnMiddleware
       └─ SpawnSubAgentAsync (ADR 0050, MaxConcurrentSpawns=16)
```

## 替代方案

1. **依赖 ADR 0050 的 spawn 限流间接保护 fork**：放弃。fork 除了 spawn 还涉及缓存复制、后台任务长期占用、事件通道，spawn 限流不能覆盖 fork 的全部资源开销。
2. **用 `_entries.Count` 运行时检查**：放弃。`ConcurrentDictionary.Count` 是近似值，竞态条件下不可靠；且检查-then-act 不是原子操作。
3. **fork 拒绝策略（超限返回 Failed）**：放弃。fork 常用于并行任务分解，拒绝导致任务不完整；等待保证最终一致性。
4. **不限制 fork，仅限制后台 fork**：放弃。同步 fork 也占资源（管道执行），且同步/异步 fork 混合时限流逻辑复杂。

## 后果

- 正面：fork 广度受控；后台 fork 不再无限堆积；与 ADR 0050 形成两层防护
- 负面：`ForkSubAgentManager` 新增构造函数依赖；高并发 fork 场景耗时增加（排队等待）
- 中性：`MaxConcurrentForks` 默认 12，`0=不限`保留逃生口；热重载按 ADR 0015 双变量切换
