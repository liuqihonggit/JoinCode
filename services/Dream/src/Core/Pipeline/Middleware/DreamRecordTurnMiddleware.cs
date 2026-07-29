namespace JoinCode.Dream.Pipeline;

using JoinCode.Dream.Persistence;

[Register]
public sealed partial class DreamRecordTurnMiddleware : IDreamMiddleware
{
    private readonly IDreamTaskRegistry _taskRegistry;

    public DreamRecordTurnMiddleware(IDreamTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }

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

        ctx.Result = DreamResult.Success(consolidationResult, taskId, ctx.SessionIds.Count, 0);

        await next(ctx, ct).ConfigureAwait(false);
    }
}
