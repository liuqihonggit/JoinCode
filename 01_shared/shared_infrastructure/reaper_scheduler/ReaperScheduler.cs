namespace Infrastructure.ReaperScheduler;

/// <summary>
/// 单一后台回收调度器 — 全局唯一后台线程, 空闲事件驱动, 按会话隔离轮流扫描
/// 替代 BackgroundEntityReaperService, 整合 EntityReaper + ShellProcessWatchdog 为扫描策略
/// 空闲时才扫描, Agent 活跃时不扫描, 避免竞争
/// </summary>
[Register(typeof(ReaperScheduler), ServiceLifetime.Singleton)]
public sealed partial class ReaperScheduler : IDisposable
{
    private readonly BlockingCollection<bool> _signals = new(1);
    private readonly List<IScanStrategy> _strategies = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _scanThread;
    private readonly ILogger<ReaperScheduler>? _logger;
    private int _totalScans;

    public int TotalScans => _totalScans;
    public int StrategyCount => _strategies.Count;

    public ReaperScheduler(ILogger<ReaperScheduler>? logger = null)
    {
        _logger = logger;
        _scanThread = new Thread(ScanLoop) { IsBackground = true, Name = "ReaperScheduler" };
    }

    /// <summary>
    /// 注册扫描策略 — EntityReaper/ShellProcessWatchdog 等
    /// </summary>
    public void AddStrategy(IScanStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategies.Add(strategy);
        _logger?.LogDebug("注册扫描策略: {Name}", strategy.Name);
    }

    /// <summary>
    /// 空闲事件触发 — Agent 循环空闲时调用, 触发一次按会话隔离扫描
    /// 非阻塞, 信号队列满则丢弃(已有扫描在进行)
    /// </summary>
    public void OnIdle()
    {
        _signals.TryAdd(true);
    }

    /// <summary>
    /// 启动后台扫描线程
    /// </summary>
    public void Start()
    {
        if (_scanThread.ThreadState == System.Threading.ThreadState.Unstarted)
            _scanThread.Start();
    }

    /// <summary>
    /// 手动触发一次全量扫描 — 遍历所有会话, 所有策略
    /// </summary>
    public int ScanOnce()
    {
        var count = 0;
        foreach (var scope in SessionRouter.GetAllScopes())
        {
            foreach (var strategy in _strategies)
            {
                try { strategy.Scan(scope); count++; }
                catch (Exception ex) { _logger?.LogWarning(ex, "策略 {Name} 扫描会话 {SessionId} 失败", strategy.Name, scope.SessionId); }
            }
        }
        Interlocked.Increment(ref _totalScans);
        return count;
    }

    private void ScanLoop()
    {
        _logger?.LogDebug("ReaperScheduler 后台线程启动");
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _signals.Take(_cts.Token);
                ScanOnce();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger?.LogWarning(ex, "ReaperScheduler 扫描循环异常"); }
        }
        _logger?.LogDebug("ReaperScheduler 后台线程退出");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signals.CompleteAdding();
        if (_scanThread.IsAlive)
            _scanThread.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
        _signals.Dispose();
    }
}
