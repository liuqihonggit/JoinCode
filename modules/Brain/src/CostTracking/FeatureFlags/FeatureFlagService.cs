
namespace Core.CostTracking.FeatureFlags;

public sealed partial class FeatureFlagResponse
{
    public Dictionary<string, FeatureFlag>? Features { get; set; }
    public DateTime? FetchedAt { get; set; }
}

[Register]
public sealed partial class FeatureFlagService : RemoteCacheRefreshServiceBase<FeatureFlag>, IFeatureFlagService
{
    private static readonly FeatureFlagJsonContext JsonContext = FeatureFlagJsonContext.Default;

    protected override string MetricsPrefix => "featureflag.operation";
    protected override string RefreshLogLabel => "特性标志";

    public FeatureFlagService(
        HttpClient httpClient,
        IOptions<FeatureFlagOptions>? options = null,
        ILogger<FeatureFlagService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
        : base(httpClient, options?.Value ?? new FeatureFlagOptions(), logger, telemetryService, clock)
    {
    }

    protected override async Task<RemoteRefreshResult<FeatureFlag>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var flagResponse = JsonSerializer.Deserialize(json, JsonContext.FeatureFlagResponse);

        return new RemoteRefreshResult<FeatureFlag>
        {
            Items = flagResponse?.Features
        };
    }

    public async Task<bool> IsEnabledAsync(string featureKey, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null || !flag.Enabled) return false;
        if (!MatchesTargetingRules(flag, attributes)) return false;
        if (flag.RolloutPercentage >= 100.0) return true;
        if (flag.RolloutPercentage <= 0.0) return false;

        var hash = ComputeHash(featureKey, attributes);
        return (hash % 100) < flag.RolloutPercentage;
    }

    public async Task<T?> GetVariantAsync<T>(string featureKey, T? defaultValue = default, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null || !flag.Enabled) return defaultValue;
        if (!MatchesTargetingRules(flag, attributes)) return defaultValue;
        if (flag.DefaultValue is T typedValue) return typedValue;

        return defaultValue;
    }

    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFlagsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return Cache.ToFrozenDictionary();
    }

    private async Task<FeatureFlag?> GetFlagAsync(string featureKey, CancellationToken cancellationToken)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return Cache.TryGetValue(featureKey, out var flag) ? flag : null;
    }

    private static bool MatchesTargetingRules(FeatureFlag flag, Dictionary<string, string>? attributes)
    {
        if (flag.TargetingRules == null || flag.TargetingRules.Count == 0) return true;
        if (attributes == null || attributes.Count == 0) return false;

        foreach (var rule in flag.TargetingRules)
        {
            if (!attributes.TryGetValue(rule.Key, out var value) || !string.Equals(value, rule.Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static double ComputeHash(string featureKey, Dictionary<string, string>? attributes)
    {
        var input = featureKey;
        if (attributes != null)
        {
            var sortedKeys = attributes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
            foreach (var key in sortedKeys)
            {
                input = $"{input}:{key}={attributes[key]}";
            }
        }

        var hash = 0;
        foreach (var c in input)
        {
            hash = ((hash << 5) - hash) + c;
            hash &= 0x7FFFFFFF;
        }

        return hash % 100;
    }
}
