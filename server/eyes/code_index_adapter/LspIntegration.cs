namespace Services.CodeIndex;

/// <summary>
/// LSP 集成服务 — 将 LSP 文件同步事件桥接到代码索引增量更新，并提供定义/引用查询
/// </summary>
[Register(typeof(LspIntegration), ServiceLifetime.Singleton)]
public sealed partial class LspIntegration : ServiceEntity, IDisposable {
    private readonly ICodeIndexer _indexer;
    private readonly ILspService? _lspService;
    private readonly ILspFileSync? _lspFileSync;
    private readonly ILogger<LspIntegration>? _logger;
    private readonly CancellationTokenSource _updateCts = new();
    private int _disposed;

    /// <summary>
    /// 构造 LSP 集成服务
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="lspService">LSP 服务 — null 表示 LSP 不可用</param>
    /// <param name="lspFileSync">LSP 文件同步 — null 表示不订阅文档变更</param>
    /// <param name="logger">日志器 — null 表示不记录日志</param>
    public LspIntegration(ICodeIndexer indexer, ILspService? lspService = null, ILspFileSync? lspFileSync = null, ILogger<LspIntegration>? logger = null) {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _lspService = lspService;
        _lspFileSync = lspFileSync;
        _logger = logger;

        if (_lspFileSync is not null) {
            _lspFileSync.DocumentChanged += OnLspDocumentChanged;
        }
    }

    /// <summary>LSP 是否可用 — 是否注入了 LSP 服务</summary>
    public bool IsLspAvailable => _lspService is not null;

    private void OnLspDocumentChanged(object? sender, DocumentChangedEventArgs e) {
        if (_disposed != 0) return;

        _ = SafeUpdateAsync(e.FilePath, _updateCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _updateCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 文档变更回调 — 增量更新索引
    /// </summary>
    /// <param name="filePath">变更文件路径</param>
    /// <param name="ct">取消令牌</param>
    public async Task OnDocumentChangedAsync(string filePath, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePath);
        await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 文档保存回调 — 增量更新索引
    /// </summary>
    /// <param name="filePath">保存文件路径</param>
    /// <param name="ct">取消令牌</param>
    public async Task OnDocumentSavedAsync(string filePath, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePath);
        await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 监视文件变更回调 — 批量增量更新索引
    /// </summary>
    /// <param name="filePaths">变更文件路径集合</param>
    /// <param name="ct">取消令牌</param>
    public async Task OnWatchedFilesChangedAsync(IEnumerable<string> filePaths, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(filePaths);
        foreach (var filePath in filePaths) {
            await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 尝试查找定义 — LSP 不可用时返回空列表
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0 基）</param>
    /// <param name="character">列号（0 基）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>定义位置列表 — 失败时返回空列表</returns>
    public async Task<List<LspLocation>> TryFindDefinitionAsync(string filePath, int line, int character, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_lspService is null) {
            return [];
        }

        try {
            return await _lspService.GotoDefinitionAsync(filePath, line, character, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationGotoDefinitionFailed));
            return [];
        }
    }

    /// <summary>
    /// 尝试查找引用 — LSP 不可用时返回空列表
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0 基）</param>
    /// <param name="character">列号（0 基）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>引用位置列表 — 失败时返回空列表</returns>
    public async Task<List<LspLocation>> TryFindReferencesAsync(string filePath, int line, int character, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_lspService is null) {
            return [];
        }

        try {
            return await _lspService.FindReferencesAsync(filePath, line, character, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationFindReferencesFailed));
            return [];
        }
    }

    private async Task SafeUpdateAsync(string filePath, CancellationToken ct) {
        try {
            await _indexer.UpdateFileAsync(filePath, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.LspIntegrationIncrementalUpdateFailed), filePath);
        }
    }

    /// <summary>
    /// 释放资源 — 取消更新令牌、取消订阅文件同步事件
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _updateCts.Cancel();
        _updateCts.Dispose();

        if (_lspFileSync is not null) {
            _lspFileSync.DocumentChanged -= OnLspDocumentChanged;
        }
        base.Dispose();
    }
}