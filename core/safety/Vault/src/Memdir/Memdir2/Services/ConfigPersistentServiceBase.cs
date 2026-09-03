namespace Core.Memdir;

public abstract class ConfigPersistentServiceBase<TValue> : IDisposable
{
    private TValue _value;
    private readonly IConfigurationService? _configService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private bool _initialized;
    private readonly AsyncLock _initLock = new();
    protected readonly ILogger? _logger;

    protected ConfigPersistentServiceBase(TValue defaultValue, IConfigurationService? configService = null, ILogger? logger = null)
    {
        _value = defaultValue;
        _configService = configService;
        _logger = logger;
    }

    protected abstract string ConfigKey { get; }
    protected abstract bool TryParseConfigValue(string? raw, out TValue result);
    protected abstract string FormatConfigValue(TValue value);

    protected TValue Value
    {
        get
        {
            EnsureInitialized();
            return _value;
        }
    }

    protected void SetValue(TValue value)
    {
        _value = value;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (Volatile.Read(ref _disposed) == 1) return;
        var guard = _initLock.TryLock(TimeSpan.Zero);
        if (guard is null) return;
        using (guard)
        {
            if (_initialized) return;
            try { InitializeAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "{TypeName}: 初始化失败", GetType().Name); }
            _initialized = true;
        }
    }

    private async Task InitializeAsync()
    {
        if (_configService == null) return;
        try
        {
            var saved = await _configService.GetAsync(ConfigKey).ConfigureAwait(false);
            if (TryParseConfigValue(saved, out var parsed))
                _value = parsed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "{TypeName}: 从配置加载 {ConfigKey} 失败", GetType().Name, ConfigKey);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_configService == null) return;
        try
        {
            await _configService.SetAsync(ConfigKey, FormatConfigValue(_value))
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "{TypeName}: 持久化 {ConfigKey} 失败", GetType().Name, ConfigKey);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _initLock.Dispose();
    }
}
