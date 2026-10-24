namespace McpToolDispatch;

/// <summary>
/// 工具巡检定时服务 — 后台 PeriodicTimer 循环定时调用 <see cref="ToolPatrolEngine.PatrolAsync"/>，
/// 巡检报告写日志。支持手动触发一次巡检（<see cref="PatrolOnceAsync"/>）供测试 + 即时巡检。
/// <para>对齐 AGENTS.md PeriodicTimer 模式：volatile bool _stopping + _timer.Dispose()，
/// 避免 CTS Dispose 竞态。释放函数禁止超时（StopAsync 直接 await loop，无超时）。</para>
/// </summary>
[Register(typeof(IToolPatrolService), ServiceLifetime.Singleton)]
public sealed class ToolPatrolService : IToolPatrolService, IAsyncDisposable {
    private readonly IToolPatrolEngine _engine;
    private readonly TimeSpan _interval;
    private readonly ILogger<ToolPatrolService>? _logger;
    private PeriodicTimer? _timer;
    private Task? _loop;
    private volatile bool _stopping;
    private int _disposed;

    /// <summary>
    /// 构造工具巡检定时服务。
    /// </summary>
    /// <param name="engine">工具巡检引擎</param>
    /// <param name="interval">巡检间隔（默认 1 小时）</param>
    /// <param name="logger">日志记录器</param>
    public ToolPatrolService(
        IToolPatrolEngine engine,
        TimeSpan? interval = null,
        ILogger<ToolPatrolService>? logger = null) {
        _engine = engine;
        _interval = interval ?? TimeSpan.FromHours(1);
        _logger = logger;
    }

    /// <summary>
    /// 启动后台定时巡检循环 — 按 <see cref="_interval"/> 间隔周期调用巡检。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public Task StartAsync(CancellationToken ct = default) {
        if (_timer is not null) return Task.CompletedTask;
        _timer = new PeriodicTimer(_interval);
        _loop = RunLoopAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 手动触发一次巡检 — 供测试 + 即时巡检（不等定时器）。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>巡检报告</returns>
    public async Task<ToolPatrolReport> PatrolOnceAsync(CancellationToken ct = default) {
        var report = await _engine.PatrolAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("[ToolPatrol] 手动巡检完成:\n{Report}", ToolPatrolEngine.FormatReport(report));
        return report;
    }

    /// <summary>
    /// 停止后台巡检循环 — 设置 _stopping + Dispose 定时器 + await 循环退出（无超时，对齐 AGENTS.md 释放函数禁止超时）。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StopAsync(CancellationToken ct = default) {
        _stopping = true;
        _timer?.Dispose();
        if (_loop is not null) {
            await _loop.ConfigureAwait(false);
        }
        _loop = null;
        _timer = null;
    }

    private async Task RunLoopAsync() {
        while (!_stopping && _timer is { } timer && await timer.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false)) {
            try {
                var report = await _engine.PatrolAsync().ConfigureAwait(false);
                _logger?.LogInformation("[ToolPatrol] 定时巡检完成: {Total} 工具, 健康={Healthy} ([OK]{Ok} [ERR]{Err} [CRASH]{Crash} [LOCK]{Lock} [TMO]{Tmo} [SKIP]{Skip})",
                    report.TotalTools, report.IsHealthy, report.OkCount, report.ErrorCount, report.CrashCount, report.DeadlockCount, report.TimeoutCount, report.SkippedCount);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[ToolPatrol] 定时巡检失败");
            }
        }
    }

    /// <summary>异步释放 — 停止巡检循环（幂等，Interlocked.Exchange 守卫防 double-free）。</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await StopAsync().ConfigureAwait(false);
    }
}
