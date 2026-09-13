namespace Core.Bridge;


/// <summary>
/// Bridge 服务端安全依赖聚合记录 — 封装 JWT 服务与可信设备存储
/// </summary>
[Register(typeof(BridgeServerSecurity), ServiceLifetime.Singleton)]
public sealed record BridgeServerSecurity(
    BridgeJwtService? JwtService = null,
    ITrustedDeviceStore? TrustedDeviceStore = null);

/// <summary>
/// Bridge 服务端会话依赖聚合记录 — 封装会话运行器、对等会话管理器与 UI 服务
/// </summary>
[Register(typeof(BridgeServerSession), ServiceLifetime.Singleton)]
public sealed record BridgeServerSession(
    BridgeSessionRunner? SessionRunner = null,
    PeerSessionManager? PeerSessionManager = null,
    BridgeUIService? UIService = null);
