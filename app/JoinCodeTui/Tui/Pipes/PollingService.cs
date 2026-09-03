namespace JoinCode.Tui.Pipes;

/// <summary>
/// 轮询服务 — 每 200ms 轮询所有 Agent 管道，检测新消息后触发事件。
/// UI 不监听推送事件，而是通过此服务定期拉取，保证渲染层单线程安全。
/// </summary>
public sealed class PollingService : IAsyncDisposable
{
    private readonly PipeRegistry _registry;
    private readonly int _pollIntervalMs;
    private readonly AsyncLock _semaphore = new();
    private readonly Dictionary<string, AgentState> _lastStates = new();
    private PeriodicTimer? _timer;
    private Task? _pollTask;
    private DateTime _lastPollTime;
    private volatile bool _disposed;

    /// <summary>轮询检测到新消息时触发（AgentId, 新消息列表）。</summary>
    public event Action<string, IReadOnlyList<TuiMessage>>? OnMessagesReceived;

    /// <summary>Agent 状态变化时触发（AgentId, 新状态）。</summary>
    public event Action<string, AgentState>? OnStateChanged;

    /// <summary>创建轮询服务。</summary>
    /// <param name="registry">管道注册表。</param>
    /// <param name="pollIntervalMs">轮询间隔毫秒（默认 200，最小 100）。</param>
    public PollingService(PipeRegistry registry, int pollIntervalMs = 200)
    {
        _registry = registry;
        _pollIntervalMs = Math.Max(100, pollIntervalMs);
        _lastPollTime = DateTime.UtcNow;
    }

    /// <summary>启动轮询。</summary>
    public void Start()
    {
        using var guard = _semaphore.TryLock() ?? throw new System.TimeoutException("锁等待超时");
        if (_timer is not null) return;
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));
        _pollTask = PollLoopAsync();
    }

    /// <summary>停止轮询。</summary>
    public async Task StopAsync()
    {
        using var guard = _semaphore.TryLock() ?? throw new System.TimeoutException("锁等待超时");
        PeriodicTimer? timer;
        Task? pollTask;
        timer = _timer;
        pollTask = _pollTask;
        _timer = null;
        _pollTask = null;
        if (timer is not null)
        {
            timer.Dispose();
        }
        if (pollTask is not null)
        {
            await pollTask.ConfigureAwait(false);
        }
    }

    /// <summary>手动执行一次轮询（用于测试或强制刷新）。</summary>
    public void PollOnce()
    {
        PollAllPipes();
    }

    private async Task PollLoopAsync()
    {
        while (_timer is not null && await _timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                PollAllPipes();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PollingService] 轮询异常: {ex.Message}");
            }
        }
    }

    private void PollAllPipes()
    {
        var now = DateTime.UtcNow;
        foreach (var pipe in _registry.All)
        {
            var newMessages = pipe.GetNewMessages(_lastPollTime);
            if (newMessages.Count > 0)
            {
                OnMessagesReceived?.Invoke(pipe.AgentId, newMessages);
            }

            if (_lastStates.TryGetValue(pipe.AgentId, out var lastState))
            {
                if (pipe.State != lastState)
                {
                    _lastStates[pipe.AgentId] = pipe.State;
                    OnStateChanged?.Invoke(pipe.AgentId, pipe.State);
                }
            }
            else
            {
                _lastStates[pipe.AgentId] = pipe.State;
                OnStateChanged?.Invoke(pipe.AgentId, pipe.State);
            }
        }
        _lastPollTime = now;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _semaphore.Dispose();
    }
}
