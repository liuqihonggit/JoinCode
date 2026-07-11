namespace Core.Context;

/// <summary>
/// 聊天初始化器 — 负责会话启动时的初始化
/// 通过中间件管道执行初始化步骤，构造函数参数从 8 个减少到 3 个
/// 提取自 ChatService.InitializeCoreAsync + OnConfigChanged
/// </summary>
[Register]
public sealed partial class ChatInitializer : IChatInitializer
{
    private readonly MiddlewarePipeline<ChatInitContext> _pipeline;
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<ChatInitializer>? _logger;
    private ISessionCostPersistence? _sessionCostPersistence;

    /// <summary>
    /// 初始化聊天初始化器
    /// </summary>
    public ChatInitializer(
        MiddlewarePipeline<ChatInitContext> pipeline,
        IChatContextManager contextManager,
        ILogger<ChatInitializer>? logger = null)
    {
        _pipeline = pipeline;
        _contextManager = contextManager;
        _logger = logger;
    }

    /// <summary>
    /// 执行完整的会话初始化流程 — 通过中间件管道执行
    /// </summary>
    public async Task InitializeAsync(ToolUseContext toolUseContext, CancellationToken cancellationToken = default)
    {
        var context = new ChatInitContext
        {
            ToolUseContext = toolUseContext,
            ContextManager = _contextManager,
        };
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        // 从管道上下文中获取后续操作所需的引用
        _sessionCostPersistence = context.SessionCostPersistence;
    }

    /// <summary>
    /// 保存当前会话成本到持久化存储
    /// </summary>
    public async Task SaveCurrentCostsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionCostPersistence is not null)
        {
            await _sessionCostPersistence.SaveCurrentSessionCostsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取会话 ID
    /// </summary>
    public string GetSessionId() => (_contextManager is ChatContextManager cm) ? cm.SessionId : "default";
}
