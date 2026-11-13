
namespace Core.Hub.Server;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AgentRegisterRequest))]
[JsonSerializable(typeof(AgentUnregisterRequest))]
[JsonSerializable(typeof(AgentHeartbeatRequest))]
[JsonSerializable(typeof(AgentMessageRequest))]
[JsonSerializable(typeof(AgentRouteRequest))]
[JsonSerializable(typeof(AgentInfo))]
[JsonSerializable(typeof(HubHealthResponse))]
[JsonSerializable(typeof(HubAgentsResponse))]
[JsonSerializable(typeof(HubErrorResponse))]
[JsonSerializable(typeof(HubStatusResponse))]
[JsonSerializable(typeof(HubRegisterResponse))]
[JsonSerializable(typeof(HubRouteResponse))]
[JsonSerializable(typeof(HubModelInfo))]
[JsonSerializable(typeof(HubModelsResponse))]
[JsonSerializable(typeof(VendorConfigEntry))]
[JsonSerializable(typeof(VendorModelEntry))]
[JsonSerializable(typeof(HubConfigDefaultResponse))]
public partial class HubServerJsonContext : JsonSerializerContext;
