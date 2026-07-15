namespace Services.CodeIndex;

[Register]
public sealed partial class LspIntegration : IDisposable
{
    private readonly ICodeIndexer _indexer;
    private readonly ILspService? _lspService;
    private readonly ILspFileSync? _lspFileSync;
    [Inject] private readonly ILogger<LspIntegration>? _logger;
    private readonly CancellationTokenSource _updateCts = new();
    private int _disposed;

    public LspIntegration(ICodeIndexer indexer, ILspService? lspService = null, ILspFileSync? lspFileSync = null, ILogger<LspIntegration>? logger = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _lspService = lspService;
        _lspFileSync = lspFileSync;
        _logger = logger;

        if (_lspFileSync is not null)
        {
            _lspFileSync.DocumentChanged += OnLspDocumentChanged;
        }
    }

    public bool IsLspAvailable => _lspService is not null;

    private void OnLspDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_disposed != 0) return;

        _ = SafeUpdateAsync(e.FilePath, _updateCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _updateCts.Token).ConfigureAwait(false);
    }

    public async Task OnDocumentChangedAsync(string filePath, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePath);
        await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
    }

    public async Task OnDocumentSavedAsync(string filePath, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePath);
        await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
    }

    public async Task OnWatchedFilesChangedAsync(IEnumerable<string> filePaths, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePaths);
        foreach (var filePath in filePaths)
        {
            await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
        }
    }

    public async Task<List<LspLocation>> TryFindDefinitionAsync(string filePath, int line, int character, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_lspService is null)
        {
            return [];
        }

        try
        {
            return await _lspService.GotoDefinitionAsync(filePath, line, character, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationGotoDefinitionFailed));
            return [];
        }
    }

    public async Task<List<LspLocation>> TryFindReferencesAsync(string filePath, int line, int character, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_lspService is null)
        {
            return [];
        }

        try
        {
            return await _lspService.FindReferencesAsync(filePath, line, character, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationFindReferencesFailed));
            return [];
        }
    }

    private async Task SafeUpdateAsync(string filePath, CancellationToken ct)
    {
        try
        {
            await _indexer.UpdateFileAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationIncrementalUpdateFailed), filePath);
        }
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        _updateCts.Cancel();
        _updateCts.Dispose();

        if (_lspFileSync is not null)
        {
            _lspFileSync.DocumentChanged -= OnLspDocumentChanged;
        }
    }
}
