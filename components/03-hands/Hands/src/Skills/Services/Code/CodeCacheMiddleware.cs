namespace Core.Skills;

/// <summary>
/// 代码缓存中间件 — Generate/Analyze 操作的缓存检查与写入
/// </summary>
[Register]
public sealed partial class CodeCacheMiddleware : ICodeMiddleware
{
    [Inject] private readonly ICacheService _cacheService;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(CodeContext context, MiddlewareDelegate<CodeContext> next, CancellationToken ct)
    {
        // Execute 操作不使用缓存
        if (context.Operation == CodeOperation.Execute)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 缓存检查
        var cacheKey = CacheKeyGenerator.GenerateCacheKey(
            context.Operation == CodeOperation.Generate ? "generate_code" : "analyze_code",
            context.Input);

        var cachedResult = await _cacheService.GetAsync<string>(cacheKey, ct).ConfigureAwait(false);
        if (cachedResult != null)
        {
            context.Result = cachedResult;
            context.IsCached = true;
            return; // 缓存命中，短路
        }

        // 执行后续中间件
        await next(context, ct).ConfigureAwait(false);

        // 缓存写入
        if (context.Result is not null)
        {
            await _cacheService.SetAsync(cacheKey, context.Result, TimeSpan.FromHours(1), ct).ConfigureAwait(false);
        }
    }
}
