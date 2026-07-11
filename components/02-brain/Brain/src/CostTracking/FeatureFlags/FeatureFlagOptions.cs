
namespace Core.CostTracking.FeatureFlags;

public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    public string ApiEndpoint { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableCache { get; set; } = true;
}
