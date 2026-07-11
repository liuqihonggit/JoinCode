
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

[Register]
public sealed record BridgeClientSession(
    BridgeJwtService? JwtService = null,
    PollConfigManager? PollConfigManager = null,
    BridgeSessionRunner? SessionRunner = null,
    BridgeApiClient? ApiClient = null);
