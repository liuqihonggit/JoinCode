namespace JoinCode.Dream.Pipeline;

[Register]
public sealed partial class DreamLlmConsolidateMiddleware : IDreamMiddleware
{
    private readonly IChatCompletionClient _chatCompletionClient;

    public DreamLlmConsolidateMiddleware(IChatCompletionClient chatCompletionClient)
    {
        _chatCompletionClient = chatCompletionClient;
    }

    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(ctx.SystemPrompt ?? throw new InvalidOperationException("SystemPrompt is not set. Ensure DreamPromptBuildMiddleware runs first."));
        chatHistory.AddUserMessage(ctx.UserPrompt ?? throw new InvalidOperationException("UserPrompt is not set. Ensure DreamPromptBuildMiddleware runs first."));

        ctx.ConsolidationResult = await _chatCompletionClient.GetCompletionAsync(chatHistory, ct).ConfigureAwait(false);
        ctx.LlmCompleted = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
