namespace JoinCode.Dream.Pipeline;

/// <summary>
/// 做梦 LLM 整合中间件 — 用聊天补全客户端对系统提示与用户提示做整合，写入整合结果到上下文
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamLlmConsolidateMiddleware : ServiceEntity, IDreamMiddleware {
    private readonly IChatCompletionClient _chatCompletionClient;

    /// <summary>
    /// 构造做梦 LLM 整合中间件
    /// </summary>
    /// <param name="chatCompletionClient">聊天补全客户端</param>
    public DreamLlmConsolidateMiddleware(IChatCompletionClient chatCompletionClient) {
        _chatCompletionClient = chatCompletionClient;
    }

    /// <summary>
    /// 执行中间件 — 构造聊天历史并调用 LLM 整合，结果写入上下文后传递给下一中间件
    /// </summary>
    /// <param name="ctx">做梦上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步执行操作的任务</returns>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct) {
        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(ctx.SystemPrompt ?? throw new InvalidOperationException("SystemPrompt is not set. Ensure DreamPromptBuildMiddleware runs first."));
        chatHistory.AddUserMessage(ctx.UserPrompt ?? throw new InvalidOperationException("UserPrompt is not set. Ensure DreamPromptBuildMiddleware runs first."));

        ctx.ConsolidationResult = await _chatCompletionClient.GetCompletionAsync(chatHistory, ct).ConfigureAwait(false);
        ctx.LlmCompleted = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}