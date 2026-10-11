namespace Core.Bridge;

/// <summary>
/// 运行派生模式中间件 — 决定单会话/多会话派生模式
/// </summary>
[Register(typeof(IBridgeRunMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RunSpawnModeMiddleware : ServiceEntity, IBridgeRunMiddleware {

    /// <summary>
    /// 构造运行派生模式中间件
    /// </summary>
    /// <param name="deps">桥主依赖</param>
    /// <param name="logger">日志记录器</param>
    public RunSpawnModeMiddleware(BridgeMainDeps deps, ILogger<RunSpawnModeMiddleware> logger) {
        _deps = deps;
        _logger = logger;
    }
    private readonly BridgeMainDeps _deps;
    private readonly ILogger<RunSpawnModeMiddleware> _logger;

    /// <summary>
    /// 执行中间件 — 推断有效派生模式并填充到上下文
    /// </summary>
    /// <param name="ctx">桥运行上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(BridgeRunContext ctx, MiddlewareDelegate<BridgeRunContext> next, CancellationToken ct) {
        var multiSessionEnabled = _deps.IsMultiSessionSpawnEnabled?.Invoke() ?? false;
        var effectiveSpawnMode = ctx.Args.SpawnMode;
        var spawnModeSource = BridgeSpawnModeSource.GateDefault;

        if (ctx.ResumeSessionId is not null) {
            effectiveSpawnMode = BridgeSpawnMode.SingleSession;
            spawnModeSource = BridgeSpawnModeSource.Resume;
        } else if (ctx.Args.SpawnMode is not null) {
            spawnModeSource = BridgeSpawnModeSource.Flag;
        } else if (_deps.GetSavedSpawnMode is not null && multiSessionEnabled) {
            var savedMode = _deps.GetSavedSpawnMode();
            if (savedMode is not null) {
                effectiveSpawnMode = savedMode;
                spawnModeSource = BridgeSpawnModeSource.Saved;
            }
        }

        if (multiSessionEnabled &&
            spawnModeSource == BridgeSpawnModeSource.GateDefault &&
            ctx.Args.SpawnMode is null &&
            ctx.ResumeSessionId is null &&
            _deps.SpawnModeDialog is not null &&
            _deps.IsWorktreeAvailable?.Invoke() == true) {
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