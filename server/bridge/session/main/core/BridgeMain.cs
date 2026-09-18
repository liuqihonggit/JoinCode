
namespace Core.Bridge;

/// <summary>
/// Bridge 独立进程编排器 — 对齐 TS 端 bridgeMain.ts
/// 核心职责: 参数解析 → OAuth认证 → 环境注册 → 工作轮询 → 子进程管理 → 优雅关闭
/// </summary>
public sealed partial class BridgeMain : ServiceEntity
{
    internal static readonly FrozenSet<string> ValidPermissionModes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept", "bubble");

    internal readonly BridgeMainDeps _deps;
    internal readonly ILogger? _logger;
    internal readonly IFileSystem _fs;
    internal readonly IClockService _clock;

    // 活跃会话跟踪 — 对齐 TS 端 runBridgeLoop 的 7 个 Map + 3 个 Set
    internal readonly BridgeSessionTracker _tracker = new();
    internal readonly List<Task> _pendingCleanups = new(); // 待清理任务 — 对齐 TS 端 pendingCleanups
    internal readonly AsyncLock _cleanupLock = new(); // 替代 lock — JCC4001 分析器要求

    // 退避状态 — 对齐 TS 端 BackoffConfig + 双轨退避
    internal readonly BridgeBackoffStrategy _backoff;

    // 崩溃恢复指针管理
    internal readonly BridgePointerManager _pointerManager;

    // 工作 API 封装
    internal readonly BridgeWorkApiClient _workApi;

    // 生命周期
    internal CancellationTokenSource? _loopCts;
    internal Task? _loopTask;
    internal int _asyncDisposed;
    internal int _isShuttingDown;
    internal bool _isResuming; // 对齐 TS 端: resume 模式标记 — 可恢复关闭时跳过 archive+deregister
    internal bool _fatalExit; // 对齐 TS 端: fatalExit — 致命错误后跳过 resume 提示

    // Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler + v1/v2 分支
    internal BridgeTokenRefreshScheduler? _tokenRefresh;

    // 遥测 — 对齐 TS 端 logEvent/logEventAsync (tengu_bridge_*)
    internal readonly ITelemetryService? _telemetry;
    internal DateTime _loopStartTime; // 主循环启动时间 — 用于计算 loop_duration_ms
    internal readonly MiddlewarePipeline<HandleWorkContext>? _handleWorkPipeline;
    internal readonly MiddlewarePipeline<ShutdownContext>? _shutdownPipeline;
    internal readonly MiddlewarePipeline<BridgeRunContext>? _runPipeline;

    /// <summary>当前活跃会话数</summary>
    public int ActiveSessionCount => _tracker.Sessions.Count;

    /// <summary>是否正在运行</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; internal set; }

    /// <summary>获取环境 ID，未注册时抛出异常</summary>
    internal string GetEnvironmentId() =>
        EnvironmentId ?? throw new InvalidOperationException("Environment not registered yet. Call RegisterEnvironmentAsync first.");

    /// <summary>环境密钥</summary>
    public string? EnvironmentSecret { get; internal set; }

    internal readonly INetworkConnectivityService? _networkService;

    // 职责类
    private readonly BridgeShutdownHandler _shutdownHandler;
    private readonly BridgeEnvironmentRegistrar _environmentRegistrar;
    private readonly BridgeLoopRunner _loopRunner;
    private readonly BridgeWorkHandler _workHandler;
    private readonly BridgeSessionMonitor _sessionMonitor;
    private readonly BridgeRunOrchestrator _runOrchestrator;

