namespace Core.Bridge;


[Register(typeof(BridgeServerSecurity), ServiceLifetime.Singleton)]
public sealed record BridgeServerSecurity(
    BridgeJwtService? JwtService = null,
    ITrustedDeviceStore? TrustedDeviceStore = null);

[Register(typeof(BridgeServerSession), ServiceLifetime.Singleton)]
public sealed record BridgeServerSession(
    BridgeSessionRunner? SessionRunner = null,
    PeerSessionManager? PeerSessionManager = null,
    BridgeUIService? UIService = null);
