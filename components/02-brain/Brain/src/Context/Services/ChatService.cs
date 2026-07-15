
namespace Core.Context;

[Register]
public partial class ChatService : IChatService {
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<ChatService>? _logger;
    private readonly IAsyncLazy<int> _initLazy;
    private int _conversationTurn;

    /// <summary>
    /// 聊天中间件管道 — 由通用 StreamMiddlewarePipeline 构建
    /// </summary>
    private readonly StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent> _middlewarePipeline;

    /// <summary>
    /// 管理操作中间件管道 — 由通用 MiddlewarePipeline 构建
    /// </summary>
    private readonly MiddlewarePipeline<ChatAdminContext> _adminPipeline;

    /// <summary>
    /// 工具执行上下文 — 对齐 TS ToolUseContext
    /// 维护技能执行时 contextModifier 修改的会话级状态（allowedTools/model/effort）
    /// 以及内容替换状态（contentReplacementState）
    /// </summary>
    private readonly ToolUseContext _toolUseContext = new();

    /// <summary>
    /// 文件读取监听器订阅 — 用于追踪最近读取的文件，压缩后恢复上下文
    /// </summary>
    private IDisposable? _fileReadListenerSubscription;

    public ChatService(
        IChatContextManager contextManager,
        StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent> middlewarePipeline,
        MiddlewarePipeline<ChatAdminContext> adminPipeline,
        IFileReadListenerRegistry? fileReadListenerRegistry = null,
        ILogger<ChatService>? logger = null)
    {
        _contextManager = contextManager;
        _middlewarePipeline = middlewarePipeline;
        _adminPipeline = adminPipeline;
        _logger = logger;

        // 对齐 TS: registerFileReadListener — 追踪最近读取的文件
        if (fileReadListenerRegistry is not null)
        {
            _fileReadListenerSubscription = fileReadListenerRegistry.Register(
                new FileReadTracker(_toolUseContext));
        }

        _initLazy = new AsyncLazy<int>(InitializeCoreAsync);
    }

