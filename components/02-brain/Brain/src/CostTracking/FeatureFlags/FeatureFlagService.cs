using JoinCode.Abstractions.Attributes;

namespace Core.CostTracking.FeatureFlags;


public sealed partial class FeatureFlagResponse
{
    public Dictionary<string, FeatureFlag>? Features { get; set; }
    public DateTime? FetchedAt { get; set; }
}

[Register]
public sealed partial class FeatureFlagService : IFeatureFlagService, IDisposable
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<FeatureFlagService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly FeatureFlagOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;
    private readonly ConcurrentDictionary<string, FeatureFlag> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly IClockService _clock;
    private DateTime _lastFetchTime = DateTime.MinValue;
    private volatile int _disposed;

    private static readonly FeatureFlagJsonContext JsonContext = FeatureFlagJsonContext.Default;

    public FeatureFlagService(
        HttpClient httpClient,
        IOptions<FeatureFlagOptions>? options = null,
        ILogger<FeatureFlagService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _telemetryService = telemetryService;
        _options = options?.Value ?? new FeatureFlagOptions();
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _refreshTimer = new Timer(
                _ => { if (_disposed == 0) _ = RefreshAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false); },
                null,
                _options.RefreshInterval,
                _options.RefreshInterval);
        }
        else
        {
            _refreshTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<bool> IsEnabledAsync(string featureKey, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null)
        {
            return false;
        }

        if (!flag.Enabled)
        {
            return false;
        }

        if (!MatchesTargetingRules(flag, attributes))
        {
            return false;
        }

        if (flag.RolloutPercentage >= 100.0)
        {
            return true;
        }

        if (flag.RolloutPercentage <= 0.0)
        {
            return false;
        }

        var hash = ComputeHash(featureKey, attributes);
        return (hash % 100) < flag.RolloutPercentage;
    }

    public async Task<T?> GetVariantAsync<T>(string featureKey, T? defaultValue = default, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null || !flag.Enabled)
        {
            return defaultValue;
        }

        if (!MatchesTargetingRules(flag, attributes))
        {
            return defaultValue;
        }

        if (flag.DefaultValue is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _logger?.LogDebug("未配置特性标志 API 端点，跳过刷新");
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger?.LogDebug("正在刷新特性标志配置");

            var requestUrl = _options.ApiEndpoint;
            if (!string.IsNullOrEmpty(_options.ClientKey))
            {
                var separator = requestUrl.Contains('?') ? "&" : "?";
                requestUrl = $"{requestUrl}{separator}clientKey={Uri.EscapeDataString(_options.ClientKey)}";
            }

            var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var flagResponse = JsonSerializer.Deserialize(json, JsonContext.FeatureFlagResponse);

            if (flagResponse?.Features != null)
            {
                foreach (var kvp in flagResponse.Features)
                {
                    _cache[kvp.Key] = kvp.Value;
                }

                _lastFetchTime = _clock.GetUtcNow();
                _logger?.LogInformation("已刷新 {Count} 个特性标志", flagResponse.Features.Count);
                RecordFeatureFlagMetrics("refresh", true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "刷新特性标志失败");
            RecordFeatureFlagMetrics("refresh", false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFlagsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return _cache.ToFrozenDictionary();
    }

    private void RecordFeatureFlagMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("featureflag.operation.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Feature flag operation count");

    private async Task<FeatureFlag?> GetFlagAsync(string featureKey, CancellationToken cancellationToken)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return _cache.TryGetValue(featureKey, out var flag) ? flag : null;
    }

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache.IsEmpty || (_options.EnableCache && _clock.GetUtcNow() - _lastFetchTime > _options.CacheExpiration))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool MatchesTargetingRules(FeatureFlag flag, Dictionary<string, string>? attributes)
    {
        if (flag.TargetingRules == null || flag.TargetingRules.Count == 0)
        {
            return true;
        }

        if (attributes == null || attributes.Count == 0)
        {
            return false;
        }

        foreach (var rule in flag.TargetingRules)
        {
            if (!attributes.TryGetValue(rule.Key, out var value) || !string.Equals(value, rule.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
        _disposeCts.Dispose();
    }
}