    /// <summary>
    /// 构造 Bridge 主编排器
    /// </summary>
    /// <param name="deps">BridgeMain 依赖包</param>
    /// <param name="handleWorkPipeline">工作处理中间件管道（可选）</param>
    /// <param name="shutdownPipeline">关闭中间件管道（可选）</param>
    /// <param name="runPipeline">运行中间件管道（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="clock">时钟服务（可选，默认系统时钟）</param>
    /// <param name="networkService">网络连通性服务（可选）</param>
    /// <param name="giveUpThreshold">退避放弃阈值（可选）</param>
    public BridgeMain(
        BridgeMainDeps deps,
        MiddlewarePipeline<HandleWorkContext>? handleWorkPipeline = null,
        MiddlewarePipeline<ShutdownContext>? shutdownPipeline = null,
        MiddlewarePipeline<BridgeRunContext>? runPipeline = null,
        ILogger? logger = null,
        IClockService? clock = null,
        INetworkConnectivityService? networkService = null,
        TimeSpan? giveUpThreshold = null)
        : base(nameof(BridgeMain))
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _logger = logger;
        _fs = deps.FileSystem;
        _telemetry = deps.TelemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _backoff = new BridgeBackoffStrategy(_clock, _logger, giveUpThreshold);
        _pointerManager = new BridgePointerManager(deps.PointerService, _logger);
        _workApi = new BridgeWorkApiClient(deps.ApiClient, _logger);
        _handleWorkPipeline = handleWorkPipeline;
        _shutdownPipeline = shutdownPipeline;
        _runPipeline = runPipeline;
        _networkService = networkService;
        _shutdownHandler = new BridgeShutdownHandler(this);
        _environmentRegistrar = new BridgeEnvironmentRegistrar(this);
        _loopRunner = new BridgeLoopRunner(this);
        _workHandler = new BridgeWorkHandler(this);
        _sessionMonitor = new BridgeSessionMonitor(this);
        _runOrchestrator = new BridgeRunOrchestrator(this);
    }

    /// <summary>
    /// 验证 HTTPS URL — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    /// <returns>null 表示通过，否则返回错误消息</returns>
    internal static string? ValidateHttpsUrl(string baseUrl)
    {
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "Bridge requires HTTPS (or localhost).";
        }
        return null;
    }

    /// <summary>
    /// 验证访问令牌 — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    /// <returns>访问令牌；null 表示无可用令牌</returns>
    internal string? GetValidAccessToken()
    {
        return _deps.GetAccessToken();
    }

    /// <summary>
    /// 注册 Bridge 环境 — 委托给 BridgeEnvironmentRegistrar
    /// </summary>
    internal async Task<BridgeEnvironmentRegistrationResponse> RegisterEnvironmentAsync(
        BridgeConfig config, CancellationToken ct)
        => await _environmentRegistrar.RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);

    /// <summary>
    /// 尝试创建初始会话 — 委托给 BridgeEnvironmentRegistrar
    /// </summary>
    internal async Task<string?> TryCreateInitialSessionAsync(
        string? name, string? permissionMode, BridgeConfig config, CancellationToken ct)
        => await _environmentRegistrar.TryCreateInitialSessionAsync(name, permissionMode, config, ct).ConfigureAwait(false);

    /// <summary>
    /// 启动 Bridge 主循环 — 对齐 TS 端 bridgeMain()
    /// 流程: 参数验证 → OAuth → 环境注册 → 进入 runBridgeLoop
    /// </summary>
    public async Task<BridgeMainResult> RunAsync(BridgeMainArgs args, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (_runPipeline is not null)
        {
            var ctx = new BridgeRunContext
            {
                Args = args,
                CancellationToken = ct,
            };
            await _runPipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);

            if (ctx.EarlyResult is not null)
            {
                return ctx.EarlyResult;
            }

            return await RunBridgeFromContextAsync(ctx, ct).ConfigureAwait(false);
        }

        return await RunDirectAsync(args, ct).ConfigureAwait(false);
    }

    private async Task<BridgeMainResult> RunBridgeFromContextAsync(BridgeRunContext ctx, CancellationToken ct)
        => await _runOrchestrator.RunBridgeFromContextAsync(ctx, ct).ConfigureAwait(false);

    private async Task<BridgeMainResult> RunDirectAsync(BridgeMainArgs args, CancellationToken ct)
        => await _runOrchestrator.RunDirectAsync(args, ct).ConfigureAwait(false);

    /// <summary>
    /// Headless 模式启动 — 对齐 TS 端 runBridgeHeadless()
    /// 守护进程入口：无 TUI、无交互、无 readline
    /// 配置性错误抛出 BridgeHeadlessPermanentError（supervisor 停放 worker）
    /// 瞬态错误抛出普通 Exception（supervisor 重试）
    /// </summary>
    public async Task RunHeadlessAsync(BridgeHeadlessOpts opts, CancellationToken ct = default)
        => await _runOrchestrator.RunHeadlessAsync(opts, ct).ConfigureAwait(false);

    /// <summary>
    /// 请求优雅关闭 — 对齐 TS 端 SIGINT/SIGTERM 处理
    /// </summary>
    public async Task ShutdownAsync()
        => await _shutdownHandler.ShutdownAsync().ConfigureAwait(false);

    /// <summary>
    /// 核心轮询循环 — 委托给 BridgeLoopRunner
    /// </summary>
    internal async Task RunBridgeLoopAsync(
        BridgeConfig config, string? initialSessionId, CancellationToken ct)
        => await _loopRunner.RunBridgeLoopAsync(config, initialSessionId, ct).ConfigureAwait(false);

    /// <summary>
    /// 处理工作项 — 对齐 TS 端 bridgeMain.ts 的工作处理流程
    /// 流程: 解码 WorkSecret → healthcheck 处理 → ACK(sessionToken) → CCR v2 判断 → 生成子进程
    /// </summary>
    internal async Task HandleWorkAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct)
        => await _workHandler.HandleWorkAsync(config, work, ct).ConfigureAwait(false);

    /// <summary>
    /// 监控会话完成 — 对齐 TS 端: onSessionDone 回调
    /// </summary>
    internal async Task MonitorSessionCompletionAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle, CancellationToken ct)
        => await _sessionMonitor.MonitorSessionCompletionAsync(config, work, handle, ct).ConfigureAwait(false);

    /// <summary>
    /// 监控会话超时 — 对齐 TS 端: onSessionTimeout
    /// </summary>
    internal async Task MonitorSessionTimeoutAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle,
        int timeoutMs, CancellationToken ct)
        => await _sessionMonitor.MonitorSessionTimeoutAsync(config, work, handle, timeoutMs, ct).ConfigureAwait(false);

    /// <summary>
    /// 清理所有会话 — 对齐 TS 端: 优雅关闭流程
    /// </summary>
    internal async Task CleanupAllSessionsAsync(BridgeConfig config, CancellationToken ct)
        => await _sessionMonitor.CleanupAllSessionsAsync(config, ct).ConfigureAwait(false);

    internal BridgeMainResult HandleRegistrationError(Exception ex)
        => _environmentRegistrar.HandleRegistrationError(ex);
}
