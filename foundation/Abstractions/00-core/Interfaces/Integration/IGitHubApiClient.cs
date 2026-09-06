namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// GitHub REST API 响应 — 通用返回包装
/// </summary>
public sealed record GitHubApiResponse
{
    /// <summary>
    /// 是否成功（2xx 状态码）
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// 响应体（JSON 字符串，由调用方用 JsonContext 解析）
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// 错误信息（失败时填充，含 GitHub 错误响应的 message 字段）
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Link header 中 rel="next" 的 URL（分页用，无则 null）
    /// </summary>
    public string? NextPageUrl { get; init; }
}

/// <summary>
/// GitHub REST API 客户端 — 直调 https://api.github.com，摆脱系统 gh CLI 依赖
/// <para>ADR: 0073 — 替代 GitHubCommandRunner 起子进程方案</para>
/// <para>核心能力：</para>
/// <para>1. Token 解析：JCC_GITHUB_TOKEN → GITHUB_TOKEN → 抛 ConfigurationException</para>
/// <para>2. Base URL 可配置：JCC_GITHUB_API_URL（支持 GitHub Enterprise）</para>
/// <para>3. Rate limit 处理：403 + X-RateLimit-Remaining:0 → 读 Retry-After → 重试</para>
/// <para>4. 分页：解析 Link header，paginate=true 时自动跟随</para>
/// <para>5. 错误映射：HTTP 状态码 + GitHub 错误响应体 → GitHubApiResponse.Error</para>
/// <para>6. Run 日志：GET /actions/runs/{id}/logs 返回 zip → 解压逐文件逐行 yield</para>
/// </summary>
public interface IGitHubApiClient
{
    /// <summary>
    /// 通用 REST API 调用 — 返回原始 JSON 字符串
    /// </summary>
    /// <param name="method">HTTP 方法（GET/POST/PATCH/PUT/DELETE）</param>
    /// <param name="path">API 路径（如 "repos/owner/repo/pulls/123"），不含 base URL</param>
    /// <param name="body">请求体 JSON（POST/PATCH/PUT 用，null=无 body）</param>
    /// <param name="query">查询参数（null=无查询参数）</param>
    /// <param name="paginate">是否自动分页（跟随 Link header rel=next，合并所有页的 JSON 数组）</param>
    /// <param name="ct">取消令牌</param>
    Task<GitHubApiResponse> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false,
        CancellationToken ct = default);

    /// <summary>
    /// 获取 Actions Run 日志 — 逐行 yield（zip 解压后逐文件逐行）
    /// <para>GitHub REST API 的 logs 端点返回 zip 流，此方法解压后逐文件逐行 yield</para>
    /// <para>调用方可逐行过滤，达到 maxLines 后取消枚举即可终止</para>
    /// </summary>
    /// <param name="owner">仓库 owner</param>
    /// <param name="repo">仓库名</param>
    /// <param name="runId">Run ID</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<string> GetRunLogsAsync(
        string owner,
        string repo,
        long runId,
        CancellationToken ct = default);

    /// <summary>
    /// 获取 Actions Job 日志 — 逐行 yield（zip 解压后逐文件逐行）
    /// <para>GET /repos/{owner}/{repo}/actions/jobs/{jobId}/logs 返回 zip 流</para>
    /// </summary>
    IAsyncEnumerable<string> GetJobLogsAsync(
        string owner,
        string repo,
        long jobId,
        CancellationToken ct = default);

    /// <summary>
    /// 上传 Release asset — 二进制上传到 uploads.github.com
    /// <para>POST https://uploads.github.com/repos/{owner}/{repo}/releases/{releaseId}/assets?name={fileName}</para>
    /// <para>Content-Type: application/octet-stream，Body: 文件二进制内容</para>
    /// </summary>
    Task<GitHubApiResponse> UploadAssetAsync(
        string owner,
        string repo,
        long releaseId,
        string fileName,
        Stream fileStream,
        CancellationToken ct = default);
}
