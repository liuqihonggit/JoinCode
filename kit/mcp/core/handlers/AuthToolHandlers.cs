namespace McpToolDispatch;

/// <summary>
/// 通用认证工具处理器 — 查询认证状态/刷新令牌/登出
/// 复用 McpAuthToolHandlers 的认证提供者状态，提供更通用的 auth_* 工具接口
/// </summary>
[McpToolDispatch(ToolCategory.McpAuth)]
public sealed partial class AuthToolHandlers {
    private readonly McpAuthToolHandlers _mcpAuthHandlers;
    private readonly IUserInteractionService _userInteraction;
    private readonly ILogger<AuthToolHandlers>? _logger;

    /// <summary>
    /// 初始化认证工具处理器
    /// </summary>
    /// <param name="mcpAuthHandlers">MCP 认证处理器（共享认证提供者状态）</param>
    /// <param name="userInteraction">用户交互服务（登出二次确认）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public AuthToolHandlers(
        McpAuthToolHandlers mcpAuthHandlers,
        IUserInteractionService userInteraction,
        ILogger<AuthToolHandlers>? logger = null) {
        _mcpAuthHandlers = mcpAuthHandlers ?? throw new ArgumentNullException(nameof(mcpAuthHandlers));
        _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        _logger = logger;
    }

    /// <summary>
    /// 获取认证状态 — 只读操作，可并发执行
    /// </summary>
    /// <param name="auth_name">认证配置名（可选，省略则列出全部）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含认证状态的工具执行结果</returns>
    [McpTool(AuthToolNameEnumConstants.AuthGetStatus, "Get authentication status for a config or all configs", "mcp_auth", ConcurrencySafe = true)]
    public Task<ToolResult> AuthGetStatusAsync(
        [McpToolParameter("Authentication config name (optional, omit for all)", Required = false)] string? auth_name = null,
        CancellationToken cancellationToken = default) {
        return _mcpAuthHandlers.McpAuthStatusAsync(auth_name, cancellationToken);
    }

    /// <summary>
    /// 刷新认证令牌 — 涉及网络请求和凭据写入，禁止并发
    /// </summary>
    /// <param name="auth_name">认证配置名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含刷新结果的工具执行结果</returns>
    [McpTool(AuthToolNameEnumConstants.AuthRefresh, "Refresh authentication token", "mcp_auth")]
    public Task<ToolResult> AuthRefreshAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        CancellationToken cancellationToken = default) {
        return _mcpAuthHandlers.McpAuthRefreshAsync(auth_name, cancellationToken);
    }

    /// <summary>
    /// 登出并撤销认证会话 — 敏感操作，需用户二次确认，禁止并发
    /// </summary>
    /// <param name="auth_name">认证配置名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含登出结果的工具执行结果</returns>
    [McpTool(AuthToolNameEnumConstants.AuthLogout, "Logout and revoke authentication session", "mcp_auth")]
    public async Task<ToolResult> AuthLogoutAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(auth_name))
            return ToolResultBuilder.Error().WithText("auth_name cannot be empty").Build();

        var confirmed = await _userInteraction.ConfirmAsync($"确认登出 '{auth_name}'？", cancellationToken).ConfigureAwait(false);
        if (!confirmed)
            return ToolResultBuilder.Error().WithText("User cancelled logout").Build();

        return await _mcpAuthHandlers.McpAuthRemoveAsync(auth_name, cancellationToken).ConfigureAwait(false);
    }
}
