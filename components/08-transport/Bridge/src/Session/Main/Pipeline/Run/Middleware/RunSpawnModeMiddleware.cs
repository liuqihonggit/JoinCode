namespace Core.Bridge;

[Register(typeof(IBridgeRunMiddleware))]
public sealed partial class RunSpawnModeMiddleware : IBridgeRunMiddleware
{
    [Inject] private readonly BridgeMainDeps _deps;
    [Inject] private readonly ILogger<RunSpawnModeMiddleware> _logger;

    public async Task InvokeAsync(BridgeRunContext ctx, MiddlewareDelegate<BridgeRunContext> next, CancellationToken ct)
    {
        var multiSessionEnabled = _deps.IsMultiSessionSpawnEnabled?.Invoke() ?? false;
        var effectiveSpawnMode = ctx.Args.SpawnMode;
        var spawnModeSource = BridgeSpawnModeSource.GateDefault;

        if (ctx.ResumeSessionId is not null)
        {
            effectiveSpawnMode = BridgeSpawnMode.SingleSession;
            spawnModeSource = BridgeSpawnModeSource.Resume;
        }
        else if (ctx.Args.SpawnMode is not null)
        {
            spawnModeSource = BridgeSpawnModeSource.Flag;
        }
        else if (_deps.GetSavedSpawnMode is not null && multiSessionEnabled)
        {
            var savedMode = _deps.GetSavedSpawnMode();
            if (savedMode is not null)
            {
                effectiveSpawnMode = savedMode;
                spawnModeSource = BridgeSpawnModeSource.Saved;
            }
        }

        if (multiSessionEnabled &&
            spawnModeSource == BridgeSpawnModeSource.GateDefault &&
            ctx.Args.SpawnMode is null &&
            ctx.ResumeSessionId is null &&
            _deps.SpawnModeDialog is not null &&
            _deps.IsWorktreeAvailable?.Invoke() == true)
        {
            var chosenMode = await _deps.SpawnModeDialog(ct).ConfigureAwait(false);
            effectiveSpawnMode = chosenMode;
            _deps.SaveSpawnModePreference?.Invoke(chosenMode);
            _logger.LogInformation("BridgeMain: spawn mode chosen via dialog: {Mode}", chosenMode.ToValue());
        }

        ctx.EffectiveSpawnMode = effectiveSpawnMode;
        ctx.SpawnModeSource = spawnModeSource;
        ctx.IsResuming = ctx.ResumeSessionId is not null;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
