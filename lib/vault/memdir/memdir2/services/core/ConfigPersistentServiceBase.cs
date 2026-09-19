namespace Core.Memdir;

/// <summary>
/// 配置持久化服务基类 — 提供从配置服务加载/保存值的通用机制，子类通过重写抽象成员定义序列化行为
/// </summary>
/// <typeparam name="TValue">配置值的类型</typeparam>
public abstract class ConfigPersistentServiceBase<TValue> : IDisposable {
    private TValue _value;
    private readonly IConfigurationService? _configService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private bool _initialized;
    private readonly AsyncLock _initLock = new();
    /// <summary>
    /// 日志记录器（可选）
    /// </summary>
    protected readonly ILogger? _logger;

    /// <summary>
    /// 构造配置持久化服务基类
    /// </summary>
    /// <param name="defaultValue">默认值</param>
    /// <param name="configService">配置服务（可选，用于持久化）</param>
    /// <param name="logger">日志记录器（可选）</param>
    protected ConfigPersistentServiceBase(TValue defaultValue, IConfigurationService? configService = null, ILogger? logger = null) {
        _value = defaultValue;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// 配置键名 — 子类指定持久化使用的键
    /// </summary>
    protected abstract string ConfigKey { get; }
    /// <summary>
    /// 尝试将原始配置字符串解析为配置值
    /// </summary>
    /// <param name="raw">原始配置字符串</param>
    /// <param name="result">解析结果</param>
    /// <returns>解析是否成功</returns>
    protected abstract bool TryParseConfigValue(string? raw, out TValue result);
    /// <summary>
    /// 将配置值格式化为可持久化的字符串
    /// </summary>
    /// <param name="value">配置值</param>
    /// <returns>格式化后的字符串</returns>
    protected abstract string FormatConfigValue(TValue value);

    /// <summary>
    /// 当前配置值（首次访问时延迟初始化）
    /// </summary>
    protected TValue Value {
        get {
            EnsureInitialized();
            return _value;
        }
    }

    /// <summary>
    /// 设置配置值并异步持久化
    /// </summary>
    /// <param name="value">新值</param>
    protected void SetValue(TValue value) {
        _value = value;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    private void EnsureInitialized() {
        if (_initialized) return;
        if (Volatile.Read(ref _disposed) == 1) return;
        var guard = _initLock.TryLock();
        if (guard is null) return;
        using (guard) {
            if (_initialized) return;
            try { InitializeAsync().GetAwaiter().GetResult(); } catch (Exception ex) { _logger?.LogWarning(ex, "{TypeName}: 初始化失败", GetType().Name); }
            _initialized = true;
        }
    }

    private async Task InitializeAsync() {
        if (_configService == null) return;
        try {
            var saved = await _configService.GetAsync(ConfigKey).ConfigureAwait(false);
            if (TryParseConfigValue(saved, out var parsed))
                _value = parsed;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "{TypeName}: 从配置加载 {ConfigKey} 失败", GetType().Name, ConfigKey);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken) {
        if (_configService == null) return;
        try {
            await _configService.SetAsync(ConfigKey, FormatConfigValue(_value))
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogWarning(ex, "{TypeName}: 持久化 {ConfigKey} 失败", GetType().Name, ConfigKey);
        }
    }

    /// <summary>
    /// 释放资源，取消待处理的持久化操作
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.CancelAndDisposeSafe(_logger);
        _initLock.Dispose();
    }
}