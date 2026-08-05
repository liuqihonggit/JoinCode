namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Git 命令执行结果
/// </summary>
public sealed class GitCommandResult
{
    public required bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}

/// <summary>
/// 合并冲突检测结果 — 基于 git merge-tree --write-tree（只读，不污染工作区）
/// </summary>
public sealed class MergeConflictResult
{
    public required bool HasConflict { get; init; }
    public IReadOnlyList<string> ConflictFiles { get; init; } = [];
    public string MergedTreeOid { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Git 命令统一执行器 — 消除各处重复的 ExecuteGitCommandAsync 私有方法
/// <para>
/// 核心价值：
/// 1. 统一 git 进程调用（编码、环境变量、错误处理）
/// 2. 强制走 IProcessService（消除直接 ProcessStartInfo 绕过安全检查的隐患）
/// 3. 统一 GIT_TERMINAL_PROMPT=0 环境变量（避免交互式提示卡死）
/// </para>
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>
    /// 执行 git 命令并返回结果
    /// </summary>
    /// <param name="arguments">git 子命令及参数（如 "status --porcelain"）</param>
    /// <param name="workingDirectory">工作目录（null=当前目录）</param>
    /// <param name="ct">取消令牌</param>
    Task<GitCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default);

    /// <summary>
    /// 只读检测两个分支合并是否冲突 — 基于 git merge-tree --write-tree（不污染工作区、不修改索引）
    /// </summary>
    /// <param name="branch1">第一个分支（ours）</param>
    /// <param name="branch2">第二个分支（theirs）</param>
    /// <param name="workingDirectory">工作目录（null=当前目录）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>HasConflict=true 时 ConflictFiles 列出冲突文件；退出码 0=无冲突，1=有冲突</returns>
    Task<MergeConflictResult> DetectMergeConflictAsync(
        string branch1,
        string branch2,
        string? workingDirectory = null,
        CancellationToken ct = default);
}
