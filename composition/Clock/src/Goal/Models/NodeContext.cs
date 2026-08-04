namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// FunctionNode 执行上下文 — 提供上游输出、全局状态、服务访问
/// </summary>
public sealed class NodeContext
{
    public required string NodeId { get; init; }
    public required GoalNodePayload CurrentNode { get; init; }
    public required IReadOnlyDictionary<string, string?> UpstreamOutputs { get; init; }
    public required GoalState GlobalState { get; init; }
    public required IServiceProvider Services { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    public IGoalGraphMutator? GraphMutator { get; init; }
}

/// <summary>
/// 节点执行结果
/// </summary>
public sealed class NodeResult
{
    public string? Output { get; init; }
    public string[]? Routes { get; init; }
    public string? Message { get; init; }
    public int TokensUsed { get; init; }
    public bool IsFailed { get; init; }

    public static NodeResult Succeeded(string? output, int tokensUsed = 0)
        => new() { Output = output, TokensUsed = tokensUsed };

    public static NodeResult Routed(string? output, string[] routes, int tokensUsed = 0)
        => new() { Output = output, Routes = routes, TokensUsed = tokensUsed };

    public static NodeResult Failed(string errorMessage, int tokensUsed = 0)
        => new() { Output = null, TokensUsed = tokensUsed, Message = errorMessage, IsFailed = true };
}
