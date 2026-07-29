using JoinCode.Abstractions.Attributes;

namespace Core.Context.Compact;

/// <summary>
/// 微压缩中间件 — 时间间隔压缩 + 工具结果清理
/// </summary>
[Register(typeof(ICompactMiddleware))]
public sealed partial class MicrocompactMiddleware : ICompactMiddleware
{
    [Inject] private readonly IMicrocompactService _microcompactService;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        var preCompactTokens = _microcompactService.EstimateMessageTokens(context.Request.Messages);
        context.PreCompactTokens = preCompactTokens;

        var timeBasedResult = _microcompactService.TimeBasedCompact(context.Request.Messages);
        if (timeBasedResult is not null && timeBasedResult.TokensSaved > 0)
        {
            var saved = timeBasedResult.TokensSaved;
            context.Result = new CompactResult
            {
                Compacted = true,
                Level = CompactLevel.TimeBasedMicrocompact,
                Trigger = context.Request.Trigger,
                PreCompactTokenCount = preCompactTokens,
                PostCompactTokenCount = preCompactTokens - saved,
                MessagesRemoved = 0,
                MessagesPreserved = context.Request.Messages.Count,
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["gapMinutes"] = JsonElementHelper.FromDouble(timeBasedResult.GapMinutes),
                    ["toolsCleared"] = JsonElementHelper.FromInt32(timeBasedResult.ToolsCleared),
                    ["toolsKept"] = JsonElementHelper.FromInt32(timeBasedResult.ToolsKept),
                    ["tokensSaved"] = JsonElementHelper.FromInt32(timeBasedResult.TokensSaved)
                }
            };
            return;
        }

        var microResult = _microcompactService.CompactMessages(context.Request.Messages);
        if (microResult.WasCompacted && microResult.TokensSaved > 0)
        {
            context.Result = new CompactResult
            {
                Compacted = true,
                Level = CompactLevel.Microcompact,
                Trigger = context.Request.Trigger,
                PreCompactTokenCount = preCompactTokens,
                PostCompactTokenCount = preCompactTokens - microResult.TokensSaved,
                MessagesRemoved = 0,
                MessagesPreserved = context.Request.Messages.Count,
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["toolsCleared"] = JsonElementHelper.FromInt32(microResult.ToolsCleared),
                    ["tokensSaved"] = JsonElementHelper.FromInt32(microResult.TokensSaved)
                }
            };
            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
