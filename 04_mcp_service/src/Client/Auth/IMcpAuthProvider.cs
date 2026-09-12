namespace McpClient;

/// <summary>
/// MCP 认证提供者接口
/// </summary>
public interface IMcpAuthProvider
{
    /// <summary>
    /// 认证类型
    /// </summary>
    McpAuthType AuthType { get; }

    /// <summary>
    /// 获取认证头
    /// </summary>
    Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取访问令牌
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新认证令牌
    /// </summary>
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 认证是否有效
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 标记 Step-Up 认证待处理 — 对齐 TS ClaudeAuthProvider.markStepUpPending
    /// 当服务端返回 403 + WWW-Authenticate: insufficient_scope 时调用
    /// </summary>
    void MarkStepUpPending(string scope);

    /// <summary>
    /// 当前 Step-Up 待处理的 scope — 对齐 TS ClaudeAuthProvider._pendingStepUpScope
    /// </summary>
    string? StepUpPendingScope { get; }

    /// <summary>
    /// 是否需要 Step-Up 认证（当前 scope 不包含待提升的 scope）
    /// </summary>
    bool NeedsStepUp { get; }

    /// <summary>
    /// 清除 Step-Up 状态（令牌保存后调用）
    /// </summary>
    void ClearStepUpPending();
}

/// <summary>
/// MCP 认证上下文
/// </summary>
public class McpAuthContext
{
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 令牌过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 认证头
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// 是否已过期
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    /// <summary>
    /// 令牌关联的 scope（空格分隔）— 对齐 TS tokenData.scope
    /// </summary>
    public string? Scope { get; set; }
}
