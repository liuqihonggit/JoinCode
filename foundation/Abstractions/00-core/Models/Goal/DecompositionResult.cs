
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 任务分解分析结果
/// </summary>
public sealed class DecompositionResult
{
    public bool IsDecomposable { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<SubTaskDefinition> SubTasks { get; init; } = [];

    public static DecompositionResult NotDecomposable(string reason) =>
        new() { IsDecomposable = false, Reason = reason };

    public static DecompositionResult Decomposable(string reason, IReadOnlyList<SubTaskDefinition> subTasks) =>
        new() { IsDecomposable = true, Reason = reason, SubTasks = subTasks };
}

/// <summary>
/// 子任务定义 — LLM 输出的分解结果
/// </summary>
public sealed class SubTaskDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> DependsOn { get; init; } = [];
    public List<string> OwnedFiles { get; init; } = [];
    public string Priority { get; init; } = "medium";
    public string Variant { get; init; } = "code";
}
