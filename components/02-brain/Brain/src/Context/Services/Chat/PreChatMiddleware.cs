using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 预处理中间件 — 文件上下文、预处理、执行设置、历史快照
/// 对应原 ChatService.PrepareSendContextAsync + yield PromptInjectionInfo
/// 遥测已统一到管道 onPreExecute/onPostExecute 回调
/// </summary>
[Register]
public sealed partial class PreChatMiddleware : IChatMiddleware
{
    [Inject] private readonly IChatContextManager _contextManager;
    [Inject] private readonly IChatPreprocessor _preprocessor;
    [Inject] private readonly IChatFileContextService _fileContextService;
    [Inject] private readonly IChatOptionsFactory _optionsFactory;
    [Inject] private readonly ILogger<PreChatMiddleware>? _logger;


    /// <summary>
    /// 处理聊天事件流：文件上下文 → 预处理 → 执行设置 → 历史快照 → yield 注入信息 → 调用下游
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger?.LogInformation("正在发送聊天消息");

        context.Timing.StartTotal();
        context.Timing.StartPreprocess();

        _fileContextService.UpdateFileContext(context.Message);

        var preprocessResult = await _preprocessor.AnalyzeAndInjectAsync(context.Message, ct).ConfigureAwait(false);
        await _preprocessor.PrepareContextAsync(context.Message, context.IsDryRun, ct).ConfigureAwait(false);
        context.PreprocessResult = preprocessResult;

        context.ExecutionSettings = _optionsFactory.Create();
        context.PromptSnapshot = await _contextManager.RecordPromptStateAsync(ct).ConfigureAwait(false);

        context.Timing.StopPreprocess();

        if (!string.IsNullOrEmpty(preprocessResult.PromptInjectionInfo))
        {
            yield return ChatStreamEvent.Text(preprocessResult.PromptInjectionInfo + "\n\n");
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
