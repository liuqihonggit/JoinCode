namespace Core.Planning;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
public partial class PlanJsonContext : JsonSerializerContext;
