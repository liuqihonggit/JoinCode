namespace Core.Bridge;

/// <summary>
/// Bridge 指针管理器 — 管理崩溃恢复指针的写入/刷新/清除
/// 从 BridgeMain 提取,降低大类字段数和指针管理逻辑复杂度
/// </summary>
internal sealed class BridgePointerManager : IDisposable {
    private readonly BridgePointerService _pointerService;
    private readonly ILogger? _logger;
    private Timer? _refreshTimer;
    private int _disposed;

    /// <summary>指针来源目录 — 恢复失败时清除正确的指针</summary>
    public string? ResumePointerDir { get; set; }

    /// <summary>当前刷新定时器 — 供管道上下文传递</summary>
    public Timer? RefreshTimer => _refreshTimer;

    public BridgePointerManager(BridgePointerService pointerService, ILogger? logger) {
        _pointerService = pointerService;
        _logger = logger;
    }

    /// <summary>写入崩溃恢复指针</summary>
    public async Task WritePointerAsync(BridgeConfig config, string sessionId, string? environmentId) {
        try {
            var pointer = new BridgePointer {
                SessionId = sessionId,
                EnvironmentId = environmentId ?? "",
                Source = BridgePointerSource.Standalone.ToValue(),
            };
            await _pointerService.WriteAsync(config.Dir, pointer).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "BridgeMain: pointer write failed (non-fatal)");
        }
    }

    /// <summary>启动崩溃恢复指针刷新定时器 — 每小时刷新 mtime</summary>
    public void StartPointerRefreshTimer(BridgeConfig config, string sessionId, string? environmentId) {
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(async _ => {
            await WritePointerAsync(config, sessionId, environmentId).ConfigureAwait(false);
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    /// <summary>清除崩溃恢复指针 — resume 模式下保留指针文件供下次 --continue</summary>
    public async Task ClearPointerAsync(bool isResuming, BridgeSpawnMode spawnMode, string workingDirectory) {
        if (!isResuming && spawnMode == BridgeSpawnMode.SingleSession) {
            try {
                var pointerDir = ResumePointerDir ?? workingDirectory;
                await _pointerService.ClearAsync(pointerDir).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "BridgeMain: pointer clear failed (non-fatal)");
            }
        }
    }

    /// <summary>停止指针刷新定时器</summary>
    public void StopRefreshTimer() {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}