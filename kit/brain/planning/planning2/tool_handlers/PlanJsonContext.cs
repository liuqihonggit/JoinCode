namespace Core.Planning;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
[JsonSerializable(typeof(PersistablePlanState))]
[JsonSerializable(typeof(PlanState))]
[JsonSerializable(typeof(PlanStep))]
[JsonSerializable(typeof(List<PlanStep>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class PlanJsonContext : JsonSerializerContext;
