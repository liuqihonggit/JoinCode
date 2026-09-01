# 0050. spawn 阶段 SemaphoreSlim 限流

- 状态：proposed
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0048](docs/adr/0048-subagent-concurrency-unified-config.md)
- 验证：待实现后补充

## 背景

子代理 spawn 阶段（创建子代理）当前**无限流**，存在资源压垮风险：

### 1. 批量 spawn 无限流

```csharp
// AgentCoordinator.SpawnSubAgentsAsync (AgentCoordinator.cs:101)
public async Task<IReadOnlyList<IAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, ...)
{
    var spawnTasks = taskList
        .Select(task => SpawnSubAgentAsync(task, options, cancellationToken))
        .ToList();
    var agents = await Task.WhenAll(spawnTasks).ConfigureAwait(false);  // ❌ 全部并行
    return agents.ToList();
}
```

`Task.WhenAll(spawnTasks)` 一次性创建所有子代理，无并发限制。若 `tasks` 有 100 个，则同时创建 100 个子代理。

### 2. Unified Spawn 管道无限流

`LifecycleSpawnMiddleware` → `AgentLifecycleManager.SpawnSubAgentAsync` → `AgentFactory.Create` 直接创建，无 SemaphoreSlim。

### 3. spawn 阶段的资源开销

spawn 不仅是内存对象创建，还涉及：
- **Worktree 创建**（`WorktreeSpawnMiddleware`）— `git worktree add` 涉及磁盘 I/O + git 进程 fork
- **状态机注册**（`AgentStateMachine.RegisterAgent`）
- **会话目录创建**（`subagents/{agentId}/`）

100 个并发 worktree 创建会压垮磁盘 I/O 和 git 锁。

### 4. execute 阶段有限流但 spawn 没有

`AgentExecutionEngine.ExecuteParallelAsync` 用 `SemaphoreSlim(maxConcurrency)` 限流 execute，但 spawn 在 execute 之前，spawn 无限流 = execute 限流前资源已被 spawn 阶段压垮。

## 决策

**在 spawn 阶段引入 `SemaphoreSlim` 限流**，限制同时创建子代理的数量。

### 1. AgentCoordinator 加 spawn 信号量

```csharp
// AgentCoordinator.cs
private readonly SemaphoreSlim _spawnSemaphore;

public AgentCoordinator(..., SubAgentConcurrencyOptions concurrencyOptions, ...)
{
    _spawnSemaphore = new SemaphoreSlim(
        Math.Max(1, concurrencyOptions.MaxConcurrentSpawns),
        Math.Max(1, concurrencyOptions.MaxConcurrentSpawns));
}

public async Task<IAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, ...)
{
    await _spawnSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        // 原有 spawn 管道逻辑
        var ctx = new UnifiedSpawnContext { ... };
        await _spawnPipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        ...
        return ctx.Agent ?? throw new InvalidOperationException("Spawn pipeline completed without agent");
    }
    finally
    {
        _spawnSemaphore.Release();
    }
}
```

### 2. SpawnSubAgentsAsync 自然受限

`SpawnSubAgentsAsync` 内部调用 `SpawnSubAgentAsync`，每个调用都经过 `_spawnSemaphore`，`Task.WhenAll` 自然被节流，无需额外改造。

### 3. 配置来源

`MaxConcurrentSpawns` 来自 `SubAgentConcurrencyOptions`（ADR 0048），默认 16。

### 4. 热重载

按 ADR 0015 双变量切换，`MaxConcurrentSpawns` 变更时重建 `_spawnSemaphore`（用 `Interlocked.Exchange` 原子交换）。

### 5. 拒绝排队策略

spawn 超过上限时**等待**（`WaitAsync`），不拒绝。原因：spawn 是批量调度的前置步骤，拒绝会导致任务丢失；等待保证最终一致性。

## 替代方案

1. **仅 execute 阶段限流（现状）**：放弃。spawn 阶段 worktree 创建压垮磁盘，execute 限流前资源已耗尽。
2. **用 `Parallel.ForEachAsync` + `MaxDegreeOfParallelism`**：放弃。`SpawnSubAgentsAsync` 是 `Task.WhenAll` 模式，改 `Parallel.ForEachAsync` 需重构返回值聚合，且 `Parallel.ForEachAsync` 不保证顺序。
3. **用 `Channel<T>` 生产者-消费者**：放弃。spawn 是一次性批量操作，不是流式生产消费，Channel 过度设计。
4. **spawn 拒绝策略（超限抛异常）**：放弃。批量调度依赖 spawn 全部成功，拒绝导致任务丢失。

## 后果

- 正面：spawn 阶段资源受控；worktree 创建不再压垮磁盘；spawn+execute 双阶段限流形成纵深防御
- 负面：`AgentCoordinator` 新增 `SemaphoreSlim` 字段和构造函数依赖；批量 spawn 耗时增加（排队等待）
- 中性：`MaxConcurrentSpawns` 默认 16，可通过 `settings.json` 热重载调整
