namespace Core.Query;

/// <summary>
/// 内容替换中间件 — 工具调用结果处理时执行内容替换和预算检查
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ContentReplacementMiddleware : ServiceEntity, IQueryMiddleware
{
    /// <summary>
    /// 构造函数 — 注入内容替换服务（可选）
    /// </summary>
    /// <param name="contentReplacementService">内容替换服务</param>
    public ContentReplacementMiddleware(IContentReplacementService? contentReplacementService = null)
    {
        _contentReplacementService = contentReplacementService;
    }
    private readonly IContentReplacementService? _contentReplacementService;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子执行内容替换预算检查，并将服务实例设置到上下文供核心引擎使用
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_contentReplacementService is not null)
        {
            context.ContentReplacementService = _contentReplacementService;
            context.AfterToolCallHooks.Add(ApplyToolResultBudgetAsync);
        }

        return next(context, ct);
    }

    private async Task ApplyToolResultBudgetAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        var contentReplacementService = _contentReplacementService ?? throw new InvalidOperationException("ContentReplacementService not available.");
        var state = context.Options?.ContentReplacementState;
        if (state is null)
            return;

        var sessionId = context.Options?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var neverPersistTools = context.Options?.NeverPersistTools;

        var (budgeted, newlyReplaced) = await contentReplacementService.ApplyToolResultBudgetAsync(
            context.ChatHistory, state, sessionId, neverPersistTools, ct).ConfigureAwait(false);

        if (newlyReplaced.Count > 0)
        {
            var writeToTranscript = context.Options?.WriteToTranscript;
            if (writeToTranscript is not null)
            {
                try
                {
                    writeToTranscript(newlyReplaced);
                }
                catch (Exception ex)
                {
                    context.Logger?.LogWarning(ex, "Failed to write content replacement records to transcript");
                }
            }
        }

        var hasChanges = newlyReplaced.Count > 0 || budgeted.Count != context.ChatHistory.Count;
        if (hasChanges)
        {
            context.ChatHistory.ReplaceAll(budgeted);
        }
    }
}
