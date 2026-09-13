namespace Core.Context;

/// <summary>
/// 预处理中间件 — 文件上下文、预处理、执行设置、历史快照
/// 对应原 ChatService.PrepareSendContextAsync + yield PromptInjectionInfo
/// 遥测已统一到管道 onPreExecute/onPostExecute 回调
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PreChatMiddleware : ServiceEntity, IChatMiddleware
{

    /// <summary>
    /// 初始化预处理中间件
    /// </summary>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="preprocessor">聊天预处理器</param>
    /// <param name="fileContextService">文件上下文服务</param>
    /// <param name="optionsFactory">聊天选项工厂</param>
    /// <param name="emptyResponseTracker">空响应追踪器</param>
    /// <param name="logger">可选日志记录器</param>
    public PreChatMiddleware(IChatContextManager contextManager, IChatPreprocessor preprocessor, IChatFileContextService fileContextService, IChatOptionsFactory optionsFactory, IEmptyResponseTracker emptyResponseTracker, ILogger<PreChatMiddleware>? logger = null)
    {
        _contextManager = contextManager;
        _preprocessor = preprocessor;
        _fileContextService = fileContextService;
        _optionsFactory = optionsFactory;
        _emptyResponseTracker = emptyResponseTracker;
        _logger = logger;
    }
    private readonly IChatContextManager _contextManager;
    private readonly IChatPreprocessor _preprocessor;
    private readonly IChatFileContextService _fileContextService;
    private readonly IChatOptionsFactory _optionsFactory;
    private readonly IEmptyResponseTracker _emptyResponseTracker;
    private readonly ILogger<PreChatMiddleware>? _logger;


    /// <summary>
    /// 处理聊天事件流：文件上下文 → 预处理 → 执行设置 → 历史快照 → yield 注入信息 → 调用下游
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger?.LogInformation("正在发送聊天消息");

        _emptyResponseTracker.Reset();
        context.EmptyResponseTracker = _emptyResponseTracker;

        context.Timing.StartTotal();
        context.Timing.StartPreprocess();

        _fileContextService.UpdateFileContext(context.Message);

        var preprocessResult = await _preprocessor.AnalyzeAndInjectAsync(context.Message, ct).ConfigureAwait(false);
        await _preprocessor.PrepareContextAsync(context.Message, context.IsDryRun, ct).ConfigureAwait(false);
        context.PreprocessResult = preprocessResult;

        context.ExecutionSettings = _optionsFactory.Create();
        context.PromptSnapshot = await _contextManager.RecordPromptStateAsync(context.AgentId, ct).ConfigureAwait(false);

        context.Timing.StopPreprocess();

        if (!string.IsNullOrEmpty(preprocessResult.PromptInjectionInfo))
        {
            yield return ChatStreamEvent.Text(preprocessResult.PromptInjectionInfo + "\n\n");
        }

        if (!string.IsNullOrEmpty(context.ModalityMismatchInjection))
        {
            yield return ChatStreamEvent.Text(context.ModalityMismatchInjection + "\n");
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
