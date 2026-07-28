namespace JoinCode.Abstractions.Models.Shell;

/// <summary>
/// Shell 执行结果
/// </summary>
public sealed record ShellExecutionResult
{
    /// <summary>
    /// Bash 工具内联输出上限 — 对齐 TS maxResultSizeChars (30K)
    /// </summary>
    public const int MaxInlineOutputChars = 30_000;

    /// <summary>
    /// 持久化输出硬上限 — 对齐 TS MAX_PERSISTED_SIZE (64MB)
    /// </summary>
    public const long MaxPersistedSizeBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 预览大小 — 对齐 TS PREVIEW_SIZE_BYTES (2KB)
    /// </summary>
    public const int PreviewSizeBytes = 2048;

    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
    public int? ExitCode { get; init; }
    public bool Interrupted { get; init; }
    public bool Success => ExitCode == 0 && !Interrupted;
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 大输出持久化路径 — 对齐 TS outputFilePath
    /// 输出超过 MaxInlineOutputChars 时，完整输出保存到磁盘，Stdout 仅含预览
    /// </summary>
    public string? PersistedOutputPath { get; init; }

    /// <summary>
    /// 持久化输出原始大小（字节）
    /// </summary>
    public long? PersistedOutputSize { get; init; }

    /// <summary>
    /// 是否被用户手动后台化 — 对齐 TS backgroundedByUser
    /// </summary>
    public bool BackgroundedByUser { get; init; }

    /// <summary>
    /// 是否被 Assistant 模式自动后台化 — 对齐 TS assistantAutoBackgrounded
    /// 当主智能体阻塞命令超过 ASSISTANT_BLOCKING_BUDGET_MS (15s) 时自动后台化
    /// </summary>
    public bool AssistantAutoBackgrounded { get; init; }

    /// <summary>
    /// 后台任务 ID — 对齐 TS backgroundTaskId
    /// </summary>
    public string? BackgroundTaskId { get; init; }

    /// <summary>
    /// CWD 是否被重置 — 对齐 TS resetCwdIfOutsideProject
    /// </summary>
    public bool CwdWasReset { get; init; }

    /// <summary>
    /// 生成大输出持久化消息 — 对齐 TS buildLargeToolResultMessage
    /// 使用统一的 ContentReplacementConstants.BuildPersistedOutputMessage
    /// </summary>
    public string BuildPersistedOutputMessage()
    {
        if (PersistedOutputPath is null) return Stdout;

        var preview = Stdout.Length > PreviewSizeBytes
            ? Stdout[..PreviewSizeBytes]
            : Stdout;

        var hasMore = Stdout.Length > PreviewSizeBytes;
        var result = new PersistedToolResult
        {
            Filepath = PersistedOutputPath,
            OriginalSize = (int)(PersistedOutputSize ?? Stdout.Length),
            IsJson = false,
            Preview = preview,
            HasMore = hasMore
        };

        return ContentReplacementConstants.BuildPersistedOutputMessage(result);
    }

    public static ShellExecutionResult SuccessResult(string stdout, string stderr, int? exitCode = 0)
        => new()
        {
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = exitCode,
            Interrupted = false
        };

    public static ShellExecutionResult FailureResult(string errorMessage, string stdout = "", string stderr = "")
        => new()
        {
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = -1,
            Interrupted = false,
            ErrorMessage = errorMessage
        };

    public static ShellExecutionResult TimeoutResult(int timeoutMs)
        => new()
    {
            Stdout = string.Empty,
            Stderr = $"Command timed out ({timeoutMs}ms)",
            ExitCode = -1,
            Interrupted = true,
            ErrorMessage = "Timeout"
        };
}
