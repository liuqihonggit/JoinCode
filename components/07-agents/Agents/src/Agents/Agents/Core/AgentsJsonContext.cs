
namespace Core.Agents;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(SwarmPermissionRequestData))]
[JsonSerializable(typeof(SwarmPermissionResponseData))]
[JsonSerializable(typeof(SwarmPermissionUpdateData))]
[JsonSerializable(typeof(List<SwarmPermissionUpdateData>))]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
public partial class AgentsJsonContext : JsonSerializerContext;
