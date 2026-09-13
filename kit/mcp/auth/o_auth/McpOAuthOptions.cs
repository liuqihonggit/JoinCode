
namespace McpClient;

/// <summary>
/// MCP OAuth 认证选项 — 配置 OAuth 2.0 PKCE 授权流程的参数
/// </summary>
public sealed class McpOAuthOptions
{
    /// <summary>
    /// OAuth 客户端 ID
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// OAuth 客户端密钥（机密应用用，公开 PKCE 应用可空）
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// 授权端点 URL
    /// </summary>
    public string AuthorizationUrl { get; init; } = string.Empty;

    /// <summary>
    /// 令牌端点 URL
    /// </summary>
    public string TokenUrl { get; init; } = string.Empty;

    /// <summary>
    /// 授权回调重定向 URL（默认 http://localhost:8765/callback）
    /// </summary>
    public string RedirectUrl { get; init; } = "http://localhost:8765/callback";

    /// <summary>
    /// 申请的 OAuth scope 列表
    /// </summary>
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 授权流程超时时间（默认 5 分钟）
    /// </summary>
    public TimeSpan AuthorizationTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 令牌持久化存储路径（为空则不持久化）
    /// </summary>
    public string TokenStoragePath { get; init; } = string.Empty;

    /// <summary>
    /// 是否已预配置客户端（ClientId、AuthorizationUrl、TokenUrl 均非空时为 true）
    /// </summary>
    public bool HasPreconfiguredClient => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(AuthorizationUrl) && !string.IsNullOrEmpty(TokenUrl);
}
