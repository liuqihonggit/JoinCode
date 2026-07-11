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
        await _taskRegistry.AddDreamTurnAsync(
            ctx.TaskId!,
            new DreamTurn { Text = ctx.ConsolidationResult!, ToolUseCount = 0 },
            Array.Empty<string>(),
            ct).ConfigureAwait(false);
        ctx.TurnRecorded = true;

        await _taskRegistry.CompleteDreamTaskAsync(ctx.TaskId!, ct).ConfigureAwait(false);
        ctx.TaskCompleted = true;

        ctx.Result = DreamResult.Success(ctx.ConsolidationResult!, ctx.TaskId!, ctx.SessionIds.Count, 0);

        await next(ctx, ct).ConfigureAwait(false);
    }
}
