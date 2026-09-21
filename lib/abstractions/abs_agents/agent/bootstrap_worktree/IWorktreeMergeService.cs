namespace JoinCode.Abstractions.Interfaces;

public enum WorktreeMergeStrategy {
    [EnumValue("fail")]
    Fail,
    [EnumValue("ours")]
    Ours,
    [EnumValue("theirs")]
    Theirs,
    [EnumValue("auto_merge")]
    AutoMerge
}

public sealed class WorktreeMergeResult {
    /// <summary>获取源工作树路径。</summary>
    public required string SourceWorktreePath { get; init; }
    /// <summary>获取目标工作树路径。</summary>
    public required string TargetWorktreePath { get; init; }
    /// <summary>获取是否成功。</summary>
    public bool IsSuccess { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? Error { get; init; }
    /// <summary>获取是否存在冲突。</summary>
    public bool HadConflicts { get; init; }
    /// <summary>获取已合并文件列表。</summary>
    public IReadOnlyList<string> MergedFiles { get; init; } = [];
    /// <summary>获取冲突文件列表。</summary>
    public IReadOnlyList<string> ConflictFiles { get; init; } = [];
    /// <summary>获取实际使用的合并策略。</summary>
    public string? StrategyUsed { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="source">源工作树路径。</param>
    /// <param name="target">目标工作树路径。</param>
    /// <param name="mergedFiles">已合并文件列表。</param>
    /// <param name="strategy">使用的策略。</param>
    public static WorktreeMergeResult Success(string source, string target, IReadOnlyList<string> mergedFiles, string strategy) => new() {
        SourceWorktreePath = source,
        TargetWorktreePath = target,
        IsSuccess = true,
        MergedFiles = mergedFiles,
        StrategyUsed = strategy
    };

    /// <summary>构造失败结果。</summary>
    /// <param name="source">源工作树路径。</param>
    /// <param name="target">目标工作树路径。</param>
    /// <param name="error">错误消息。</param>
    /// <param name="conflictFiles">冲突文件列表。</param>
    public static WorktreeMergeResult Failed(string source, string target, string error, IReadOnlyList<string>? conflictFiles = null) => new() {
        SourceWorktreePath = source,
        TargetWorktreePath = target,
        IsSuccess = false,
        Error = error,
        ConflictFiles = conflictFiles ?? []
    };
}

public interface IWorktreeMergeService {
    /// <summary>异步将源工作树合并到目标工作树。</summary>
    /// <param name="sourceWorktreePath">源工作树路径。</param>
    /// <param name="targetWorktreePath">目标工作树路径。</param>
    /// <param name="strategy">合并策略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorktreeMergeResult> MergeToTargetAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        WorktreeMergeStrategy strategy = WorktreeMergeStrategy.Fail,
        CancellationToken cancellationToken = default);
}