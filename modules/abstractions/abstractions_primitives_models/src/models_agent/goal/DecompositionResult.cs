namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 任务分解分析结果
/// </summary>
public sealed class DecompositionResult {
    /// <summary>获取是否可分解。</summary>
    public bool IsDecomposable { get; init; }
    /// <summary>获取原因说明。</summary>
    public string Reason { get; init; } = string.Empty;
    /// <summary>获取子任务列表。</summary>
    public IReadOnlyList<SubTaskDefinition> SubTasks { get; init; } = [];
    /// <summary>获取复杂度级别。</summary>
    public ComplexityLevel Complexity { get; init; } = ComplexityLevel.Medium;
    /// <summary>获取执行模式。</summary>
    public ExecutionMode Mode { get; init; } = ExecutionMode.PlanA;
    /// <summary>获取分解理由。</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>创建不可分解结果。</summary>
    public static DecompositionResult NotDecomposable(string reason) =>
        new() { IsDecomposable = false, Reason = reason };

    /// <summary>创建可分解结果。</summary>
    public static DecompositionResult Decomposable(string reason, IReadOnlyList<SubTaskDefinition> subTasks) =>
        new() { IsDecomposable = true, Reason = reason, SubTasks = subTasks };

    /// <summary>创建可分解结果（含复杂度）。</summary>
    public static DecompositionResult Decomposable(string reason, IReadOnlyList<SubTaskDefinition> subTasks, ComplexityLevel complexity) =>
        new() { IsDecomposable = true, Reason = reason, SubTasks = subTasks, Complexity = complexity };

    /// <summary>创建可分解结果（含复杂度、执行模式和理由）。</summary>
    public static DecompositionResult Decomposable(string reason, IReadOnlyList<SubTaskDefinition> subTasks, ComplexityLevel complexity, ExecutionMode mode, string rationale) =>
        new() { IsDecomposable = true, Reason = reason, SubTasks = subTasks, Complexity = complexity, Mode = mode, Rationale = rationale };
}

/// <summary>
/// 子任务定义 — LLM 输出的分解结果
/// </summary>
public sealed class SubTaskDefinition {
    /// <summary>获取子任务标识。</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>获取子任务标题。</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>获取子任务描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取依赖任务标识列表。</summary>
    public List<string> DependsOn { get; init; } = [];
    /// <summary>获取拥有的文件列表。</summary>
    public List<string> OwnedFiles { get; init; } = [];
    /// <summary>获取优先级。</summary>
    public SubTaskPriority Priority { get; init; } = SubTaskPriority.Medium;
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant Variant { get; init; } = ExecutorVariant.Code;
}