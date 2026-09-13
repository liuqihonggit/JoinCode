namespace Infrastructure.HotSpot;

/// <summary>
/// 热点 spawn 集成服务实现 — Worker spawn 时注册文件写入监听器 + 获取契约变更通知队列
/// </summary>
[Register(typeof(IHotSpotSpawnIntegration), ServiceLifetime.Singleton)]
public sealed partial class HotSpotSpawnIntegration : IHotSpotSpawnIntegration
{
    private readonly IFileWriteListenerRegistry _registry;
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IContractChangeBroadcaster _broadcaster;
    private readonly IHotSpotTracker _hotSpotTracker;
    private readonly IContractChangeNotificationRouter _router;
    private readonly ILogger<HotSpotSpawnIntegration>? _logger;

    private volatile bool _listenersRegistered;
    private string? _registeredCaptainId;
    private readonly AsyncLock _registerLock = new("HotSpotSpawnIntegration");

    /// <summary>
    /// 构造热点 spawn 集成服务
    /// </summary>
    /// <param name="registry">文件写入监听器注册表</param>
    /// <param name="intentCollector">意图收集器</param>
    /// <param name="hotFileDetector">热文件检测器</param>
    /// <param name="broadcaster">契约变更广播器</param>
    /// <param name="hotSpotTracker">热点追踪器</param>
    /// <param name="router">契约变更通知路由器</param>
    /// <param name="logger">日志记录器</param>
    public HotSpotSpawnIntegration(
        IFileWriteListenerRegistry registry,
        IIntentCollector intentCollector,
        IHotFileDetector hotFileDetector,
        IContractChangeBroadcaster broadcaster,
        IHotSpotTracker hotSpotTracker,
        IContractChangeNotificationRouter router,
        ILogger<HotSpotSpawnIntegration>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _hotSpotTracker = hotSpotTracker ?? throw new ArgumentNullException(nameof(hotSpotTracker));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger;
    }

    /// <summary>
    /// 确保 listener 已注册（幂等）— 同一 captainId 只注册一次，captainId 变化时重新注册
    /// </summary>
    public void EnsureListenersRegistered(string captainId)
    {
        if (_listenersRegistered && _registeredCaptainId == captainId) return;

        using (_registerLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_registerLock.Name}' 等待超时"))
        {
            if (_listenersRegistered && _registeredCaptainId == captainId) return;

            var intentListener = new IntentReportFileWriteListener(_intentCollector, _hotFileDetector, captainId);
            var broadcastListener = new ContractChangeBroadcastListener(_broadcaster, _hotFileDetector, _hotSpotTracker, _router, captainId);
            _registry.Register(intentListener);
            _registry.Register(broadcastListener);

            _listenersRegistered = true;
            _registeredCaptainId = captainId;
            _logger?.LogInformation("[HotSpotIntegration] Listener 已注册, captainId={CaptainId}", captainId);
        }
    }

    /// <summary>
    /// 获取或创建 Worker 的契约变更通知队列
    /// </summary>
    public ConcurrentQueue<string> GetOrCreateNotificationQueue(string agentId)
    {
        return _router.GetOrCreateQueue(agentId);
    }
}
