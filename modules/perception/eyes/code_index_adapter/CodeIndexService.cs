namespace Services.CodeIndex;

/// <summary>
/// 代码索引托管服务 — 启动时构建索引，集成文件监视器与 LSP，停止时优雅关闭
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed partial class CodeIndexService : IHostedService, IAsyncDisposable {
    private readonly ICodeIndexer _indexer;
    private readonly FileWatcherIntegration? _watcher;
    private readonly LspIntegration? _lspIntegration;
    private readonly CodeIndexOptions _options;
    private readonly ILogger<CodeIndexService>? _logger;
    private Task? _startTask;
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
        ILogger<CodeIndexService>? logger = null) {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _watcher = watcher;
        _lspIntegration = lspIntegration;
        _logger = logger;
    }

    /// <summary>
    /// 启动托管服务 — 加载已有索引（不自动构建），启动文件监视器与 LSP
    /// 索引构建请用斜杠命令 /code-index 显式触发
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
    public async Task StartAsync(CancellationToken cancellationToken) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger?.LogInformation(L.T(StringKey.CodeIndexServiceWorkspace), _options.WorkspaceRoot);

        try {
            await _indexer.EnsureIndexLoadedAsync(cancellationToken).ConfigureAwait(false);

            if (_watcher is not null) {
                await _watcher.StartAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation(L.T(StringKey.CodeIndexWatcherStarted));
            }

            if (_lspIntegration is not null) {
                _logger?.LogInformation(L.T(StringKey.CodeIndexLspReady), _lspIntegration.IsLspAvailable);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.CodeIndexStartFailed));
            throw;
        }
    }

    /// <summary>
    /// 等待后台启动完成 — 工具调用方在使用 CodeIndex 前调用此方法确保索引已加载
    /// </summary>
    public Task WaitForStartAsync() => _startTask ?? Task.CompletedTask;

    /// <summary>
    /// 设置后台启动 Task — 由 SessionInitStep 调用，工具调用方通过 WaitForStartAsync 等待
    /// </summary>
    public void SetStartTask(Task task) => _startTask = task;

    /// <summary>
    /// 停止托管服务 — 停止文件监视器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public async Task StopAsync(CancellationToken cancellationToken) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger?.LogInformation(L.T(StringKey.CodeIndexServiceStopped));

        if (_watcher is not null) {
            await _watcher.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步释放资源 — 释放文件监视器、LSP 集成与索引器
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        if (_startTask is not null) {
            try {
                await _startTask.ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "CodeIndexService 后台启动失败，释放时忽略");
            }
        }

        _lspIntegration?.Dispose();
        (_indexer as IDisposable)?.Dispose();
        if (_watcher is not null) {
            await _watcher.DisposeAsync().ConfigureAwait(false);
        }
    }
}