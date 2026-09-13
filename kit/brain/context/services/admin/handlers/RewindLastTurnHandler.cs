namespace Core.Context;

/// <summary>
/// 撤回最后一轮对话操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class RewindLastTurnHandler : ServiceEntity, IChatAdminOperationHandler
{
    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.RewindLastTurn;

    /// <summary>
    /// 执行撤回最后一轮对话操作，将撤回结果写入上下文
    /// </summary>
    /// <param name="context">管理操作上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        context.RewindResult = await context.ContextManager.RewindLastTurnAsync(ct).ConfigureAwait(false);
    }
}
