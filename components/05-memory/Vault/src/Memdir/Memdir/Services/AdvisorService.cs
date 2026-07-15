namespace Core.Memdir;

[Register]
public sealed partial class AdvisorService : IAdvisorService, IDisposable
{
    private string? _advisorModel;
    private readonly IConfigurationService? _configService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AdvisorService(IConfigurationService? configService = null)
    {
        _configService = configService;
    }

    public string? AdvisorModel
    {
        get
        {
            EnsureInitialized();
            return _advisorModel;
        }
    }

    public bool IsAdvisorEnabled
    {
        get
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(_advisorModel);
        }
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
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"AdvisorService: Initialization failed: {ex.Message}"); }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void SetAdvisorModel(string modelId)
    {
        _advisorModel = modelId;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    public void ClearAdvisorModel()
    {
        _advisorModel = null;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    private async Task InitializeAsync()
    {
        if (_configService == null) return;
        try
        {
            var saved = await _configService.GetAsync("advisor.model").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(saved)) _advisorModel = saved;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"AdvisorService: Failed to load advisor model from config: {ex.Message}");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_configService == null) return;
        try
        {
            var value = _advisorModel ?? "";
            await _configService.SetAsync("advisor.model", value)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"AdvisorService: Failed to persist advisor model: {ex.Message}");
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
