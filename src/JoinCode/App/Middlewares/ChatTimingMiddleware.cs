using JoinCode.Abstractions.Attributes;

namespace JoinCode.App.Middlewares;

/// <summary>
/// 聊天计时中间件 — 管道完成后输出计时摘要，JCC_VERBOSE 环境变量控制输出
/// Order=10 确保包裹全部业务中间件
/// 注意：StartTotal/StopTotal 由 PreChatMiddleware(100) 和 SaveContextMiddleware(330) 负责，
/// 本中间件仅负责在管道最外层输出 TimingSummary 事件
/// </summary>
[Register(typeof(Core.Context.IChatMiddleware))]
internal sealed partial class ChatTimingMiddleware : Core.Context.IChatMiddleware
{
    private readonly bool _verbose;

    public ChatTimingMiddleware()
    {
        _verbose = Diag.IsVerbose;
    }

    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        if (_verbose)
        {
            yield return JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.TimingSummary(context.Timing.FormatSummary(context.FinalUsage));
        }
    }
}
