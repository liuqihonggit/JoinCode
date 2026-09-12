namespace Services.OAuth;

/// <summary>
/// OAuth 配置选项
/// 用于 IOptions 模式绑定
/// </summary>
public sealed class OAuthOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "OAuth";

    /// <summary>
    /// 客户端 ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// 授权端点 URL
    /// </summary>
    public string AuthorizeUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token 端点 URL
    /// </summary>
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>
    /// 撤销端点 URL（可选）
    /// </summary>
    public string? RevocationUrl { get; set; }

    /// <summary>
    /// 重定向 URI
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost:8080/callback";

    /// <summary>
    /// 请求的作用域
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// 是否使用 PKCE
    /// </summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>
    /// Token 存储位置
    /// </summary>
    public TokenStorageLocation StorageLocation { get; set; } = TokenStorageLocation.DPAPI;

    /// <summary>
    /// 自动刷新 Token 的提前时间（分钟）
    /// </summary>
    public int RefreshBufferMinutes { get; set; } = 5;

    /// <summary>
    /// 转换为 OAuthConfig
    /// </summary>
    public OAuthConfig ToOAuthConfig(string provider) => new()
    {
        Provider = provider,
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        AuthorizationEndpoint = AuthorizeUrl,
        TokenEndpoint = TokenUrl,
        RevocationEndpoint = RevocationUrl,
        RedirectUri = RedirectUri,
        Scope = Scopes.AsReadOnly(),
        UsePkce = UsePkce
    };
}

/// <summary>
/// Token 存储位置
/// </summary>
public enum TokenStorageLocation
{
    /// <summary>
    /// 使用 Windows DPAPI 加密存储
    /// </summary>
    [EnumValue("dpapi")] DPAPI,

    /// <summary>
    /// 存储在环境变量中
    /// </summary>
    [EnumValue("environment")] Environment,

    /// <summary>
    /// 存储在文件中（加密）
    /// </summary>
    [EnumValue("file")] File
}
