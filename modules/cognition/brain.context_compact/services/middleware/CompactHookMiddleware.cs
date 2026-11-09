namespace Core.Context.Compact;

/// <summary>
/// 压缩钩子中间件 — 执行 pre-compact / post-compact hooks
/// </summary>
[Register(typeof(ICompactMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CompactHookMiddleware : ServiceEntity, ICompactMiddleware {

    /// <summary>
    /// 初始化 <see cref="CompactHookMiddleware"/> 实例
    /// </summary>
    /// <param name="microcompactService">微压缩服务，用于估算消息 token 数</param>
    /// <param name="compactHookManager">可选的压缩钩子管理器，为 null 时跳过钩子执行</param>
    public CompactHookMiddleware(IMicrocompactService microcompactService, ICompactHookManager? compactHookManager = null) {
        _microcompactService = microcompactService;
        _compactHookManager = compactHookManager;
    }
    private readonly IMicrocompactService _microcompactService;
    private readonly ICompactHookManager? _compactHookManager;

    /// <summary>中间件异常时的行为：继续传递给下一个中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct) {
        if (_compactHookManager != null) {
            var hookContext = new CompactHookContext {
                SessionId = "unknown",
                Trigger = context.Request.Trigger.ToString(),
                CurrentTokenCount = _microcompactService.EstimateMessageTokens(context.Request.Messages),
                TargetTokenCount = 0
            };
            var hookResult = await _compactHookManager.OnPreCompactAsync(hookContext, ct).ConfigureAwait(false);
            if (hookResult.Action == CompactHookAction.Skip) {
                context.Result = new CompactResult {
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
        if (_compactHookManager != null && context.Result is { Compacted: true }) {
            var postContext = new CompactHookContext {
                SessionId = "unknown",
                Trigger = context.Request.Trigger.ToString(),
                CurrentTokenCount = context.Result.PreCompactTokenCount,
                TargetTokenCount = context.Result.PostCompactTokenCount
            };
            var postData = new PostCompactData {
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