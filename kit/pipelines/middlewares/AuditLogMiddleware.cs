namespace JoinCode.Pipelines.Middlewares;

/// <summary>
/// 审计日志中间件 — 记录对话交互摘要到日志
/// Order=40 在 ErrorHandling(30) 之后、业务中间件之前执行
/// 用户消息截断到 200 字符；AI 回复仅累积前 200 字符；记录工具调用和 TokenUsage
/// </summary>
internal sealed partial class AuditLogMiddleware : ServiceEntity, Core.Context.IChatMiddleware {
    private readonly ILogger<AuditLogMiddleware> _logger;
    private const int MaxAuditLength = 200;

    /// <summary>构造 AuditLogMiddleware。</summary>
    /// <param name="logger">日志记录器</param>
    public AuditLogMiddleware(ILogger<AuditLogMiddleware> logger) {
        _logger = logger;
    }

    /// <summary>执行中间件。</summary>
    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
        var truncatedMessage = TruncateForAudit(context.Message, MaxAuditLength);
        _logger.LogInformation("[Audit] User (Turn={Turn}): {Message}", context.ConversationTurn, truncatedMessage);

        var responseChars = 0;
        var toolCallCount = 0;

        await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
            var (newChars, isToolCall) = AccumulateEvent(evt, responseChars, MaxAuditLength);
            responseChars = newChars;
            if (isToolCall) {
                toolCallCount++;
                _logger.LogInformation("[Audit] Tool: {ToolName}", evt.ToolName);
            }
            if (evt.Type == JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Complete) {
                _logger.LogInformation("[Audit] Done: Model={Model}, Tokens={Tokens}",
                    evt.ModelId, evt.Usage);
            }

            yield return evt;
        }

        _logger.LogInformation("[Audit] Assistant (Turn={Turn}): {Chars} chars, {Tools} tool calls",
            context.ConversationTurn, responseChars, toolCallCount);
    }

    /// <summary>
    /// 截断审计消息到指定长度,超长则尾部追加 "..."。
    /// 纯计算,不依赖时序/IO,可确定性测试。
    /// </summary>
    /// <param name="message">原始消息。</param>
    /// <param name="maxLen">最大保留长度(超出此长度才截断)。</param>
    /// <returns>截断后的消息;超长时为前 maxLen 字符 + "...",否则原样返回。</returns>
    internal static string TruncateForAudit(string message, int maxLen) {
        return message.Length > maxLen
            ? string.Concat(message.AsSpan(0, maxLen), "...")
            : message;
    }

    /// <summary>
    /// 累积单个流事件,返回新的字符计数与是否为工具调用标记。
    /// 纯计算,不依赖时序/IO,可确定性测试。
    /// </summary>
    /// <param name="evt">当前流事件。</param>
    /// <param name="currentChars">已累积的字符数。</param>
    /// <param name="maxLen">最大累积字符数。</param>
    /// <returns>(新字符数, 是否为工具调用开始事件)。</returns>
    internal static (int newResponseChars, bool isToolCall) AccumulateEvent(
        JoinCode.Abstractions.LLM.Chat.ChatStreamEvent evt, int currentChars, int maxLen) {
        if (evt.Type == JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Content
            && evt.Content is not null
            && currentChars < maxLen) {
            var remaining = maxLen - currentChars;
            var toTake = Math.Min(evt.Content.Length, remaining);
            return (currentChars + toTake, false);
        }
        if (evt.Type == JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallStart) {
            return (currentChars, true);
        }
        return (currentChars, false);
    }
}