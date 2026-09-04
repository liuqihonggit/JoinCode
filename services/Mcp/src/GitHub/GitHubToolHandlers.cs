namespace McpToolDispatch;

/// <summary>
/// GitHub CLI 工具处理器 — 将 gh 子命令暴露为 MCP 工具供 LLM 直接调用
/// <para>避坑要点（AGENTS.md 坑1-5 内化）：</para>
/// <para>1. 禁用 --jq，改 gh --json 输出完整 JSON 后用 JsonDocument 解析（AOT 友好）</para>
/// <para>2. 大日志加 maxLines 截断（默认 200），优先 --job 精准拉单 job</para>
/// <para>3. Release 下载复用 IDownloader 多线程分片 + 断点续传</para>
/// <para>4. pr checks 正确处理 skipping 语义（非失败）</para>
/// <para>5. 不经 PowerShell 管道，C# Process 直接调 gh</para>
/// </summary>
[McpToolDispatch(ToolCategory.GitHub)]
public partial class GitHubToolHandlers
{
    private readonly IGitHubCommandRunner _gh;
    private readonly IDownloader _downloader;
    private readonly IFileSystem _fs;
    private readonly ILogger<GitHubToolHandlers>? _logger;

    public GitHubToolHandlers(
        IGitHubCommandRunner gh,
        IDownloader downloader,
        IFileSystem fs,
        ILogger<GitHubToolHandlers>? logger = null)
    {
        _gh = gh ?? throw new ArgumentNullException(nameof(gh));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    // === 共用辅助方法 ===

    /// <summary>
    /// 转义并引用命令行参数 — 值用双引号包裹，内部双引号转义
    /// </summary>
    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var escaped = value.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// 截断输出到指定行数 — 避免大日志撑爆 LLM 上下文（避坑2/5）
    /// </summary>
    private static string TruncateLines(string output, int maxLines)
    {
        if (string.IsNullOrEmpty(output) || maxLines <= 0) return output;
        var lines = output.Split('\n');
        if (lines.Length <= maxLines) return output;
        var sb = new StringBuilder(maxLines * 80);
        for (int i = 0; i < maxLines; i++)
        {
            sb.Append(lines[i]);
            sb.Append('\n');
        }
        sb.Append($"... [已截断，共 {lines.Length} 行，仅显示前 {maxLines} 行。如需更多请缩小过滤范围或用 --job 精准拉取]");
        return sb.ToString();
    }

    /// <summary>
    /// 执行 gh 命令 — 封装日志
    /// </summary>
    private async Task<GitHubCommandResult> RunGhAsync(string arguments, string? workingDir, CancellationToken ct)
    {
        _logger?.LogDebug("执行 gh 命令: {Args}", arguments);
        return await _gh.ExecuteAsync(arguments, workingDir, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建失败 ToolResult
    /// </summary>
    private static ToolResult Fail(GitHubCommandResult result)
    {
        var err = string.IsNullOrEmpty(result.Error)
            ? $"gh 命令失败，退出码 {result.ExitCode}"
            : result.Error;
        return ToolResultBuilder.Error().WithText(err).Build();
    }

    /// <summary>
    /// 构建成功 ToolResult
    /// </summary>
    private static ToolResult Ok(string output, string? prefix = null)
    {
        var text = string.IsNullOrEmpty(prefix) ? output : $"{prefix}\n{output}";
        return ToolResultBuilder.Success().WithText(text).Build();
    }

    /// <summary>
    /// 追加 --repo 参数（仓库不为空时）
    /// </summary>
    private static string RepoArg(string? repo)
    {
        return string.IsNullOrWhiteSpace(repo) ? string.Empty : $" --repo {repo}";
    }

    /// <summary>
    /// 追加 --workdir 参数（工作目录不为空时，通过 ExecuteAsync 的 workingDirectory 传入）
    /// </summary>
    private static string? ResolveWorkDir(string? workingDir)
    {
        return string.IsNullOrWhiteSpace(workingDir) ? null : workingDir;
    }
}
