namespace JoinCode.Dream.Pipeline;

using JoinCode.Dream.Persistence;

[Register]
public sealed partial class DreamTaskRegisterMiddleware : IDreamMiddleware
{
    private readonly IDreamTaskRegistry _taskRegistry;
    private readonly AutoDreamConfig _config;

    public DreamTaskRegisterMiddleware(IDreamTaskRegistry taskRegistry, AutoDreamConfig config)
    {
        _taskRegistry = taskRegistry;
        _config = config;
    }

    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(
            new DreamTaskRegistrationRequest(
                ctx.SessionIds.Count,
                DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond,
                new CancellationTokenSource()),
            ct).ConfigureAwait(false);

        ctx.TaskId = taskId;
        ctx.TaskRegistered = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
