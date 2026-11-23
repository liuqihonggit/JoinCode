namespace Core.Context;

/// <summary>
/// 设置系统提示词操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class SetSystemPromptHandler : ServiceEntity, IChatAdminOperationHandler {

    /// <summary>
    /// 初始化 <see cref="SetSystemPromptHandler"/> 实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public SetSystemPromptHandler(ILogger<SetSystemPromptHandler>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<SetSystemPromptHandler>? _logger;

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.SetSystemPrompt;

    /// <summary>
    /// 执行设置系统提示词操作，将上下文中的系统提示词更新到上下文管理器
    /// </summary>
    /// <param name="context">管理操作上下文，需提供 SystemPrompt</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct) {
        await context.ContextManager.UpdateSystemPromptAsync(context.SystemPrompt ?? throw new InvalidOperationException("SystemPrompt is required."), ct).ConfigureAwait(false);
    }
}