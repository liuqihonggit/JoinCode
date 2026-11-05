namespace Core.CostTracking;

/// <summary>
/// 成本跟踪 JSON 序列化上下文 — 紧凑格式，源码生成器为 AOT 提供预生成序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<TokenUsageRecord>))]
[JsonSerializable(typeof(List<AnalyticsEvent>))]
[JsonSerializable(typeof(AnalyticsEvent))]
[JsonSerializable(typeof(AnalyticsExportData))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(CostStatistics))]
[JsonSerializable(typeof(List<ModelCostStatistics>))]
[JsonSerializable(typeof(SessionCostData))]
public partial class CostTrackingJsonContext : JsonSerializerContext;

/// <summary>
/// 成本跟踪 JSON 序列化上下文 — 缩进格式，用于导出可读 JSON
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AnalyticsEvent>))]
[JsonSerializable(typeof(AnalyticsExportData))]
public partial class CostTrackingIndentedJsonContext : JsonSerializerContext;