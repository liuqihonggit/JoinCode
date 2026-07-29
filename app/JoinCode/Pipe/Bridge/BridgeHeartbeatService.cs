namespace JoinCode.Pipe;

using JoinCode.Abstractions.Attributes;

[Register]
public sealed partial class BridgeHeartbeatService
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _timeout;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private volatile int _isRunning;
    private DateTime? _lastPongReceived;
    private bool _timeoutFired;
    private readonly IClockService _clock;

    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) != 0;
    public DateTime? LastPingAt { get; private set; }

    public event EventHandler? TimeoutDetected;
    public event EventHandler? Recovered;

    public BridgeHeartbeatService(TimeSpan interval, TimeSpan timeout, IClockService? clock = null)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        _interval = interval;
        _timeout = timeout;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// DI 构造函数 — 使用默认心跳间隔 30s 和超时 90s
    /// </summary>
    public BridgeHeartbeatService()
        : this(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), null)
    {
    }

    public void Start()
    {
        if (IsRunning) return;

        Interlocked.Exchange(ref _isRunning, 1);
        _timeoutFired = false;
        _lastPongReceived = _clock.GetUtcNow();
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        Interlocked.Exchange(ref _isRunning, 0);
        _cts?.Cancel();
    }

    public void ReceivePong()
    {
        _lastPongReceived = _clock.GetUtcNow();

        if (_timeoutFired)
        {
            _timeoutFired = false;
            Recovered?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);

                LastPingAt = _clock.GetUtcNow();

                if (_lastPongReceived.HasValue &&
                    _clock.GetUtcNow() - _lastPongReceived.Value > _timeout)
                {
                    if (!_timeoutFired)
                    {
                        _timeoutFired = true;
                        TimeoutDetected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
