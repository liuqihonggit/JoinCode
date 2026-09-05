namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// GitHub CLI 命令执行结果
/// </summary>
public sealed class GitHubCommandResult
{
    public required bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}

/// <summary>
/// PR 创建结果
/// </summary>
public sealed class PrCreateResult
{
    public required bool Success { get; init; }
    public string? PrUrl { get; init; }
    public string? PrNumber { get; init; }
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// PR 列表项
/// </summary>
public sealed class PrListItem
{
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// PR 列表结果
/// </summary>
public sealed class PrListResult
{
    public required bool Success { get; init; }
    public IReadOnlyList<PrListItem> Items { get; init; } = [];
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// GitHub CLI 命令统一执行器 — 封装 gh 命令，支持 PR body 自动生成
/// <para>
/// 核心价值：
/// 1. 统一 gh 进程调用（编码、环境变量、错误处理）
/// 2. 强制走 IProcessService（消除直接 ProcessStartInfo 绕过安全检查的隐患）
/// 3. PR body 自动生成（避免用户忘记填写）
/// 4. 重试机制（指数退避，解决网络超时问题）
/// </para>
/// </summary>
public interface IGitHubCommandRunner
{
    /// <summary>
    /// 执行 gh 命令并返回结果
    /// </summary>
    /// <param name="arguments">gh 子命令及参数（如 "pr list --state open"）</param>
    /// <param name="workingDirectory">工作目录（null=当前目录）</param>
    /// <param name="timeoutMs">超时毫秒（null=用默认 30s；大日志命令如 --log 可传 120000）</param>
    /// <param name="ct">取消令牌</param>
    Task<GitHubCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        int? timeoutMs = null,
        CancellationToken ct = default);

    /// <summary>
    /// 流式执行 gh 命令，逐行 yield stdout — 用于大日志过滤场景，避免全部读到内存
    /// <para>调用方可逐行过滤，达到 maxLines 后取消枚举即可终止进程读取</para>
    /// </summary>
    /// <param name="arguments">gh 子命令及参数</param>
    /// <param name="workingDirectory">工作目录（null=当前目录）</param>
    /// <param name="timeoutMs">超时毫秒（null=用默认 120s；流式场景给更长超时）</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<string> ExecuteStreamingAsync(
        string arguments,
        string? workingDirectory = null,
        int? timeoutMs = null,
        CancellationToken ct = default);

    /// <summary>
    /// 创建 PR — 自动注入 body 参数
    /// </summary>
    /// <param name="title">PR 标题</param>
    /// <param name="body">PR 内容（null=自动生成）</param>
    /// <param name="baseBranch">基础分支</param>
    /// <param name="headBranch">头部分支</param>
    /// <param name="repo">仓库（null=当前仓库）</param>
    /// <param name="draft">是否草稿</param>
    /// <param name="ct">取消令牌</param>
    Task<PrCreateResult> CreatePrAsync(
        string title,
        string? body,
        string baseBranch,
        string headBranch,
        string? repo = null,
        bool draft = false,
        CancellationToken ct = default);

    /// <summary>
    /// 列出 PR
    /// </summary>
    /// <param name="repo">仓库（null=当前仓库）</param>
    /// <param name="state">状态（open/closed/merged/all）</param>
    /// <param name="limit">数量限制</param>
    /// <param name="ct">取消令牌</param>
    Task<PrListResult> ListPrsAsync(
        string? repo = null,
        string state = "open",
        int limit = 30,
        CancellationToken ct = default);
}
