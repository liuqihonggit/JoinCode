namespace JoinCode.Pipe;


/// <summary>
/// 桥接心跳服务 — 定期发送 ping，监控 pong 响应，超时触发事件并支持恢复通知
/// </summary>
[Register(typeof(BridgeHeartbeatService), ServiceLifetime.Singleton)]
public sealed partial class BridgeHeartbeatService : ServiceEntity
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _timeout;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private volatile int _isRunning;
    private DateTime? _lastPongReceived;
    private bool _timeoutFired;
    private readonly IClockService _clock;

    /// <summary>心跳循环是否正在运行</summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) != 0;
    /// <summary>最后一次发送 ping 的时间</summary>
    public DateTime? LastPingAt { get; private set; }

    /// <summary>超时检测事件 — pong 响应超过阈值时触发</summary>
    public event EventHandler? TimeoutDetected;
    /// <summary>恢复事件 — 超时后再次收到 pong 时触发</summary>
    public event EventHandler? Recovered;

    /// <summary>
    /// 构造函数 — 指定心跳间隔与超时阈值
    /// </summary>
    /// <param name="interval">心跳发送间隔</param>
    /// <param name="timeout">pong 响应超时阈值</param>
    /// <param name="clock">时钟服务，可选，默认使用系统时钟</param>
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

    /// <summary>启动心跳循环</summary>
    public void Start()
    {
        if (IsRunning) return;

        Interlocked.Exchange(ref _isRunning, 1);
        _timeoutFired = false;
        _lastPongReceived = _clock.GetUtcNow();
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    /// <summary>停止心跳循环</summary>
    public void Stop()
    {
        if (!IsRunning) return;

        Interlocked.Exchange(ref _isRunning, 0);
        _cts?.Cancel();
    }

    /// <summary>接收 pong 响应 — 更新最后接收时间，若此前处于超时状态则触发恢复事件</summary>
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
