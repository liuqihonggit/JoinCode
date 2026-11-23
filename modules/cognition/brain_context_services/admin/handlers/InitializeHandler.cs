namespace Core.Context;

/// <summary>
/// 初始化操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class InitializeHandler : ServiceEntity, IChatAdminOperationHandler {

    /// <summary>
    /// 初始化 <see cref="InitializeHandler"/> 实例
    /// </summary>
    /// <param name="initializer">聊天初始化器，用于执行初始化逻辑</param>
    public InitializeHandler(IChatInitializer initializer) {
        _initializer = initializer;
    }
    private readonly IChatInitializer _initializer;

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.Initialize;

    /// <summary>
    /// 执行初始化操作，调用聊天初始化器完成上下文初始化
    /// </summary>
    /// <param name="context">管理操作上下文，需提供 ToolUseContext</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct) {
        await _initializer.InitializeAsync(context.ToolUseContext ?? throw new InvalidOperationException("ToolUseContext is required.")).ConfigureAwait(false);
    }
}