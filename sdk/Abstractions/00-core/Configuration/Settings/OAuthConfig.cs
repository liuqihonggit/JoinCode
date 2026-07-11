
namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// OAuth 配置 — 下沉到 Contracts 层供 IProviderDefinition 使用
/// </summary>
public sealed record OAuthConfig
{
    /// <summary>
    /// 提供商名称
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// 客户端 ID
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// 客户端密钥（可选，用于某些流程）
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// 授权端点 URL
    /// </summary>
    public required string AuthorizationEndpoint { get; init; }

    /// <summary>
    /// Token 端点 URL
    /// </summary>
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// 撤销端点 URL（可选）
    /// </summary>
    public string? RevocationEndpoint { get; init; }

    /// <summary>
    /// 重定向 URI
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// 请求的作用域
    /// </summary>
    public IReadOnlyList<string> Scope { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 是否使用 PKCE
    /// </summary>
    public bool UsePkce { get; init; } = true;

    /// <summary>
    /// 额外的授权参数
    /// </summary>
    public Dictionary<string, string> AdditionalParams { get; init; } = new();
}