    /// <summary>
    /// 初始化聊天服务核心组件，由 AsyncLazy 延迟调用
    /// 通过管理操作管道执行 Initialize 操作
    /// </summary>
    private async Task<int> InitializeCoreAsync() {
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.Initialize,
            ContextManager = _contextManager,
            ToolUseContext = _toolUseContext,
        };
        await _adminPipeline.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// 确保聊天服务已初始化，若未初始化则执行一次性初始化
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default) {
        await _initLazy.GetValueAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以纯文本流式方式发送消息，逐字符返回 AI 响应。
    /// 不支持工具调用迭代，适用于简单问答场景。
    /// 通过中间件管道执行，从 ChatStreamEvent 中提取文本内容。
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreamAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatMiddlewareContext { Message = message, SpanName = "chat.send.stream", ConversationTurn = _conversationTurn, ToolUseContext = _toolUseContext };

        try
        {
            await foreach (var evt in _middlewarePipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false))
            {
                if (evt.Type == ChatStreamEventType.Content && evt.Content is not null)
                {
                    yield return evt.Content;
                }
            }
        }
        finally
        {
            await CleanupAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 以事件流方式发送消息，支持工具调用迭代，返回结构化事件。
    /// 每轮工具调用产生 ToolStart/ToolEnd 事件，最终产生 Done 事件。
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatMiddlewareContext { Message = message, SpanName = "chat.send.events", ConversationTurn = _conversationTurn, ToolUseContext = _toolUseContext };

        try
        {
            await foreach (var evt in _middlewarePipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            await CleanupAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 以同步方式发送消息，等待完整响应后返回。
    /// 不支持工具调用迭代，适用于不需要流式输出的场景。
    /// 通过中间件管道执行，收集所有文本内容后返回。
    /// </summary>
    public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default) {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatMiddlewareContext { Message = message, SpanName = "chat.send.sync", ConversationTurn = _conversationTurn, ToolUseContext = _toolUseContext };

        try
        {
            var responseBuilder = new StringBuilder();

            await foreach (var evt in _middlewarePipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false))
            {
                if (evt.Type == ChatStreamEventType.Content && evt.Content is not null)
                {
                    responseBuilder.Append(evt.Content);
                }
            }

            var aiResponse = responseBuilder.ToString();
            if (string.IsNullOrEmpty(aiResponse))
            {
                aiResponse = "抱歉，我无法生成回复。";
            }

            var injectionInfo = context.PreprocessResult?.PromptInjectionInfo;
            return !string.IsNullOrEmpty(injectionInfo)
                ? $"{injectionInfo}\n\n{aiResponse}"
                : aiResponse;
        }
        finally
        {
            await CleanupAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 清理逻辑 — 递增对话轮次
    /// 用量处理、清理注入、保存上下文已移入独立中间件
    /// </summary>
    private Task CleanupAsync(ChatMiddlewareContext context)
    {
        Interlocked.Increment(ref _conversationTurn);
        return Task.CompletedTask;
    }

    // === 管理操作 — 通过 admin 管道执行 ===

    /// <summary>
    /// 清空聊天历史记录，保留系统提示词，重置会话统计和空闲检测状态
    /// </summary>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.ClearHistory,
            ContextManager = _contextManager,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException(CoreErrorMessages.ClearMessageListFailed, context.Error);
    }

    /// <summary>
    /// 获取当前会话的消息列表，转换为 API 记录格式
    /// </summary>
    public async Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.GetMessageList,
            ContextManager = _contextManager,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        return context.MessageList ?? throw new InvalidOperationException("MessageList is not set after pipeline execution.");
    }

    /// <summary>
    /// 更新系统提示词并持久化到上下文
    /// </summary>
    public async Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.SetSystemPrompt,
            ContextManager = _contextManager,
            SystemPrompt = systemPrompt,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("设置系统提示词失败", context.Error);
    }

    /// <summary>
    /// 撤回最后一轮对话（用户消息 + 助手回复）
    /// </summary>
    public async Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.RewindLastTurn,
            ContextManager = _contextManager,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("撤回对话失败", context.Error);
        return context.RewindResult ?? throw new InvalidOperationException("RewindResult is not set after pipeline execution.");
    }

    /// <summary>
    /// 撤回到指定消息索引位置，移除该索引之后的所有消息
    /// </summary>
    public async Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.RewindToMessageIndex,
            ContextManager = _contextManager,
            MessageIndex = messageIndex,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("撤回对话失败", context.Error);
        return context.RewindResult ?? throw new InvalidOperationException("RewindResult is not set after pipeline execution.");
    }

    /// <summary>
    /// 撤回到会话初始状态，清空所有消息并重置会话统计
    /// </summary>
    public async Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.RewindToStart,
            ContextManager = _contextManager,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("撤回对话失败", context.Error);
        return context.RewindResult ?? throw new InvalidOperationException("RewindResult is not set after pipeline execution.");
    }

    /// <summary>
    /// 加载历史消息到当前会话，先清空现有消息再逐条注入
    /// </summary>
    public async Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.LoadSessionMessages,
            ContextManager = _contextManager,
            Messages = messages,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("加载会话消息失败", context.Error);
    }

    /// <summary>
    /// 压缩对话历史，用摘要替代原始消息，保留系统提示词和已调用技能附件
    /// </summary>
    public async Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.CompactHistory,
            ContextManager = _contextManager,
            Summary = summary,
            ToolUseContext = _toolUseContext,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
            throw new WorkflowException("压缩对话历史失败", context.Error);
    }

    /// <summary>
    /// 添加系统提醒，由 SystemReminderManager 管理生命周期
    /// </summary>
    public async Task AddSystemReminderAsync(string id, string content, int priority = 0, CancellationToken cancellationToken = default) {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.AddSystemReminder,
            ContextManager = _contextManager,
            ReminderId = id,
            ReminderContent = content,
            ReminderPriority = priority,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 移除指定 ID 的系统提醒
    /// </summary>
    public async Task RemoveSystemReminderAsync(string id, CancellationToken cancellationToken = default) {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var context = new ChatAdminContext
        {
            Operation = ChatAdminOperation.RemoveSystemReminder,
            ContextManager = _contextManager,
            ReminderId = id,
        };
        await _adminPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 释放资源 — ChatInitializer 由 DI 容器自动释放，此处不再手动调用
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// 文件读取追踪器 — 对齐 TS fileReadListeners
/// 追踪最近读取的文件路径，用于压缩后恢复上下文
/// </summary>
internal sealed class FileReadTracker : IFileReadListener
{
    private readonly ToolUseContext _toolUseContext;

    public FileReadTracker(ToolUseContext toolUseContext)
    {
        _toolUseContext = toolUseContext;
    }

    public void OnFileRead(FileReadEventArgs e)
    {
        _toolUseContext.RecordFileRead(e.FilePath);
    }
}
