# 子代理卡死防护纵深防御 — 技术设计

> 📍 **导航**: [docs/](../README.md) › [design/](README.md)
> 🔗 **ADR**: [0106](../adr/0106-subagent-stall-defense-in-depth.md)

## 一、整体架构

```
                    ┌─────────────────────────────────────────┐
                    │  L1 预防：工具超时（已有，补全接线）       │
                    │  AgentTimeoutMiddleware（新建中间件）      │
                    └─────────────────────────────────────────┘
                                    │
                                    ▼
        ┌───────────────────────────────────────────────────────┐
        │  L2 检测                                               │
        │  ├─ 死循环输出：InformationEntropyGuardian（已有复用）  │
        │  ├─ 完全无输出：SubAgentIdleDetector（新建）            │
        │  ├─ 80%巡查：SubAgentLivenessScanner（新建）            │
        │  └─ 链路聚合：SubAgentChainStallDetector（新建）        │
        └───────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    ▼                               ▼
        ┌───────────────────────┐       ┌───────────────────────┐
        │ L3 干预：激活           │       │ L3 干预：抢塞新任务     │
        │ SubAgentActivator      │       │ SubAgentPool           │
        │ （注入提示+Resume）     │       │ PreemptiveScheduler    │
        └───────────────────────┘       └───────────────────────┘
                    │
                    ▼（干预无效）
        ┌───────────────────────────────────────────────────────┐
        │  L4 恢复：渐进式压缩                                     │
        │  ProgressiveCompactor（新建编排器）                      │
        │  Light → Aggressive → ExitWithSummary                  │
        │  复用 IChatContextManager.FoldIfNeededAsync(agentId)    │
        └───────────────────────────────────────────────────────┘
```

## 二、状态机设计

### 2.1 SubAgentIdleDetector 状态机

复用 `ShannonEntropyDetector` 的 FSM 模式（ADR 0040）。

```
状态枚举：SubAgentLivenessState : byte
{
    None      = 0,
    Monitoring = 1,   // 正常监控中
    Suspected  = 2,   // 疑似卡死（等待二次确认）
    Confirmed  = 4,   // 确认卡死
    Recovered  = 8    // 已恢复（瞬态，自动回 Monitoring）
}

事件枚举：SubAgentLivenessEvent : byte
{
    Idle,      // 检测到无活动（LastActivityAt 超过阈值 且 无孙代理）
    Active,    // 检测到有活动（上下文变化 或 有孙代理）
    Confirm,   // 二次确认窗口内再次 Idle
    Timeout,   // 二次确认窗口超时（误报消除）
    Recover,   // 干预后恢复活动
    Reset      // 手动重置
}

状态转换表：
  Monitoring ──Idle──→ Suspected
  Suspected  ──Confirm──→ Confirmed
  Suspected  ──Timeout──→ Monitoring   （误报消除）
  Suspected  ──Active──→ Monitoring    （恢复活动）
  Confirmed  ──Recover──→ Monitoring   （干预成功）
  Confirmed  ──Idle──→ Confirmed       （干预无效，保持确认）
  *          ──Reset──→ Monitoring
```

### 2.2 触发条件表

| 检测器 | 触发条件 | 动作 | 配置项 |
|--------|----------|------|--------|
| **InformationEntropyGuardian**（已有） | 输出文本尾部重复 / 逻辑指纹循环 / 工具调用序列循环 / Shannon熵持续下降 | 发射 `LoopDetected` 事件 → L3激活 → L4压缩 | `LoopInterventionOptions` 各子配置 |
| **SubAgentIdleDetector**（新建） | `Now - agent.LastActivityAt > IdleThresholdSeconds` 且 `无孙代理(ParentSessionId链无Running)` | 状态机 `Monitoring→Suspected→Confirmed` | `IdleThresholdSeconds=30`, `ConfirmationWindowSeconds=5` |
| **SubAgentLivenessScanner**（新建） | `完成率 = CompletedCount/TotalAgents ≥ CompletionCheckThreshold` | 触发一次全量巡查所有 Running 子代理 | `CompletionCheckThreshold=0.8` |
| **SubAgentChainStallDetector**（新建） | 链上所有节点都 `Confirmed` | 触发链路告警 → L4 压缩 | `ChainStallThreshold=3` |

### 2.3 干预决策表

