namespace JoinCode.Abstractions.Interfaces;

public enum WorktreeMergeStrategy
{
    Fail,
    Ours,
    Theirs,
    AutoMerge
}

public sealed class WorktreeMergeResult
{
    public required string SourceWorktreePath { get; init; }
    public required string TargetWorktreePath { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public bool HadConflicts { get; init; }
    public IReadOnlyList<string> MergedFiles { get; init; } = [];
    public IReadOnlyList<string> ConflictFiles { get; init; } = [];
    public string? StrategyUsed { get; init; }

    public static WorktreeMergeResult Success(string source, string target, IReadOnlyList<string> mergedFiles, string strategy) => new()
    {
        SourceWorktreePath = source,
        TargetWorktreePath = target,
        IsSuccess = true,
        MergedFiles = mergedFiles,
        StrategyUsed = strategy
    };

    public static WorktreeMergeResult Failed(string source, string target, string error, IReadOnlyList<string>? conflictFiles = null) => new()
    {
        SourceWorktreePath = source,
        TargetWorktreePath = target,
        IsSuccess = false,
        Error = error,
        ConflictFiles = conflictFiles ?? []
    };
}

public interface IWorktreeMergeService
{
    Task<WorktreeMergeResult> MergeToTargetAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        WorktreeMergeStrategy strategy = WorktreeMergeStrategy.Fail,
        CancellationToken cancellationToken = default);
}
