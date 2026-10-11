namespace JoinCode.Abstractions.Models.OAuth;

/// <summary>
/// OAuth2 令牌响应 — 统一 MCP 客户端、PKCE 认证、Guard OAuth 三处重复定义
/// </summary>
public sealed class OAuth2TokenResponse {
    /// <summary>获取或设置访问令牌。</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>获取或设置令牌类型。</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    /// <summary>获取或设置过期时间（秒）。</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>获取或设置刷新令牌。</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>获取或设置授权范围。</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}