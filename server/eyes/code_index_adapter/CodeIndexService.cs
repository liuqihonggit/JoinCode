namespace Services.CodeIndex;

/// <summary>
/// 代码索引托管服务 — 启动时构建索引，集成文件监视器与 LSP，停止时优雅关闭
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed partial class CodeIndexService : IHostedService, IAsyncDisposable
{
    private readonly ICodeIndexer _indexer;
    private readonly FileWatcherIntegration? _watcher;
    private readonly LspIntegration? _lspIntegration;
    private readonly CodeIndexOptions _options;
    private readonly ILogger<CodeIndexService>? _logger;
    private int _disposed;

    /// <summary>
    /// 构造代码索引托管服务
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="options">代码索引选项</param>
    /// <param name="watcher">文件监视器集成（可选）</param>
    /// <param name="lspIntegration">LSP 集成（可选）</param>
    /// <param name="logger">日志记录器</param>
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

    /// <summary>
    /// 启动托管服务 — 构建索引并启动文件监视器与 LSP
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
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

    /// <summary>
    /// 停止托管服务 — 停止文件监视器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger?.LogInformation(L.T(StringKey.CodeIndexServiceStopped));

        if (_watcher is not null)
        {
            await _watcher.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步释放资源 — 释放文件监视器、LSP 集成与索引器
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed))
        {
            return ValueTask.CompletedTask;
        }

        _lspIntegration?.Dispose();
        (_indexer as IDisposable)?.Dispose();
        return _watcher?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
