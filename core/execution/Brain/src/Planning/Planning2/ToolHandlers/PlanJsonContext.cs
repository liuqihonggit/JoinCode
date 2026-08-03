namespace Core.Planning;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
public partial class PlanJsonContext : JsonSerializerContext;
