
namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 集群执行计划 — 由 DecomposabilityAnalyzer 输出构建
/// </summary>
public sealed class ClusterPlan
{
    public required string Objective { get; init; }
    public required DecompositionResult Decomposition { get; init; }
    public required ClusterExecutionOptions ExecutionOptions { get; init; }
    public ClusterPlanValidationResult? ValidationResult { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 集群计划验证结果
/// </summary>
public sealed class ClusterPlanValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<FileConflictInfo> FileConflicts { get; init; } = [];

    public static ClusterPlanValidationResult Valid(IReadOnlyList<string> warnings, IReadOnlyList<FileConflictInfo>? conflicts = null) =>
        new() { IsValid = true, Warnings = warnings, FileConflicts = conflicts ?? [] };

    public static ClusterPlanValidationResult Invalid(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<FileConflictInfo> conflicts) =>
        new() { IsValid = false, Errors = errors, Warnings = warnings, FileConflicts = conflicts };
}

/// <summary>
/// 文件冲突信息
/// </summary>
public sealed class FileConflictInfo
{
    public required string FilePath { get; init; }
    public required IReadOnlyList<string> SubTaskIds { get; init; }
}
