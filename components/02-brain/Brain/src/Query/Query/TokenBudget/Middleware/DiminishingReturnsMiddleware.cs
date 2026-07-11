using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 递减回报检测中间件 — 每次工具调用后检测递减回报
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class DiminishingReturnsMiddleware : IQueryMiddleware
{
    [Inject] private readonly IDiminishingReturnsDetector? _detector;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子检测递减回报
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_detector is not null)
        {
            context.AfterToolCallHooks.Add(CheckDiminishingReturnsAsync);
        }

        return next(context, ct);
    }

    private Task CheckDiminishingReturnsAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        if (context.RecentConsumptions.Count >= 3)
        {
            var result = _detector!.CheckDiminishingReturns(context.RecentConsumptions);
            if (result.IsDiminishing)
            {
                context.Logger?.LogWarning("[QueryEngine] 检测到递减回报: {Recommendation}", result.Recommendation);
                context.OutputChunks.Add(new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = $"⚠️ {result.Recommendation}" });
                context.ShouldBreak = true;
            }
        }

        return Task.CompletedTask;
    }
}
