namespace Core.Skills;

/// <summary>
/// 代码 LLM 中间件 — Generate/Analyze 操作的 LLM 调用
/// </summary>
[Register]
public sealed partial class CodeLlmMiddleware : ICodeMiddleware
{
    private readonly JoinCode.Abstractions.LLM.IQueryService _queryService;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 CodeLlmMiddleware
    /// </summary>
    public CodeLlmMiddleware(JoinCode.Abstractions.LLM.IQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(CodeContext context, MiddlewareDelegate<CodeContext> next, CancellationToken ct)
    {
        // 仅 Generate/Analyze 操作使用 LLM
        if (context.Operation == CodeOperation.Execute)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var chatHistory = new MessageList();

        if (context.Operation == CodeOperation.Generate)
        {
            chatHistory.AddSystemMessage(HandsPromptTemplates.GetContent("code_generation")!);
            chatHistory.AddUserMessage(L.T(StringKey.CodeServiceGenerateCodePrompt, context.Input));
        }
        else
        {
            chatHistory.AddSystemMessage(HandsPromptTemplates.GetContent("code_analysis")!);
            chatHistory.AddUserMessage(L.T(StringKey.CodeServiceAnalyzeCodePrompt, context.Input));
        }

        var executionSettings = new ChatOptions
        {
            Temperature = context.Operation == CodeOperation.Generate ? 0.7f : 0.5f,
            MaxTokens = context.Operation == CodeOperation.Generate ? 2000 : 1500,
            TopP = 0.95f
        };

        var results = await _queryService.GetApiMessageContentsAsync(chatHistory, executionSettings, cancellationToken: ct).ConfigureAwait(false);
        context.Result = results[0].Content ?? L.T(
            context.Operation == CodeOperation.Generate ? StringKey.CodeServiceGenerateCodeFailed : StringKey.CodeServiceAnalyzeCodeFailed);

        await next(context, ct).ConfigureAwait(false);
    }
}
