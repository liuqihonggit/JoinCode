namespace Core.Goal;

/// <summary>
/// 目标状态更新器 — 独立类，封装 StateLock 保护的 goalState/chatHistory 写操作。
/// <para>从 GoalGraphEngine 提取，消除 Engine 对锁的直接依赖。</para>
/// <para>职责单一：只做 StateLock 临界区内的快速赋值，不执行任何外部 IO。</para>
/// </summary>
internal sealed class GoalStateUpdater {
    private readonly IClockService _clock;

    /// <summary>初始化目标状态更新器</summary>
    /// <param name="clock">时钟服务，用于 AchievedAt 时间戳</param>
    public GoalStateUpdater(IClockService clock) {
        _clock = clock;
    }

    /// <summary>设置目标最终状态（Achieved/Unmet），在 StateLock 保护下更新 Status 和 AchievedAt</summary>
    /// <param name="goalState">目标状态</param>
    /// <param name="status">目标最终状态</param>
    /// <param name="context">图执行上下文（提供 StateLock）</param>
    /// <param name="ct">取消令牌</param>
    public async Task SetGoalStatusAsync(GoalState goalState, GoalStatus status, GraphExecutionContext context, CancellationToken ct) {
        var lk = context.StateLock;
        using var guard = await lk.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        goalState.Status = status;
        goalState.AchievedAt = _clock.GetUtcNow();
    }

    /// <summary>更新目标累计 token 和轮次统计，在 StateLock 保护下写入 State</summary>
    /// <param name="context">图执行上下文</param>
    public async Task UpdateGoalStateAsync(GraphExecutionContext context) {
        var totalTokens = 0;
        var totalTurns = 0;
        foreach (var node in context.Graph.Dag.Nodes.Values) {
            totalTokens += node.Payload.TokensUsed;
            if (node.Payload.Status == GoalNodeStatus.Completed)
                totalTurns++;
        }

        var lk = context.StateLock;
        using var guard = await lk.TryLockAsync().ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        context.State.TokensUsed = totalTokens;
        context.State.TurnsCompleted = totalTurns;
    }

    /// <summary>在 StateLock 保护下向聊天历史追加助手消息</summary>
    /// <param name="context">图执行上下文</param>
    /// <param name="message">助手消息文本</param>
    /// <param name="ct">取消令牌</param>
    public async Task AppendChatMessageAsync(GraphExecutionContext context, string message, CancellationToken ct) {
        var lk = context.StateLock;
        using var guard = await lk.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        context.ChatHistory.AddAssistantMessage(message);
    }
}