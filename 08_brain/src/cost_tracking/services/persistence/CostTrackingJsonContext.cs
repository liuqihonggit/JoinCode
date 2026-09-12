namespace Core.CostTracking;

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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AnalyticsEvent>))]
[JsonSerializable(typeof(AnalyticsExportData))]
public partial class CostTrackingIndentedJsonContext : JsonSerializerContext;
