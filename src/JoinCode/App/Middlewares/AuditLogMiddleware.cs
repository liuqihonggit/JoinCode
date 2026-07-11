using JoinCode.Abstractions.Attributes;

namespace JoinCode.App.Middlewares;

/// <summary>
/// 审计日志中间件 — 记录对话交互摘要到日志
/// Order=40 在 ErrorHandling(30) 之后、业务中间件之前执行
/// 用户消息截断到 200 字符；AI 回复仅累积前 200 字符；记录工具调用和 TokenUsage
/// </summary>
[Register(typeof(Core.Context.IChatMiddleware))]
internal sealed partial class AuditLogMiddleware : Core.Context.IChatMiddleware
{
    private readonly ILogger<AuditLogMiddleware> _logger;
    private const int MaxAuditLength = 200;

    public AuditLogMiddleware(ILogger<AuditLogMiddleware> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var truncatedMessage = context.Message.Length > MaxAuditLength
            ? string.Concat(context.Message.AsSpan(0, MaxAuditLength), "...")
            : context.Message;
        _logger.LogInformation("[Audit] User (Turn={Turn}): {Message}", context.ConversationTurn, truncatedMessage);

        var responseChars = 0;
        var toolCallCount = 0;

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            switch (evt.Type)
            {
                case JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Content when evt.Content is not null && responseChars < MaxAuditLength:
                    var remaining = MaxAuditLength - responseChars;
                    var toTake = Math.Min(evt.Content.Length, remaining);
                    responseChars += toTake;
                    break;
                case JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallStart:
                    toolCallCount++;
                    _logger.LogInformation("[Audit] Tool: {ToolName}", evt.ToolName);
                    break;
                case JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Complete:
                    _logger.LogInformation("[Audit] Done: Model={Model}, Tokens={Tokens}",
                        evt.ModelId, evt.Usage);
                    break;
            }

            yield return evt;
        }

        _logger.LogInformation("[Audit] Assistant (Turn={Turn}): {Chars} chars, {Tools} tool calls",
            context.ConversationTurn, responseChars, toolCallCount);
    }
}
