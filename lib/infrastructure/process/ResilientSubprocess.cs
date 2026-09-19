namespace Infrastructure.Subprocess;

/// <summary>
/// 弹性子进程 — 包装交互式进程，提供健康监控、自动重启、断路器与弹性通道读写能力
/// </summary>
public sealed class ResilientSubprocess : IAsyncDisposable {
    private readonly SubprocessResiliencePolicy _policy;
    private readonly Func<CancellationToken, Task<IInteractiveProcess>> _spawnFunc;
    private readonly ILogger? _logger;

    private IInteractiveProcess _process;
    private readonly ResilientChannel _inputChannel;
    private readonly ResilientChannel _outputChannel;
    private ProcessHealthMonitor? _healthMonitor;
    private ProcessRestartManager? _restartManager;
    private UnifiedCircuitBreaker? _circuitBreaker;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>获取进程是否健康（未配置健康监控时视为健康）</summary>
    public bool IsHealthy => _healthMonitor?.IsHealthy ?? true;
    /// <summary>获取累计重启次数（未配置重启时为 0）</summary>
    public int RestartCount => _restartManager?.RestartCount ?? 0;
    /// <summary>获取断路器是否处于开启状态（未配置断路器时为 false）</summary>
    public bool IsCircuitOpen => _circuitBreaker?.IsOpen ?? false;
    /// <summary>获取底层进程的系统标识符</summary>
    public int ProcessId => _process.Id;
    /// <summary>获取底层进程是否已退出</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>进程被判定为不健康时触发，参数携带不健康事件详情</summary>
    public event EventHandler<ProcessUnhealthyEventArgs>? Unhealthy;
    /// <summary>进程完成重启时触发，参数携带重启事件详情</summary>
    public event EventHandler<ProcessRestartedEventArgs>? Restarted;

    /// <summary>
    /// 构造弹性子进程
    /// </summary>
    /// <param name="process">已启动的交互式进程实例</param>
    /// <param name="spawnFunc">用于重启时再次拉起进程的工厂委托</param>
    /// <param name="policy">弹性策略配置</param>
    /// <param name="logger">可选日志记录器</param>
    public ResilientSubprocess(
        IInteractiveProcess process,
        Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc,
        SubprocessResiliencePolicy policy,
        ILogger? logger = null) {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _spawnFunc = spawnFunc ?? throw new ArgumentNullException(nameof(spawnFunc));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;

        _circuitBreaker = new UnifiedCircuitBreaker(_policy.Name, _policy.CircuitBreaker);

        _inputChannel = new ResilientChannel(
            $"{_policy.Name}/stdin", _circuitBreaker, _policy.WriteTimeout, _logger);

        _outputChannel = new ResilientChannel(
            $"{_policy.Name}/stdout", _circuitBreaker, _policy.ReadTimeout, _logger);

        InitializeResilience();
    }

    private void InitializeResilience() {
        if (_policy.HealthCheck.Interval > TimeSpan.Zero) {
            _healthMonitor = new ProcessHealthMonitor(_process, _policy.HealthCheck, _logger);
            _healthMonitor.Unhealthy += OnProcessUnhealthy;
        }

        if (_policy.MaxRestarts > 0) {
            _restartManager = new ProcessRestartManager(_policy.MaxRestarts, _logger);
            _restartManager.AfterRestart += OnProcessRestarted;
        }
    }

    /// <summary>
    /// 向子进程标准输入写入数据，受弹性通道与断路器保护
    /// </summary>
    /// <param name="data">待写入的文本数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步写入操作的任务</returns>
    public Task WriteStdinAsync(string data, CancellationToken ct = default) =>
        _inputChannel.ExecuteAsync(async token => {
            await _process.StandardInput.WriteAsync(data.AsMemory(), token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }, ct);

    /// <summary>
    /// 从子进程标准输出按行读取，受弹性通道与断路器保护
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>读取到的一行文本，到达末尾时为 null</returns>
    public Task<string?> ReadStdoutLineAsync(CancellationToken ct = default) =>
        _outputChannel.ExecuteAsync(async token =>
            await _process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false), ct);

    /// <summary>
    /// 重启子进程 — 销毁旧进程并通过工厂委托拉起新进程，重置断路器与健康监控
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">未配置重启策略时抛出</exception>
    public async Task RestartAsync(CancellationToken ct = default) {
        if (_restartManager is null) {
            throw new InvalidOperationException($"[INF041] [{_policy.Name}] 未配置重启");
        }

        var oldMonitor = _healthMonitor;
        if (oldMonitor is not null) await oldMonitor.DisposeAsync().ConfigureAwait(false);

        var newProcess = await _restartManager.RestartAsync(_process, _spawnFunc, ct).ConfigureAwait(false);

        _process = newProcess;

        if (_policy.HealthCheck.Interval > TimeSpan.Zero) {
            _healthMonitor = new ProcessHealthMonitor(_process, _policy.HealthCheck, _logger);
            _healthMonitor.Unhealthy += OnProcessUnhealthy;
        }

        _circuitBreaker?.Reset();
    }

    /// <summary>
    /// 终止底层进程，异常被吞并并记录到日志
    /// </summary>
    public void Kill() {
        try {
            _process.Kill();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ResilientSubprocess] 终止进程失败");
        }
    }

    private void OnProcessUnhealthy(object? sender, ProcessUnhealthyEventArgs e) {
        Unhealthy?.Invoke(this, e);

        if (e.Action == UnhealthyAction.KillAndRestart && _restartManager is not null && _restartManager.CanRestart) {
            _ = RestartAsync(_disposeCts.Token);
        } else if (e.Action == UnhealthyAction.Kill) {
            Kill();
        }
    }

    private void OnProcessRestarted(object? sender, ProcessRestartedEventArgs e) {
        Restarted?.Invoke(this, e);
    }

    /// <summary>
    /// 异步释放资源 — 取消内部令牌、销毁健康监控、释放通道与底层进程
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _disposeCts.Cancel();
        if (_healthMonitor is not null) await _healthMonitor.DisposeAsync().ConfigureAwait(false);
        _inputChannel.Dispose();
        _outputChannel.Dispose();

        await _process.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}