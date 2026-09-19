
namespace Services.Cache;

/// <summary>
/// 内存缓存服务 — 基于 Microsoft.Extensions.Caching.Memory.MemoryCache 实现 ICacheService
/// <para>单例服务,支持容量限制、自动压缩与过期扫描</para>
/// <para>默认条目过期时间由 WorkflowConstants.Cache.ToolInfoCacheExpirationMinutes 决定</para>
/// </summary>
[Register(typeof(ICacheService), ServiceLifetime.Singleton)]
public partial class MemoryCacheService : ServiceEntity, ICacheService, IDisposable {
    private MemoryCache _cache;
    private readonly ILogger<MemoryCacheService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly MemoryCacheEntryOptions _defaultEntryOptions;
    private bool _disposed;

    /// <summary>
    /// 初始化内存缓存服务实例 — 配置容量上限、压缩比例与过期扫描频率
    /// </summary>
    /// <param name="logger">日志记录器,为 null 时静默运行</param>
    /// <param name="telemetryService">遥测服务,为 null 时不记录指标</param>
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

    /// <summary>
    /// 同步获取缓存值 — 未命中返回类型默认值
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <returns>命中返回对应值;未命中返回 default(T)</returns>
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

    /// <summary>
    /// 异步获取缓存值 — 内部委托同步 Get 实现
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌(当前实现未使用)</param>
    /// <returns>命中返回对应值;未命中返回 default(T)</returns>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) {
        var result = Get<T>(key);
        return Task.FromResult(result);
    }

    /// <summary>
    /// 同步设置缓存值 — 指定过期时间则用绝对过期,否则使用默认过期策略
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">可选绝对过期时长;为 null 时使用默认过期时间</param>
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

    /// <summary>
    /// 异步设置缓存值 — 内部委托同步 Set 实现
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">可选绝对过期时长;为 null 时使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌(当前实现未使用)</param>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) {
        Set(key, value, expiration);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步移除缓存键 — 返回该键是否存在
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>键存在并已移除返回 true;键不存在返回 false</returns>
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

    /// <summary>
    /// 异步移除缓存键 — 内部委托同步 Remove 实现
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌(当前实现未使用)</param>
    /// <returns>键存在并已移除返回 true;键不存在返回 false</returns>
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) {
        var result = Remove(key);
        return Task.FromResult(result);
    }

    /// <summary>
    /// 判断缓存中是否包含指定键
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>包含返回 true;否则返回 false</returns>
    public bool ContainsKey(string key) {
        return _cache.TryGetValue(key, out _);
    }

    /// <summary>
    /// 异步判断缓存中是否包含指定键 — 内部委托同步 ContainsKey 实现
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌(当前实现未使用)</param>
    /// <returns>包含返回 true;否则返回 false</returns>
    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default) {
        var result = ContainsKey(key);
        return Task.FromResult(result);
    }

    /// <summary>
    /// 清空缓存中所有条目
    /// </summary>
    public void Clear() {
        _cache.Clear();
        _logger?.LogInformation("缓存已清空");
    }

    /// <summary>
    /// 异步清空缓存 — 内部委托同步 Clear 实现
    /// </summary>
    /// <param name="cancellationToken">取消令牌(当前实现未使用)</param>
    public Task ClearAsync(CancellationToken cancellationToken = default) {
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放底层 MemoryCache 资源
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache?.Dispose();
        base.Dispose();
    }

    private void RecordCacheMetrics(string operation, string result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "cache.operation.count", operation, result, "Cache operation count");

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
