namespace Core.DependencyInjection;

/// <summary>
/// 延迟初始化的预处理器代理 — DI 没有 async 工厂重载，注册阶段不能 await；
/// 把异步 profile 链解析推迟到首次调用（调用方本身都是 async），避免同步阻塞装配。
/// <para>失败语义：profile 声明缺失/节点装配失败会在首次预处理时抛出（首请求即暴露），
/// 不再阻塞 DI 解析本身。</para>
/// </summary>
internal sealed class DeferredChatPreprocessor : IChatPreprocessor {
    private readonly Lazy<Task<IChatPreprocessor>> _inner;

    /// <summary>
    /// 构造函数 — 传入一次性的异步解析工厂。
    /// </summary>
    /// <param name="factory">异步创建真实预处理器的工厂。</param>
    public DeferredChatPreprocessor(Func<Task<IChatPreprocessor>> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _inner = new Lazy<Task<IChatPreprocessor>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc/>
    public async Task<PreprocessResult> AnalyzeAndInjectAsync(string message, CancellationToken ct) {
        var inner = await _inner.Value.ConfigureAwait(false);
        return await inner.AnalyzeAndInjectAsync(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PrepareContextAsync(string message, bool isDryRun = false, CancellationToken ct = default) {
        var inner = await _inner.Value.ConfigureAwait(false);
        await inner.PrepareContextAsync(message, isDryRun, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CleanupInjectionsAsync(
        UserPromptKeywordResult keywordResult, List<string> synonymInjectionIds, CancellationToken ct) {
        var inner = await _inner.Value.ConfigureAwait(false);
        await inner.CleanupInjectionsAsync(keywordResult, synonymInjectionIds, ct).ConfigureAwait(false);
    }
}
