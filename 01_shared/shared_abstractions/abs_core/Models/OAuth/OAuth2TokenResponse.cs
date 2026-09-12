namespace JoinCode.Abstractions.Models.OAuth;

/// <summary>
/// OAuth2 令牌响应 — 统一 MCP 客户端、PKCE 认证、Guard OAuth 三处重复定义
/// </summary>
public sealed class OAuth2TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}
