namespace Core.Context;

/// <summary>
/// 撤回到初始状态操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class RewindToStartHandler : ServiceEntity, IChatAdminOperationHandler
{
    private readonly ISessionStats _sessionStats;

    /// <summary>
    /// 初始化 <see cref="RewindToStartHandler"/> 实例
    /// </summary>
    /// <param name="sessionStats">会话统计服务，用于在撤回后重置统计</param>
    public RewindToStartHandler(ISessionStats sessionStats)
    {
        _sessionStats = sessionStats;
    }

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.RewindToStart;

    /// <summary>
    /// 执行撤回到初始状态操作，重置会话统计并记录撤回结果
    /// </summary>
    /// <param name="context">管理操作上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        try
        {
            var result = await context.ContextManager.RewindToStartAsync(ct).ConfigureAwait(false);
            _sessionStats.Reset();
            context.RewindResult = result;
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }
    }
}
