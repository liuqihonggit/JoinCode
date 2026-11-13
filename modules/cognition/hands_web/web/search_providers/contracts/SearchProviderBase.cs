namespace Services.Web;

/// <summary>
/// 搜索供应商抽象基类 — 提供公共骨架（Stopwatch 计时 + try-catch 错误处理 + 日志记录）。
/// 子类只需实现 <see cref="SearchCoreAsync"/> 核心搜索逻辑，无需关心计时和异常包装。
/// 类似 DSH LlmAdapter 的"抽象类 + 默认实现 + 选择性覆写"模式。
/// </summary>
public abstract class SearchProviderBase : ISearchProvider, IAsyncDisposable {
    private readonly ILogger? _logger;

    /// <summary>
    /// 初始化 <see cref="SearchProviderBase"/> 实例。
    /// </summary>
    /// <param name="logger">可选日志记录器，子类传入用于错误日志。</param>
    protected SearchProviderBase(ILogger? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract int Priority { get; }

    /// <inheritdoc />
    public virtual bool IsAvailable => true;

    /// <summary>
    /// 子类可用的日志记录器。
    /// </summary>
    protected ILogger? Logger => _logger;

    /// <summary>
    /// 核心搜索逻辑 — 子类必须实现。基类负责 Stopwatch 计时和 try-catch 异常包装。
    /// 子类无需在返回结果中设置 <see cref="SearchProviderResult.DurationSeconds"/>，基类会自动覆写。
    /// 前置检查（如 API key 是否存在）失败时直接返回失败的 <see cref="SearchProviderResult"/> 即可。
    /// </summary>
    /// <param name="request">统一搜索请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>搜索结果。</returns>
    protected abstract Task<SearchProviderResult> SearchCoreAsync(SearchRequest request, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        try {
            var result = await SearchCoreAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return result with { DurationSeconds = stopwatch.Elapsed.TotalSeconds };
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "{ProviderName} 搜索失败: {Query}", Name, request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }

    /// <summary>
    /// 默认空实现 — 子类如有资源需要释放可覆写此方法。
    /// </summary>
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
