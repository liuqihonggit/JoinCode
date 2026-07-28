using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 内容替换中间件 — 工具调用结果处理时执行内容替换和预算检查
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class ContentReplacementMiddleware : IQueryMiddleware
{
    [Inject] private readonly IContentReplacementService? _contentReplacementService;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子执行内容替换预算检查，并将服务实例设置到上下文供核心引擎使用
    /// </summary>
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

        var sessionId = context.Options?.SessionId ?? "default";
        var neverPersistTools = context.Options?.NeverPersistTools;

        var (budgeted, newlyReplaced) = await contentReplacementService.ApplyToolResultBudgetAsync(
            context.ChatHistory.ToList(), state, sessionId, neverPersistTools, ct).ConfigureAwait(false);

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