| 检测结果 | 干预动作 | 后续 |
|----------|----------|------|
| 死循环输出（InformationEntropyGuardian） | 直接触发 L4 渐进式压缩 | Light→Aggressive→ExitWithSummary |
| 完全无输出 + Suspected | 不干预，等待二次确认 | 窗口内仍 Idle → Confirmed |
| 完全无输出 + Confirmed | 注入催促提示 + `ResumeAgentAsync` | 5s 内恢复活动 → Recovered；否则 → L4 |
| 子代理已完成 | 回池等待抢塞 | 新任务到来 → 抢塞复用上下文 |
| 链路全卡死 | L4 压缩链上所有 Confirmed 节点 | 从叶子节点开始压缩 |

## 三、类设计

### 3.1 配置类

```csharp
// 文件：lib/abstractions/abs_agents/agent/SubAgentLivenessOptions.cs
public sealed partial class SubAgentLivenessOptions : ServiceEntity
{
    /// <summary>完全无输出超时阈值（秒），默认 30</summary>
    public int IdleThresholdSeconds { get; set; } = 30;

    /// <summary>80% 巡查触发阈值，默认 0.8</summary>
    public double CompletionCheckThreshold { get; set; } = 0.8;

    /// <summary>二次确认时间窗口（秒），默认 5</summary>
    public int ConfirmationWindowSeconds { get; set; } = 5;

    /// <summary>定时扫描间隔（秒），默认 10</summary>
    public int ScanIntervalSeconds { get; set; } = 10;

    /// <summary>链路卡死节点数阈值，默认 3</summary>
    public int ChainStallThreshold { get; set; } = 3;

    /// <summary>代理池最大保留数，默认 8</summary>
    public int PoolMaxSize { get; set; } = 8;

    /// <summary>代理池空闲超时（秒），超时后真正 Dispose，默认 300</summary>
    public int PoolIdleTimeoutSeconds { get; set; } = 300;

    /// <summary>抢塞新任务时上下文窗口剩余比例阈值，低于此值拒绝抢塞，默认 0.2</summary>
    public double PreemptMinWindowRatio { get; set; } = 0.2;
}
```

### 3.2 SubAgentIdleDetector

```csharp
// 文件：llm/agents/Coordinator/Core/Liveness/SubAgentIdleDetector.cs
public sealed partial class SubAgentIdleDetector : ServiceEntity
{
    // 状态机（复用 FsmStateMachine 源码生成器模式）
    [FsmStateMachine(typeof(SubAgentLivenessState), typeof(SubAgentLivenessEvent), SubAgentLivenessState.Monitoring)]
    // ... Transition 特性声明转换表

    private SubAgentLivenessState _state = SubAgentLivenessState.Monitoring;
    private DateTimeOffset _suspectedAt;        // 进入 Suspected 的时刻
    private readonly Func<DateTimeOffset> _clock; // 时钟注入（测试可控）
    private readonly SubAgentLivenessOptions _options;

    /// <summary>记录一次活动检测（由 Scanner 定时调用）</summary>
    public SubAgentLivenessEvent Record(bool isIdle, bool hasGrandchildren)
    {
        var actuallyIdle = isIdle && !hasGrandchildren;
        var evt = actuallyIdle ? SubAgentLivenessEvent.Idle : SubAgentLivenessEvent.Active;

        if (_state == SubAgentLivenessState.Suspected)
        {
            // 二次确认窗口检查
            var elapsed = _clock() - _suspectedAt;
            if (elapsed.TotalSeconds > _options.ConfirmationWindowSeconds)
                evt = actuallyIdle ? SubAgentLivenessEvent.Confirm : SubAgentLivenessEvent.Timeout;
        }

        _state = _fsm.Transition(_state, evt);
        if (_state == SubAgentLivenessState.Suspected && evt == SubAgentLivenessEvent.Idle)
            _suspectedAt = _clock();

        return evt;
    }

    public SubAgentLivenessState State => _state;
    public bool IsConfirmed => _state == SubAgentLivenessState.Confirmed;
    public void Reset() => _state = SubAgentLivenessState.Monitoring;
}
```

### 3.3 SubAgentLivenessScanner

