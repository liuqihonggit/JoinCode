namespace Core.Memdir;

[Register]
public sealed class EditorModeService : IEditorModeService, IDisposable
{
    private volatile EditorMode _currentMode = EditorMode.Normal;
    private readonly IConfigurationService? _configService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public EditorModeService(IConfigurationService? configService = null)
    {
        _configService = configService;
    }

    public EditorMode CurrentMode
    {
        get
        {
            EnsureInitialized();
            return _currentMode;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (Volatile.Read(ref _disposed) == 1) return;
        if (!_initLock.Wait(0)) return; // 其他线程正在初始化，跳过
        try
        {
            if (_initialized) return;
            try { InitializeAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"EditorModeService: Initialization failed: {ex.Message}"); }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void SetMode(EditorMode mode)
    {
        _currentMode = mode;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    public EditorMode Toggle()
    {
        var newMode = _currentMode == EditorMode.Normal ? EditorMode.Vim : EditorMode.Normal;
        _currentMode = newMode;
        if (Volatile.Read(ref _disposed) == 0)
            _ = PersistAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        return newMode;
    }

    private async Task InitializeAsync()
    {
        if (_configService == null) return;
        try
        {
            var saved = await _configService.GetAsync("editor.mode").ConfigureAwait(false);
            if (saved != null && EditorModeExtensions.FromValue(saved) is { } mode)
                _currentMode = mode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"EditorModeService: Failed to load editor mode from config: {ex.Message}");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_configService == null) return;
        try
        {
            await _configService.SetAsync("editor.mode", _currentMode.ToString().ToLowerInvariant())
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"EditorModeService: Failed to persist editor mode: {ex.Message}");
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
