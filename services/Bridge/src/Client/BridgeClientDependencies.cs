namespace Core.Bridge;


[Register(typeof(BridgeClientSession), ServiceLifetime.Singleton)]
public sealed record BridgeClientSession(
    BridgeJwtService? JwtService = null,
    PollConfigManager? PollConfigManager = null,
    BridgeSessionRunner? SessionRunner = null,
    BridgeApiClient? ApiClient = null);