```csharp
// 文件：llm/agents/Coordinator/Core/Liveness/SubAgentLivenessScanner.cs
public sealed partial class SubAgentLivenessScanner : ServiceEntity, IAsyncDisposable
{
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IForkSubAgentManager _forkManager;
    private readonly SubAgentLivenessOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, SubAgentIdleDetector> _detectors = new();
    private readonly Timer _scanTimer;
    private bool _milestoneScanTriggered;  // 80% 巡查是否已触发
    private int _totalAgents;
    private int _completedAgents;

    // 订阅状态变更事件（在构造时）
    // _lifecycleManager.StateChanged += OnStateChanged;

    private void OnStateChanged(object? sender, AgentStateContext context)
    {
        // 更新完成计数
        Interlocked.Exchange(ref _totalAgents, context.TotalAgents);
        Interlocked.Exchange(ref _completedAgents, context.CompletedCount);

        // 80% 里程碑巡查
        var completionRate = (double)_completedAgents / Math.Max(1, _totalAgents);
        if (!_milestoneScanTriggered && completionRate >= _options.CompletionCheckThreshold)
        {
            _milestoneScanTriggered = true;
            _ = ScanAllAsync();  // 触发一次全量巡查
        }
    }

    private async Task ScanAllAsync()
    {
        foreach (var agent in _lifecycleManager.GetActiveAgents())
        {
            var detector = _detectors.GetOrAdd(agent.ObjectId.UniqueId,
                _ => new SubAgentIdleDetector(_options, _clock));

            var isIdle = (_clock() - agent.LastActivityAt).TotalSeconds > _options.IdleThresholdSeconds;
            var hasGrandchildren = _forkManager.HasActiveDescendants(agent.SessionId);
            var evt = detector.Record(isIdle, hasGrandchildren);

            if (detector.IsConfirmed)
            {
                // 触发 L3 激活
                await _activator.ActivateAsync(agent.ObjectId.UniqueId);
            }
        }
    }
}
```

### 3.4 SubAgentChainStallDetector

```csharp
// 文件：llm/agents/Coordinator/Core/Liveness/SubAgentChainStallDetector.cs
public sealed partial class SubAgentChainStallDetector : ServiceEntity
{
    /// <summary>沿 ParentSessionId 链聚合判定是否全卡死</summary>
    public ChainStallResult CheckChain(string agentId, IReadOnlyDictionary<string, SubAgentIdleDetector> detectors)
    {
        var chain = BuildChain(agentId);  // 沿 ParentSessionId 回溯
        var confirmedCount = chain.Count(id => detectors.TryGetValue(id, out var d) && d.IsConfirmed);

        return new ChainStallResult(
            Chain: chain,
            TotalNodes: chain.Count,
            ConfirmedNodes: confirmedCount,
            IsChainStalled: confirmedCount >= _options.ChainStallThreshold && confirmedCount == chain.Count);
    }

    private List<string> BuildChain(string agentId)
    {
        var chain = new List<string> { agentId };
        var current = agentId;
        for (var i = 0; i < 100; i++)  // 硬上限 100 层
        {
            var parent = _forkManager.GetParentSessionId(current);
            if (parent is null) break;
            chain.Add(parent);
            current = parent;
        }
        return chain;
    }
}
```

### 3.5 SubAgentPool

```csharp
// 文件：llm/agents/Coordinator/Core/Pool/SubAgentPool.cs
public sealed partial class SubAgentPool : ServiceEntity, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PooledAgent> _pool = new();
    private readonly SubAgentLivenessOptions _options;
    private readonly Timer _cleanupTimer;

    /// <summary>子代理完成后回池（替代直接 Dispose）</summary>
    public void Return(string agentId, AgentBase agent)
    {
        if (_pool.Count >= _options.PoolMaxSize)
        {
            agent.Dispose();  // 池满则直接销毁
            return;
        }
        _pool[agentId] = new PooledAgent(agent, _clock());
    }

    /// <summary>抢塞新任务：从池中找上下文最匹配的已完成子代理</summary>
    public AgentBase? TryAcquire(string taskDescription)
    {
        var candidate = _pool.Values
            .Where(p => p.Agent.Status == AgentStatus.Completed)
            .OrderByDescending(p => ContextRelevance(p.Agent, taskDescription))
            .FirstOrDefault();

        if (candidate is not null && _pool.TryRemove(candidate.Agent.ObjectId.UniqueId, out _))
            return candidate.Agent;
        return null;
    }

    /// <summary>空闲超时清理</summary>
    private void OnCleanupTick()
    {
        var now = _clock();
        foreach (var (id, pooled) in _pool)
        {
            if ((now - pooled.ReturnedAt).TotalSeconds > _options.PoolIdleTimeoutSeconds)
            {
                if (_pool.TryRemove(id, out var removed))
                    removed.Agent.Dispose();
            }
        }
    }
}
```

### 3.6 PreemptiveScheduler

