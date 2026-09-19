namespace Core.Planning;

/// <summary>
/// 计划模块 JSON 序列化上下文 — 源码生成器为 AOT 编译预生成类型信息
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
[JsonSerializable(typeof(PersistablePlanState))]
[JsonSerializable(typeof(PlanState))]
[JsonSerializable(typeof(PlanStep))]
[JsonSerializable(typeof(List<PlanStep>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class PlanJsonContext : JsonSerializerContext;