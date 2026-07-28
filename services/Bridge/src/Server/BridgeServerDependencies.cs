
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

[Register]
public sealed record BridgeServerSecurity(
    BridgeJwtService? JwtService = null,
    ITrustedDeviceStore? TrustedDeviceStore = null);

[Register]
public sealed record BridgeServerSession(
    BridgeSessionRunner? SessionRunner = null,
    PeerSessionManager? PeerSessionManager = null,
    BridgeUIService? UIService = null);