```csharp
// 文件：llm/agents/Coordinator/Core/Pool/PreemptiveScheduler.cs
public sealed partial class PreemptiveScheduler : ServiceEntity
{
    private readonly SubAgentPool _pool;
    private readonly IChatContextManager _contextManager;
    private readonly SubAgentLivenessOptions _options;

    /// <summary>抢塞新任务到已完成的子代理</summary>
    public async Task<PreemptResult> TryPreemptAsync(string taskDescription, CancellationToken ct)
    {
        var agent = _pool.TryAcquire(taskDescription);
        if (agent is null)
            return PreemptResult.NoAvailableAgent();

        // 检查上下文窗口剩余空间
        var usage = _contextManager.GetTokenUsage(agent.SessionId);
        var remainingRatio = 1.0 - (double)usage.TotalTokens / usage.ContextWindow;
        if (remainingRatio < _options.PreemptMinWindowRatio)
        {
            // 窗口不足，先压缩再抢塞
            await _contextManager.FoldIfNeededAsync(ContextFoldDecision.FoldNormal, agent.SessionId, ct);
        }

        // 注入新任务 prompt（接在旧上下文之后，前缀不变 → KV cache 命中）
        agent.ChatHistory.AddUserMessage($"[新任务] {taskDescription}");
        agent.Status = AgentStatus.Running;

        return PreemptResult.Success(agent);
    }
}
```

### 3.7 ProgressiveCompactor（L4 渐进式压缩编排器）

```csharp
// 文件：llm/agents/Coordinator/Core/Liveness/ProgressiveCompactor.cs
public sealed partial class ProgressiveCompactor : ServiceEntity
{
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<ProgressiveCompactor> _logger;

    /// <summary>渐进式压缩：Light → Aggressive → ExitWithSummary</summary>
    public async Task<CompactionResult> CompactProgressiveAsync(string agentId, CancellationToken ct)
    {
        // Level 1: Light 压缩
        var lightResult = await TryCompactAsync(agentId, ContextFoldDecision.FoldNormal, ct);
        if (lightResult.IsSuccessful)
            return CompactionResult.Light(lightResult);

        _logger.LogWarning("Light 压缩失败，升级到 Aggressive: {AgentId}", agentId);

        // Level 2: Aggressive 压缩
        var aggressiveResult = await TryCompactAsync(agentId, ContextFoldDecision.FoldAggressive, ct);
        if (aggressiveResult.IsSuccessful)
            return CompactionResult.Aggressive(aggressiveResult);

        _logger.LogWarning("Aggressive 压缩失败，升级到 ExitWithSummary: {AgentId}", agentId);

        // Level 3: ExitWithSummary
        var summary = await _contextManager.GenerateSummaryAsync(agentId, ct);
        return CompactionResult.ExitWithSummary(summary);
    }

    private async Task<FoldResult> TryCompactAsync(string agentId, ContextFoldDecision decision, CancellationToken ct)
    {
        return await _contextManager.FoldIfNeededAsync(decision, agentId, ct);
    }
}
```

## 四、交互流程

### 4.1 正常流程（无卡死）

```
子代理执行中
  → 每次工具调用/输出 → Entity.Touch() 刷新 LastActivityAt
  → InformationEntropyGuardian 检测输出（无循环）
  → SubAgentIdleDetector.Record(isIdle: false) → 保持 Monitoring
  → 任务完成 → SubAgentPool.Return() 回池
  → 新任务到来 → PreemptiveScheduler.TryPreemptAsync() 抢塞
```

### 4.2 死循环输出流程

```
子代理执行中
  → InformationEntropyGuardian 检测到输出循环
  → 发射 LoopDetected 事件
  → LoopInterventionMiddleware 拦截
  → ProgressiveCompactor.CompactProgressiveAsync(agentId)
    → Light 压缩成功 → 恢复执行
    → Light 失败 → Aggressive 压缩
    → Aggressive 失败 → ExitWithSummary
```

### 4.3 完全无输出流程

```
子代理执行中
  → 30s 无活动（欠费/网络中断）
  → SubAgentLivenessScanner 定时扫描（10s 间隔）
  → SubAgentIdleDetector.Record(isIdle: true, hasGrandchildren: false)
  → Monitoring → Suspected（记录 _suspectedAt）
  → 5s 后再次扫描仍 Idle → Suspected → Confirmed
  → SubAgentActivator.ActivateAsync(agentId)
    → 注入催促提示："检测到 30s 无活动，请继续任务或报告阻塞原因"
    → ResumeAgentAsync(agentId)
  → 5s 内恢复活动 → Confirmed → Monitoring（Recover 事件）
  → 5s 内仍无活动 → ProgressiveCompactor.CompactProgressiveAsync(agentId)
```

