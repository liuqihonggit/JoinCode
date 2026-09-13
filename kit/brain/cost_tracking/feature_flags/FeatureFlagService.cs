
namespace Core.CostTracking.FeatureFlags;

/// <summary>
/// 特性标志响应 — 远程特性标志服务返回的响应结构
/// </summary>
public sealed partial class FeatureFlagResponse
{
    /// <summary>
    /// 特性标志字典，键为标志名称
    /// </summary>
    public Dictionary<string, FeatureFlag> Features { get; set; } = [];

    /// <summary>
    /// 响应获取时间戳 (UTC，可为空)
    /// </summary>
    public DateTime? FetchedAt { get; set; }
}

/// <summary>
/// 特性标志服务 — 提供远程特性标志的缓存、刷新、定向规则匹配与灰度发布能力
/// </summary>
[Register(typeof(RemoteCacheRefreshServiceBase<FeatureFlag>), ServiceLifetime.Singleton)]
[Register(typeof(IFeatureFlagService), ServiceLifetime.Singleton)]
public sealed partial class FeatureFlagService : RemoteCacheRefreshServiceBase<FeatureFlag>, IFeatureFlagService
{
    private static readonly FeatureFlagJsonContext JsonContext = FeatureFlagJsonContext.Default;

    /// <summary>
    /// 遥测指标前缀
    /// </summary>
    protected override string MetricsPrefix => "featureflag.operation";

    /// <summary>
    /// 刷新日志标签
    /// </summary>
    protected override string RefreshLogLabel => "特性标志";

    /// <summary>
    /// 构造特性标志服务实例
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="options">特性标志配置选项（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    public FeatureFlagService(
        HttpClient httpClient,
        IOptions<FeatureFlagOptions>? options = null,
        ILogger<FeatureFlagService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
        : base(httpClient, options?.Value ?? new FeatureFlagOptions(), logger, telemetryService, clock)
    {
    }

    /// <summary>
    /// 异步从远程 URL 拉取并反序列化特性标志数据
    /// </summary>
    /// <param name="requestUrl">请求 URL</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程刷新结果，包含拉取到的特性标志条目列表</returns>
    protected override async Task<RemoteRefreshResult<FeatureFlag>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var flagResponse = RelaxedJsonSerializer.Deserialize(json, JsonContext.FeatureFlagResponse);

        return new RemoteRefreshResult<FeatureFlag>
        {
            Items = flagResponse?.Features ?? []
        };
    }

    /// <summary>
    /// 异步判断指定特性标志是否启用 — 综合考虑标志开关、定向规则与灰度发布百分比
    /// </summary>
    /// <param name="featureKey">特性标志键</param>
    /// <param name="attributes">定向规则属性字典（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>标志是否启用；若标志不存在则返回 true，若禁用或未命中定向规则则返回 false</returns>
    public async Task<bool> IsEnabledAsync(string featureKey, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null) return true;
        if (!flag.Enabled) return false;
        if (!MatchesTargetingRules(flag, attributes)) return false;
        if (flag.RolloutPercentage >= 100.0) return true;
        if (flag.RolloutPercentage <= 0.0) return false;

        var hash = ComputeHash(featureKey, attributes);
        return (hash % 100) < flag.RolloutPercentage;
    }

    /// <summary>
    /// 异步获取指定特性标志的变体值
    /// </summary>
    /// <typeparam name="T">变体值类型</typeparam>
    /// <param name="featureKey">特性标志键</param>
    /// <param name="defaultValue">默认值（可选）</param>
    /// <param name="attributes">定向规则属性字典（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>变体值；若标志不存在、禁用、未命中定向规则或类型不匹配则返回默认值</returns>
    public async Task<T?> GetVariantAsync<T>(string featureKey, T? defaultValue = default, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureKey);

        var flag = await GetFlagAsync(featureKey, cancellationToken).ConfigureAwait(false);
        if (flag == null || !flag.Enabled) return defaultValue;
        if (!MatchesTargetingRules(flag, attributes)) return defaultValue;
        if (flag.DefaultValue is T typedValue) return typedValue;

        return defaultValue;
    }

    /// <summary>
    /// 异步获取所有特性标志的只读字典快照
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>特性标志只读字典，键为标志名称</returns>
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
