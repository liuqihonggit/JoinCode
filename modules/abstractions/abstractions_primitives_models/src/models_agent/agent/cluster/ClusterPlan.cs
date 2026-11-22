
namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 集群执行计划 — 由 DecomposabilityAnalyzer 输出构建
/// </summary>
public sealed class ClusterPlan {
    /// <summary>获取执行目标。</summary>
    public required string Objective { get; init; }
    /// <summary>获取任务分解结果。</summary>
    public required DecompositionResult Decomposition { get; init; }
    /// <summary>获取集群执行选项。</summary>
    public required ClusterExecutionOptions ExecutionOptions { get; init; }
    /// <summary>获取或设置验证结果。</summary>
    public ClusterPlanValidationResult? ValidationResult { get; set; }
    /// <summary>获取创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 集群计划验证结果
/// </summary>
public sealed class ClusterPlanValidationResult {
    /// <summary>获取是否有效。</summary>
    public bool IsValid { get; init; }
    /// <summary>获取错误信息列表。</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
    /// <summary>获取警告信息列表。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>获取文件冲突信息列表。</summary>
    public IReadOnlyList<FileConflictInfo> FileConflicts { get; init; } = [];

    /// <summary>创建有效验证结果。</summary>
    public static ClusterPlanValidationResult Valid(IReadOnlyList<string> warnings, IReadOnlyList<FileConflictInfo>? conflicts = null) =>
        new() { IsValid = true, Warnings = warnings, FileConflicts = conflicts ?? [] };

    /// <summary>创建无效验证结果。</summary>
    public static ClusterPlanValidationResult Invalid(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<FileConflictInfo> conflicts) =>
        new() { IsValid = false, Errors = errors, Warnings = warnings, FileConflicts = conflicts };
}

/// <summary>
/// 文件冲突信息
/// </summary>
public sealed class FileConflictInfo {
    /// <summary>获取冲突文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取涉及冲突的子任务 ID 列表。</summary>
    public required IReadOnlyList<string> SubTaskIds { get; init; }
}
