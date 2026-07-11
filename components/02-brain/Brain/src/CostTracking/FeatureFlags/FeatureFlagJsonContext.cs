
namespace Core.CostTracking.FeatureFlags;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FeatureFlag))]
[JsonSerializable(typeof(List<FeatureFlag>))]
[JsonSerializable(typeof(Dictionary<string, FeatureFlag>))]
[JsonSerializable(typeof(FeatureFlagResponse))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class FeatureFlagJsonContext : JsonSerializerContext;
