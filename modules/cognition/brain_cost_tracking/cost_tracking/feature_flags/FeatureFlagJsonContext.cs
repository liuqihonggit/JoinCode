
namespace Core.CostTracking.FeatureFlags;

/// <summary>
/// 特性标志 JSON 序列化上下文 — 源码生成器为 AOT 提供预生成序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FeatureFlag))]
[JsonSerializable(typeof(List<FeatureFlag>))]
[JsonSerializable(typeof(Dictionary<string, FeatureFlag>))]
[JsonSerializable(typeof(FeatureFlagResponse))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class FeatureFlagJsonContext : JsonSerializerContext;