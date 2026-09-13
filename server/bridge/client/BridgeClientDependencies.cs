namespace Core.Bridge;


/// <summary>
/// Bridge 客户端会话依赖聚合记录 — 封装 JWT 服务、轮询配置管理器、会话运行器和 API 客户端
/// </summary>
/// <param name="JwtService">JWT 服务（可选）</param>
/// <param name="PollConfigManager">轮询配置管理器（可选）</param>
/// <param name="SessionRunner">会话运行器（可选）</param>
/// <param name="ApiClient">Bridge API 客户端（可选）</param>
[Register(typeof(BridgeClientSession), ServiceLifetime.Singleton)]
public sealed record BridgeClientSession(
    BridgeJwtService? JwtService = null,
    PollConfigManager? PollConfigManager = null,
    BridgeSessionRunner? SessionRunner = null,
    BridgeApiClient? ApiClient = null);
