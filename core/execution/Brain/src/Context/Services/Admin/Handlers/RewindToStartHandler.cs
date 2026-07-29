namespace Core.Context;

/// <summary>
/// 撤回到初始状态操作处理器
/// </summary>
[Register]
public sealed partial class RewindToStartHandler : IChatAdminOperationHandler
{
    private readonly ISessionStats _sessionStats;

    public RewindToStartHandler(ISessionStats sessionStats)
    {
        _sessionStats = sessionStats;
    }

    public ChatAdminOperation Operation => ChatAdminOperation.RewindToStart;

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
