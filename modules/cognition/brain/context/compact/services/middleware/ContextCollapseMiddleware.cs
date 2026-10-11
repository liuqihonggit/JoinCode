namespace Core.Context.Compact;

/// <summary>
/// 上下文折叠中间件 — 实验性功能，折叠旧上下文
/// </summary>
[Register(typeof(ICompactMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ContextCollapseMiddleware : ServiceEntity, ICompactMiddleware {

    /// <summary>
    /// 初始化 <see cref="ContextCollapseMiddleware"/> 实例
    /// </summary>
    /// <param name="contextCollapseService">上下文折叠服务（可选，为 null 时中间件直接透传）</param>
    /// <param name="logger">可选日志记录器</param>
    public ContextCollapseMiddleware(IContextCollapseService? contextCollapseService = null, ILogger<ContextCollapseMiddleware>? logger = null) {
        _contextCollapseService = contextCollapseService;
        _logger = logger;
    }
    private readonly IContextCollapseService? _contextCollapseService;
    private readonly ILogger<ContextCollapseMiddleware>? _logger;

    /// <summary>中间件异常时的行为：继续传递给下一个中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct) {
        if (_contextCollapseService == null) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        try {
            var content = string.Join("\n", context.Request.Messages.Select(m => m.Content ?? string.Empty));
            if (string.IsNullOrEmpty(content)) {
                await next(context, ct).ConfigureAwait(false);
                return;
            }

            var result = await _contextCollapseService.CollapseAsync(content, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Collapsed || result.CollapsedTokenCount >= result.OriginalTokenCount) {
                await next(context, ct).ConfigureAwait(false);
                return;
            }

            context.Result = new CompactResult {
                Compacted = true,
                Level = CompactLevel.Microcompact,
                Trigger = context.Request.Trigger,
                PreCompactTokenCount = context.PreCompactTokens,
                PostCompactTokenCount = context.PreCompactTokens - (result.OriginalTokenCount - result.CollapsedTokenCount),
                MessagesRemoved = 0,
                MessagesPreserved = context.Request.Messages.Count,
                Metadata = new Dictionary<string, JsonElement> {
                    ["collapseSegmentsCollapsed"] = JsonElementHelper.FromInt32(result.SegmentsCollapsed),
                    ["collapseSegmentsPreserved"] = JsonElementHelper.FromInt32(result.SegmentsPreserved),
                    ["collapseStrategy"] = JsonElementHelper.FromString(result.Strategy.ToString())
                }
            };
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ContextCollapse] 上下文折叠失败，继续下一个中间件");
        }

        if (!context.IsHandled) {
            await next(context, ct).ConfigureAwait(false);
        }
    }
}