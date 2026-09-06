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
    private readonly IGitHubApiClient? _apiClient;
    private readonly IGitCommandRunner? _git;
    private readonly IDownloader _downloader;
    private readonly IFileSystem _fs;
    private readonly ILogger<GitHubToolHandlers>? _logger;

    /// <summary>
    /// Run 日志缓存 — 用 MemoryCache.Default(系统内存压力自动释放)
    /// <para>两级缓存(ADR 0067): Level1 摘要(轻量)长期保留, Level2 内容(大量行)按 section 独立缓存可被驱逐</para>
    /// <para>24h 过期,内存压力时 Level2 优先被驱逐,Level1 摘要保留,AI 仍可看步骤列表和 section 摘要</para>
    /// </summary>
    private static readonly MemoryCache _logCache = MemoryCache.Default;

    /// <summary>
    /// Level1 摘要缓存 key 前缀 — value=RunLogSummary(步骤名→行数, section类型→行数,轻量)
    /// <para>用 nameof 避免硬编码类名,重构时自动跟随</para>
    /// </summary>
    private static readonly string _summaryPrefix = nameof(GitHubToolHandlers) + ":summary:";

    /// <summary>
    /// Level2 内容缓存 key 前缀 — key=section:{runId}:{jobId}:{stepName}:{sectionType}, value=List&lt;string&gt;(该 section 的日志行)
    /// <para>按 section 独立缓存,内存压力时各 section 可独立被驱逐,下次访问时按需重新拉取</para>
    /// </summary>
    private static readonly string _sectionPrefix = nameof(GitHubToolHandlers) + ":section:";

    /// <summary>
    /// 统一持久化管道 — 异步串行写缓存文件到 .jcc/gh_cache/,不阻塞调用方
    /// <para>复用 ADR 0068 统一管道(IPersistencePipeline),替代专用 GitHubCacheWriteActor</para>
    /// </summary>
    private readonly IPersistencePipeline _pipeline;

    /// <summary>
    /// 文件级缓存目录 — {projectDir}/.jcc/gh_cache/,跨进程共享
    /// </summary>
    private const string CacheDirName = ".jcc/gh_cache";

    public GitHubToolHandlers(
        IDownloader downloader,
        IFileSystem fs,
        IPersistencePipeline pipeline,
        IGitHubApiClient? apiClient = null,
        IGitCommandRunner? git = null,
        ILogger<GitHubToolHandlers>? logger = null)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _apiClient = apiClient;
        _git = git;
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
    /// 构建失败 ToolResult(直接错误消息)
    /// </summary>
    private static ToolResult Fail(string message)
    {
        return ToolResultBuilder.Error().WithText(message).Build();
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
    /// 获取缓存目录路径 — {workingDir}/.jcc/gh_cache/ 或 {cwd}/.jcc/gh_cache/
    /// <para>项目级缓存,跨进程共享,24h 过期</para>
    /// </summary>
    private string GetCacheDir(string? workingDir)
    {
        var baseDir = string.IsNullOrWhiteSpace(workingDir) ? _fs.GetCurrentDirectory() : workingDir;
        return _fs.CombinePath(baseDir, CacheDirName);
    }

    /// <summary>
    /// 获取缓存文件路径 — {cacheDir}/{sanitizedRunId}_{sanitizedJobId}.{extension}
    /// </summary>
    private string GetCacheFilePath(string cacheDir, string runId, string? jobId, string extension)
    {
        var safeRunId = SanitizeFileName(runId);
        var safeJobId = SanitizeFileName(string.IsNullOrWhiteSpace(jobId) ? "all" : jobId);
        return _fs.CombinePath(cacheDir, $"{safeRunId}_{safeJobId}.{extension}");
    }

    /// <summary>
    /// 文件名安全化 — 移除路径分隔符和特殊字符,只保留字母数字下划线减号
    /// </summary>
    private static string SanitizeFileName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('_');
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    /// <summary>
    /// 解析 owner/repo — 优先用 repo 参数，否则从 git remote origin 推断（ADR 0073）
    /// </summary>
    private async Task<(string owner, string repo)?> ResolveOwnerRepoAsync(string? repo, string? workingDir, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(repo))
        {
            var parsed = ParseGitHubRepoRef(repo);
            if (parsed is not null) return parsed;
        }
        if (_git is null) return null;
        var gitResult = await _git.ExecuteAsync("remote get-url origin", workingDir, ct).ConfigureAwait(false);
        if (!gitResult.Success || string.IsNullOrWhiteSpace(gitResult.Output)) return null;
        return ParseGitHubRemoteUrl(gitResult.Output.Trim());
    }

    /// <summary>
    /// 解析 "owner/repo" 格式
    /// </summary>
    private static (string owner, string repo)? ParseGitHubRepoRef(string repo)
    {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var owner = parts[0];
        var repoName = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return (owner, repoName);
    }

    /// <summary>
    /// 解析 GitHub remote URL — 支持 https://github.com/owner/repo.git 和 git@github.com:owner/repo.git
    /// </summary>
    private static (string owner, string repo)? ParseGitHubRemoteUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        string path;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            path = uri.AbsolutePath.TrimStart('/');
        }
        else if (url.Contains('@'))
        {
            var colonIdx = url.IndexOf(':');
            if (colonIdx < 0) return null;
            path = url[(colonIdx + 1)..];
        }
        else return null;

        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return (parts[0], parts[1]);
    }
}
