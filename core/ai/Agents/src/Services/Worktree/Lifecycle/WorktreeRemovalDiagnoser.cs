namespace Core.Agents.Worktree;

/// <summary>
/// worktree 删除状态机 — 显式状态枚举驱动，删除失败时转入 Diagnosing 探测根因。
/// <para>对齐 AGENTS.md 规则8：显式状态枚举 + switch 表达式实现状态转换，结果携带 State 字段。</para>
/// </summary>
public enum RemovalState
{
    /// <summary>初始状态</summary>
    Idle,
    /// <summary>正在删除</summary>
    InProgress,
    /// <summary>删除成功</summary>
    Succeeded,
    /// <summary>删除失败</summary>
    Failed,
    /// <summary>正在诊断失败根因</summary>
    Diagnosing,
    /// <summary>诊断完成，根因已确定</summary>
    Diagnosed
}

/// <summary>
/// worktree 删除失败根因 — 诊断后暴露给调用方决策。
/// </summary>
public enum RemovalFailureReason
{
    /// <summary>无失败</summary>
    None,
    /// <summary>路径不存在（worktree 已被删除或从未创建）</summary>
    PathNotFound,
    /// <summary>进程占用（编辑器/终端打开了 worktree 目录）</summary>
    ProcessOccupied,
    /// <summary>权限不足（无法删除目录）</summary>
    PermissionDenied,
    /// <summary>git 命令失败（非上述已知原因）</summary>
    GitError,
    /// <summary>未知原因</summary>
    Unknown
}

/// <summary>
/// worktree 删除结果 — 携带状态机状态 + 失败根因 + 错误信息。
/// </summary>
public sealed record WorktreeRemovalResult
{
    public required RemovalState State { get; init; }
    public RemovalFailureReason Reason { get; init; } = RemovalFailureReason.None;
    public string? ErrorMessage { get; init; }
    public bool Success => State == RemovalState.Succeeded;

    public static WorktreeRemovalResult SucceededResult() => new() { State = RemovalState.Succeeded };
    public static WorktreeRemovalResult FailedResult(string error) => new() { State = RemovalState.Failed, ErrorMessage = error };
    public static WorktreeRemovalResult DiagnosedResult(RemovalFailureReason reason, string? error = null) =>
        new() { State = RemovalState.Diagnosed, Reason = reason, ErrorMessage = error };
}

/// <summary>
/// worktree 删除失败诊断器 — 路径存在性 + 错误消息模式匹配，确定失败根因。
/// <para>不盲目兜底，把根因暴露给调用方决策（如提示用户关闭编辑器、检查权限）。</para>
/// </summary>
public sealed class WorktreeRemovalDiagnoser
{
    private readonly IFileOperationService _fileSystem;

    public WorktreeRemovalDiagnoser(IFileOperationService fileSystem) => _fileSystem = fileSystem;

    /// <summary>
    /// 诊断删除失败根因 — 路径不存在→PathNotFound，错误消息模式匹配→ProcessOccupied/PermissionDenied，否则→GitError/Unknown。
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="errorMessage">git 命令错误消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<WorktreeRemovalResult> DiagnoseAsync(
        string worktreePath,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var exists = await _fileSystem.DirectoryExistsAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return WorktreeRemovalResult.DiagnosedResult(RemovalFailureReason.PathNotFound, errorMessage);
        }

        var reason = ClassifyError(errorMessage);
        return WorktreeRemovalResult.DiagnosedResult(reason, errorMessage);
    }

    /// <summary>
    /// 错误消息分类 — 转小写后单次模式匹配，对齐"字符串匹配优先转小写"规范。
    /// </summary>
    private static RemovalFailureReason ClassifyError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return RemovalFailureReason.Unknown;
        }

        var lower = errorMessage.ToLowerInvariant();
        return lower switch
        {
            _ when lower.Contains("being used") || lower.Contains("occupied") || lower.Contains("busy") =>
                RemovalFailureReason.ProcessOccupied,
            _ when lower.Contains("permission denied") || lower.Contains("access is denied") || lower.Contains("unauthorized") =>
                RemovalFailureReason.PermissionDenied,
            _ => RemovalFailureReason.GitError
        };
    }
}
