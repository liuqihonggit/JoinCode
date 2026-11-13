namespace Services.Web;

/// <summary>
/// 网页获取供应商抽象基类 — 提供 try-catch 错误处理 + 日志记录的公共骨架。
/// 子类只需实现 <see cref="FetchCoreAsync"/> 核心获取逻辑，无需关心异常包装。
/// 类似 DSH LlmAdapter 的"抽象类 + 默认实现 + 选择性覆写"模式。
/// </summary>
public abstract class WebFetchProviderBase : IWebFetchProvider, IAsyncDisposable {
    private readonly ILogger? _logger;

    /// <summary>
    /// 初始化 <see cref="WebFetchProviderBase"/> 实例。
    /// </summary>
    /// <param name="logger">可选日志记录器，子类传入用于错误日志。</param>
    protected WebFetchProviderBase(ILogger? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual bool IsAvailable => true;

    /// <summary>
    /// 子类可用的日志记录器。
    /// </summary>
    protected ILogger? Logger => _logger;

    /// <summary>
    /// 核心获取逻辑 — 子类必须实现。基类负责 try-catch 异常包装和日志记录。
    /// 前置检查（如 API key 是否存在）失败时直接返回失败的 <see cref="WebFetchResult"/> 即可。
    /// </summary>
    /// <param name="url">待获取的网页 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>获取结果。</returns>
    protected abstract Task<WebFetchResult> FetchCoreAsync(string url, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default) {
        try {
            return await FetchCoreAsync(url, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "{ProviderName} Fetch 失败: {Url}", Name, url);
            return new WebFetchResult(false, url, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// 默认空实现 — 子类如有资源需要释放可覆写此方法。
    /// </summary>
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
