using JoinCode.Abstractions.Attributes;

namespace Core.Context.Compact;

/// <summary>
/// 压缩钩子中间件 — 执行 pre-compact / post-compact hooks
/// </summary>
[Register(typeof(ICompactMiddleware))]
public sealed partial class CompactHookMiddleware : ICompactMiddleware
{
    [Inject] private readonly IMicrocompactService _microcompactService;
    [Inject] private readonly ICompactHookManager? _compactHookManager;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        if (_compactHookManager != null)
        {
            var hookContext = new CompactHookContext
            {
                SessionId = "unknown",
                Trigger = context.Request.Trigger.ToString(),
                CurrentTokenCount = _microcompactService.EstimateMessageTokens(context.Request.Messages),
                TargetTokenCount = 0
            };
            var hookResult = await _compactHookManager.OnPreCompactAsync(hookContext, ct).ConfigureAwait(false);
            if (hookResult.Action == CompactHookAction.Skip)
            {
                context.Result = new CompactResult
                {
                    Compacted = false,
                    Level = CompactLevel.None,
                    Trigger = context.Request.Trigger,
                    PreCompactTokenCount = hookContext.CurrentTokenCount,
                    PostCompactTokenCount = hookContext.CurrentTokenCount,
                    ErrorMessage = "Hook 跳过压缩"
                };
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);

        // Post hook: 在管道执行完毕后调用
        if (_compactHookManager != null && context.Result is { Compacted: true })
        {
            var postContext = new CompactHookContext
            {
                SessionId = "unknown",
                Trigger = context.Request.Trigger.ToString(),
                CurrentTokenCount = context.Result.PreCompactTokenCount,
                TargetTokenCount = context.Result.PostCompactTokenCount
            };
            var postData = new PostCompactData
            {
                Level = context.Result.Level.ToString(),
                Trigger = context.Result.Trigger.ToString(),
                PreCompactTokenCount = context.Result.PreCompactTokenCount,
                PostCompactTokenCount = context.Result.PostCompactTokenCount,
                MessagesRemoved = context.Result.MessagesRemoved
            };
            await _compactHookManager.OnPostCompactAsync(postContext, postData, ct).ConfigureAwait(false);
        }
    }
}
