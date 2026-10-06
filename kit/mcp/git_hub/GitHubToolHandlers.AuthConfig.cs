namespace McpToolDispatch;

/// <summary>
/// GitHub Auth/Config 工具 — auth status 检查 API 客户端配置，config get/set 简化提示
/// <para>auth login/refresh/token 涉及 OAuth 流程，需用系统 gh CLI；config 读取 gh config.yml</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 查看 Auth 状态 — 检查 API 客户端配置状态，调 GET /user 验证 token 有效性
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAuthStatus, "查看 GitHub Auth 状态", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhAuthStatusAsync(
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return Fail("GitHub REST API 客户端未配置(IGitHubApiClient 未注入) — auth login 需用系统 gh CLI: gh auth login");
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail($"Auth 验证失败: {result.Error} — 请用系统 gh CLI 重新登录: gh auth login");
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var login = doc.RootElement.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
            var name = doc.RootElement.TryGetProperty("name", out var n) ? (n.ValueKind == JsonValueKind.Null ? "" : n.GetString() ?? "") : "";
            var sb = new StringBuilder(128);
            sb.AppendLine($"已登录: {login}");
            if (!string.IsNullOrEmpty(name)) sb.AppendLine($"名称: {name}");
            sb.Append("Token 状态: 有效");
            return Ok(sb.ToString());
        } catch { return Ok("Auth 状态: 已配置(响应解析失败)"); }
    }

    /// <summary>
    /// 读取配置 — 简化提示用系统 gh CLI
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhConfigGet, "读取 gh 配置(提示用系统 gh)", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhConfigGetAsync(
        [McpToolParameter("配置键名(如 git_protocol/editor/prompt)", Required = true)] string key,
        [McpToolParameter("host(可选,per-host 设置)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Fail($"config get 需读取 gh config.yml，jcc 未实现本地配置管理。请用系统 gh CLI: gh config get {key}{(string.IsNullOrWhiteSpace(host) ? "" : $" --host {host}")}"));

    /// <summary>
    /// 写入配置 — 简化提示用系统 gh CLI
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhConfigSet, "写入 gh 配置(提示用系统 gh)", "github")]
    public Task<ToolResult> GhConfigSetAsync(
        [McpToolParameter("配置键名", Required = true)] string key,
        [McpToolParameter("配置值", Required = true)] string value,
        [McpToolParameter("host(可选,per-host 设置)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Fail($"config set 需写入 gh config.yml，jcc 未实现本地配置管理。请用系统 gh CLI: gh config set {key} {value}{(string.IsNullOrWhiteSpace(host) ? "" : $" --host {host}")}"));
}
