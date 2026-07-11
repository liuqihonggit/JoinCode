
namespace Services.Cache;

[Register]
public partial class MemoryCacheService : ICacheService, IDisposable {
    private MemoryCache _cache;
    [Inject] private readonly ILogger<MemoryCacheService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly MemoryCacheEntryOptions _defaultEntryOptions;

    public MemoryCacheService(ILogger<MemoryCacheService>? logger = null, ITelemetryService? telemetryService = null) {
        _logger = logger;
        _telemetryService = telemetryService;
        _cache = new MemoryCache(new MemoryCacheOptions {
            SizeLimit = WorkflowConstants.Analytics.MaxEvents,
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(WorkflowConstants.Cache.ContextCacheExpirationMinutes)
        });
        _defaultEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(WorkflowConstants.Cache.ToolInfoCacheExpirationMinutes))
            .SetSize(1);
    }

    public T? Get<T>(string key) {
        if (_cache.TryGetValue(key, out T? value)) {
            _logger?.LogDebug("缓存命中，键: {Key}", key);
            RecordCacheMetrics("get", "hit");
            return value;
        }
        _logger?.LogDebug("缓存未命中，键: {Key}", key);
        RecordCacheMetrics("get", "miss");
        return default;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) {
        var result = Get<T>(key);
        return Task.FromResult(result);
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null) {
        var options = expiration.HasValue
            ? new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expiration.Value)
                .SetSize(1)
            : _defaultEntryOptions;

        _cache.Set(key, value, options);
        _logger?.LogDebug("缓存已设置，键: {Key}, 过期时间: {Expiration}", key,
            expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value).ToString() : "默认30分钟");
        RecordCacheMetrics("set", "success");
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) {
        Set(key, value, expiration);
        return Task.CompletedTask;
    }

    public bool Remove(string key) {
        var exists = _cache.TryGetValue(key, out _);
        if (exists) {
            _cache.Remove(key);
            _logger?.LogDebug("缓存已移除，键: {Key}", key);
            RecordCacheMetrics("remove", "hit");
            return true;
        }
        RecordCacheMetrics("remove", "miss");
        return false;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) {
        var result = Remove(key);
        return Task.FromResult(result);
    }

    public bool ContainsKey(string key) {
        return _cache.TryGetValue(key, out _);
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default) {
        var result = ContainsKey(key);
        return Task.FromResult(result);
    }

    public void Clear() {
        _cache.Clear();
        _logger?.LogInformation("缓存已清空");
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) {
        Clear();
        return Task.CompletedTask;
    }

    public void Dispose() {
        _cache?.Dispose();
    }

    private void RecordCacheMetrics(string operation, string result)
        => _telemetryService?.RecordCount("cache.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, description: "Cache operation count");

    /// <summary>
    /// 测试专用：强制触发过期扫描
    /// </summary>
    internal void TriggerExpirationScanForTests() {
        // 通过设置一个已过期的条目来触发扫描
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromTicks(1))
            .SetSize(1);
        _cache.Set("__test_expiration_trigger__", "test", options);
        // 立即读取以触发清理
        _cache.TryGetValue("__test_expiration_trigger__", out _);
    }
}
