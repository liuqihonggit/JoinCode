namespace Infrastructure.Subprocess;

public sealed class ResilientSubprocess : IAsyncDisposable
{
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

    public bool IsHealthy => _healthMonitor?.IsHealthy ?? true;
    public int RestartCount => _restartManager?.RestartCount ?? 0;
    public bool IsCircuitOpen => _circuitBreaker?.IsOpen ?? false;
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

    public event EventHandler<ProcessUnhealthyEventArgs>? Unhealthy;
    public event EventHandler<ProcessRestartedEventArgs>? Restarted;

    public ResilientSubprocess(
        IInteractiveProcess process,
        Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc,
        SubprocessResiliencePolicy policy,
        ILogger? logger = null)
    {
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

    private void InitializeResilience()
    {
        if (_policy.HealthCheck.Interval > TimeSpan.Zero)
        {
            _healthMonitor = new ProcessHealthMonitor(_process, _policy.HealthCheck, _logger);
            _healthMonitor.Unhealthy += OnProcessUnhealthy;
        }

        if (_policy.MaxRestarts > 0)
        {
            _restartManager = new ProcessRestartManager(_policy.MaxRestarts, _logger);
            _restartManager.AfterRestart += OnProcessRestarted;
        }
    }

    public Task WriteStdinAsync(string data, CancellationToken ct = default) =>
        _inputChannel.ExecuteAsync(async token =>
        {
            await _process.StandardInput.WriteAsync(data.AsMemory(), token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }, ct);

    public Task<string?> ReadStdoutLineAsync(CancellationToken ct = default) =>
        _outputChannel.ExecuteAsync(async token =>
            await _process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false), ct);

    public async Task RestartAsync(CancellationToken ct = default)
    {
        if (_restartManager is null)
        {
            throw new InvalidOperationException($"[{_policy.Name}] 未配置重启");
        }

        var oldMonitor = _healthMonitor;
        oldMonitor?.Dispose();

        var newProcess = await _restartManager.RestartAsync(_process, _spawnFunc, ct).ConfigureAwait(false);

        _process = newProcess;

        if (_policy.HealthCheck.Interval > TimeSpan.Zero)
        {
            _healthMonitor = new ProcessHealthMonitor(_process, _policy.HealthCheck, _logger);
            _healthMonitor.Unhealthy += OnProcessUnhealthy;
        }

        _circuitBreaker?.Reset();
    }

    public void Kill()
    {
        try
        {
            _process.Kill();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ResilientSubprocess] 终止进程失败");
        }
    }

    private void OnProcessUnhealthy(object? sender, ProcessUnhealthyEventArgs e)
    {
        Unhealthy?.Invoke(this, e);

        if (e.Action == UnhealthyAction.KillAndRestart && _restartManager is not null && _restartManager.CanRestart)
        {
            _ = RestartAsync(_disposeCts.Token);
        }
        else if (e.Action == UnhealthyAction.Kill)
        {
            Kill();
        }
    }

    private void OnProcessRestarted(object? sender, ProcessRestartedEventArgs e)
    {
        Restarted?.Invoke(this, e);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _disposeCts.Cancel();
        _healthMonitor?.Dispose();
        _inputChannel.Dispose();
        _outputChannel.Dispose();

        try
        {
            await _process.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ResilientSubprocess] 释放进程资源失败");
        }

        GC.SuppressFinalize(this);
    }
}