### 4.4 链路全卡死流程

```
孙代理卡死 → 子代理等待孙代理 → 子代理也卡死
  → SubAgentLivenessScanner 扫描
  → SubAgentIdleDetector 检测到子代理 Idle，但有孙代理 → 不触发（hasGrandchildren=true）
  → SubAgentChainStallDetector.CheckChain(子代理Id)
    → BuildChain 沿 ParentSessionId 回溯
    → 链上所有节点都 Confirmed → IsChainStalled=true
  → ProgressiveCompactor.CompactProgressiveAsync(链上每个 Confirmed 节点)
```

### 4.5 抢塞新任务流程

```
子代理 A 完成任务 → SubAgentPool.Return(A)
  → 新任务 B 到来
  → PreemptiveScheduler.TryPreemptAsync(B.description)
    → SubAgentPool.TryAcquire(B.description)
      → 找到上下文最匹配的 A（已完成，ContextRelevance 最高）
    → 检查 A 的窗口剩余空间
      → 充足 → 直接抢塞
      → 不足 → 先 FoldIfNeededAsync(FoldNormal) 压缩再抢塞
    → A.ChatHistory.AddUserMessage("[新任务] B.description")
    → A.Status = Running
    → 前缀不变 → KV cache 命中 → CacheReadInputTokens > 0
  → A 继续执行新任务 B
```

## 五、文件清单

| 文件 | 层级 | 职责 |
|------|------|------|
| `lib/abstractions/abs_agents/agent/SubAgentLivenessOptions.cs` | L2 配置 | 配置类 |
| `llm/agents/Coordinator/Core/Liveness/SubAgentIdleDetector.cs` | L2 检测 | 完全无输出检测器 |
| `llm/agents/Coordinator/Core/Liveness/SubAgentLivenessScanner.cs` | L2 检测 | 后台扫描器+80%巡查 |
| `llm/agents/Coordinator/Core/Liveness/SubAgentChainStallDetector.cs` | L2 检测 | 链路聚合判定 |
| `llm/agents/Coordinator/Core/Liveness/SubAgentActivator.cs` | L3 干预 | 激活动作 |
| `llm/agents/Coordinator/Core/Liveness/ProgressiveCompactor.cs` | L4 恢复 | 渐进式压缩编排 |
| `llm/agents/Coordinator/Core/Pool/SubAgentPool.cs` | L3 干预 | 代理池 |
| `llm/agents/Coordinator/Core/Pool/PreemptiveScheduler.cs` | L3 干预 | 抢占式调度器 |
| `llm/agents/Coordinator/Core/Liveness/AgentTimeoutMiddleware.cs` | L1 预防 | 子代理超时中间件 |

## 六、测试计划

| 测试 | 层级 | 验收标准 |
|------|------|----------|
| `SubAgentIdleDetectorTests` | L2 | Monitoring→Suspected→Confirmed 状态转换 + 时间窗口二次确认 + 误报消除 |
| `SubAgentLivenessScannerTests` | L2 | 80% 触发巡查 + 定时扫描 + 事件订阅 |
| `SubAgentChainStallDetectorTests` | L2 | 链路构建 + 全卡死判定 |
| `SubAgentPoolTests` | L3 | 回池+获取+空闲超时清理+池满销毁 |
| `PreemptiveSchedulerTests` | L3 | 抢塞+窗口不足压缩+前缀缓存保护 |
| `ProgressiveCompactorTests` | L4 | Light→Aggressive→ExitWithSummary 三级 escalation |
| `AgentTimeoutMiddlewareTests` | L1 | 超时取消+配置覆盖 |
| `SubAgentStallDefenseIntegrationTests` | 集成 | 端到端：卡死→检测→激活→压缩→恢复 |

<!-- 🤖 Auto Decision: 2026-09-15 -->
<!-- 决策: 采用四层纵深防御体系,L2双路检测(复用InformationEntropyGuardian+新建SubAgentIdleDetector),L3抢塞需新建代理池 -->
<!-- 原因: 用户要求本期一起做需求5,且卡死根因分两类(死循环输出+完全无输出)需不同检测器 -->
<!-- 替代方案: 仅复用InformationEntropyGuardian不建IdleDetector(无法检测完全无输出) -->
<!-- 验证: 待编译验证 -->
