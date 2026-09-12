namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// Token 验证上下文 — 支持通用 TokenValidationMiddleware 复用
/// </summary>
public interface ITokenValidationContext : IPipelineContext
{
    /// <summary>访问令牌（中间件设置）</summary>
    string? AccessToken { get; set; }

    /// <summary>获取访问令牌的委托</summary>
    Func<string?> GetAccessToken { get; }
}
