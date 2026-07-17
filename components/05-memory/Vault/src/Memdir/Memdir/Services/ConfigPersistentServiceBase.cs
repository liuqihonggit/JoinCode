namespace Core.Memdir;

public abstract class ConfigPersistentServiceBase<TValue> : IDisposable
{
    private TValue _value;
    private readonly IConfigurationService? _configService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    protected ConfigPersistentServiceBase(TValue defaultValue, IConfigurationService? configService = null)
    {
        _value = defaultValue;
        _configService = configService;
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
        if (!_initLock.Wait(0)) return;
        try
        {
            if (_initialized) return;
            try { InitializeAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"{GetType().Name}: Initialization failed: {ex.Message}"); }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
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
            System.Diagnostics.Trace.WriteLine($"{GetType().Name}: Failed to load {ConfigKey} from config: {ex.Message}");
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
            System.Diagnostics.Trace.WriteLine($"{GetType().Name}: Failed to persist {ConfigKey}: {ex.Message}");
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
