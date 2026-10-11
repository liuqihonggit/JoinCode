namespace Core.Query;

/// <summary>
/// 递减回报检测中间件 — 每次工具调用后检测递减回报
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DiminishingReturnsMiddleware : ServiceEntity, IQueryMiddleware {
    /// <summary>
    /// 构造函数 — 注入递减回报检测器（可选）
    /// </summary>
    /// <param name="detector">递减回报检测器</param>
    public DiminishingReturnsMiddleware(IDiminishingReturnsDetector? detector = null) {
        _detector = detector;
    }
    private readonly IDiminishingReturnsDetector? _detector;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子检测递减回报
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        if (_detector is not null) {
            context.AfterToolCallHooks.Add(CheckDiminishingReturnsAsync);
        }

        return next(context, ct);
    }

    private Task CheckDiminishingReturnsAsync(QueryMiddlewareContext context, CancellationToken ct) {
        var detector = _detector ?? throw new InvalidOperationException("DiminishingReturnsDetector not available.");
        if (context.RecentConsumptions.Count >= 3) {
            var result = detector.CheckDiminishingReturns(context.RecentConsumptions);
            if (result.IsDiminishing) {
                context.Logger?.LogWarning("[QueryEngine] 检测到递减回报: {Recommendation}", result.Recommendation);
                context.OutputChunks.Add(new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = $"⚠️ {result.Recommendation}" });
                context.ShouldBreak = true;
            }
        }

        return Task.CompletedTask;
    }
}