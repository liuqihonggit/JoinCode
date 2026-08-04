
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理结果评分器 — LLM 评分 + 规则兜底
/// </summary>
public interface ISubAgentGrader
{
    Task<GradingResult> GradeAsync(GradingContext context, CancellationToken ct = default);
}

/// <summary>
/// 评分上下文
/// </summary>
public sealed class GradingContext
{
    public required string AgentId { get; init; }
    public required string TaskDescription { get; init; }
    public required string AgentOutput { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public CheckpointResult? CheckpointResult { get; init; }
    public string? DiffSummary { get; init; }
}

/// <summary>
/// 评分结果
/// </summary>
public sealed class GradingResult
{
    public required double Score { get; init; }
    public required string Reason { get; init; }
    public GradingMethod Method { get; init; } = GradingMethod.RuleFallback;
    public IReadOnlyList<GradingCriterion> Criteria { get; init; } = [];

    public static GradingResult FromRules(double score, string reason, IReadOnlyList<GradingCriterion> criteria) =>
        new() { Score = score, Reason = reason, Method = GradingMethod.RuleFallback, Criteria = criteria };

    public static GradingResult FromLlm(double score, string reason, IReadOnlyList<GradingCriterion> criteria) =>
        new() { Score = score, Reason = reason, Method = GradingMethod.LlmEvaluation, Criteria = criteria };
}

/// <summary>
/// 评分方法
/// </summary>
public enum GradingMethod
{
    RuleFallback,
    LlmEvaluation
}

/// <summary>
/// 评分维度
/// </summary>
public sealed class GradingCriterion
{
    public required string Name { get; init; }
    public required double Score { get; init; }
    public required string Feedback { get; init; }
}
