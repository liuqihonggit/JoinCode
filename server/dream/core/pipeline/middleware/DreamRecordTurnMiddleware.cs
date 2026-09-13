namespace JoinCode.Dream.Pipeline;


/// <summary>
/// 做梦回合记录中间件 — 将合并结果记录为回合、完成任务并设置成功结果
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamRecordTurnMiddleware : ServiceEntity, IDreamMiddleware
{
    private readonly IDreamTaskRegistry _taskRegistry;

    /// <summary>
    /// 构造做梦回合记录中间件
    /// </summary>
    /// <param name="taskRegistry">做梦任务注册表</param>
    public DreamRecordTurnMiddleware(IDreamTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }

    /// <summary>
    /// 处理做梦上下文 — 记录回合、完成任务并设置成功结果后传递给下游
    /// </summary>
    /// <param name="ctx">做梦上下文</param>
    /// <param name="next">下游中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        var taskId = ctx.TaskId ?? throw new InvalidOperationException("TaskId is not set. Ensure DreamTaskRegisterMiddleware runs first.");
        var consolidationResult = ctx.ConsolidationResult ?? throw new InvalidOperationException("ConsolidationResult is not set. Ensure DreamLlmConsolidateMiddleware runs first.");

        await _taskRegistry.AddDreamTurnAsync(
            taskId,
            new DreamTurn { Text = consolidationResult, ToolUseCount = 0 },
            Array.Empty<string>(),
            ct).ConfigureAwait(false);
        ctx.TurnRecorded = true;

        await _taskRegistry.CompleteDreamTaskAsync(taskId, ct).ConfigureAwait(false);
        ctx.TaskCompleted = true;

        ctx.Result = DreamResult.Success(consolidationResult, taskId, ctx.SessionIds.Count(), 0);

        await next(ctx, ct).ConfigureAwait(false);
    }
}
