using JoinCode.Abstractions.Attributes;

namespace Services.CodeIndex;

[Register(typeof(IHostedService))]
public sealed partial class CodeIndexService : IHostedService, IDisposable
{
    private readonly ICodeIndexer _indexer;
    private readonly FileWatcherIntegration? _watcher;
    private readonly LspIntegration? _lspIntegration;
    private readonly CodeIndexOptions _options;
    [Inject] private readonly ILogger<CodeIndexService>? _logger;
    private int _disposed;

    public CodeIndexService(
        ICodeIndexer indexer,
        CodeIndexOptions options,
        FileWatcherIntegration? watcher = null,
        LspIntegration? lspIntegration = null,
        ILogger<CodeIndexService>? logger = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _watcher = watcher;
        _lspIntegration = lspIntegration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger?.LogInformation(L.T(StringKey.CodeIndexServiceWorkspace), _options.WorkspaceRoot);

        try
        {
            var result = await _indexer.BuildIndexAsync(_options, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(L.T(StringKey.CodeIndexBuildCompleted),
                result.UpdatedCount, result.SkippedCount, result.DeletedCount);

            if (_watcher is not null)
            {
                await _watcher.StartAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation(L.T(StringKey.CodeIndexWatcherStarted));
            }

            if (_lspIntegration is not null)
            {
                _logger?.LogInformation(L.T(StringKey.CodeIndexLspReady), _lspIntegration.IsLspAvailable);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.CodeIndexStartFailed));
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger?.LogInformation(L.T(StringKey.CodeIndexServiceStopped));

        if (_watcher is not null)
        {
            await _watcher.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watcher?.Dispose();
        _lspIntegration?.Dispose();
        (_indexer as IDisposable)?.Dispose();
    }
}
