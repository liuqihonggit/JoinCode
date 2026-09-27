namespace Core.Configuration;

/// <summary>
/// 设置变更应用器 Actor — 订阅 ConfigChanged 事件,通过 Actor 邮箱串行化 ApplySettingsChangeAsync 调用。
/// <para>对齐 ADR 0101: 消除并发应用设置变更的竞态,MiddlewarePipeline 由 Consumer 线程独占执行。</para>
/// <para>死循环防护: ConfigChangeNotifier.MarkInternalWrite 抑制自身写入的文件变更事件,不触发 OnConfigChanged。</para>
/// </summary>
[Register(typeof(ISettingsChangeApplier), ServiceLifetime.Singleton)]
public sealed partial class SettingsChangeApplier : ActorBase<SettingsChangeApplier.SettingsChangeCommand, Unit>, ISettingsChangeApplier {
    private readonly IConfigChangeNotifier _configChangeNotifier;
    private readonly MiddlewarePipeline<SettingsContext> _pipeline;
    private readonly IFileSystem _fs;
    private readonly ILogger<SettingsChangeApplier>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入配置变更通知器、设置管道、文件系统和可选的日志/遥测服务
    /// </summary>
    public SettingsChangeApplier(
        IConfigChangeNotifier configChangeNotifier,
        MiddlewarePipeline<SettingsContext> pipeline,
        IFileSystem fs,
        ILogger<SettingsChangeApplier>? logger = null,
        ITelemetryService? telemetryService = null)
        : base(new ActorBackpressure(100, BoundedChannelFullMode.DropOldest), null) {
        _configChangeNotifier = configChangeNotifier;
        _pipeline = pipeline;
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
        IdempotencyStore = new IdempotencyStore();
        _configChangeNotifier.ConfigChanged += OnConfigChanged;
    }

    /// <summary>命令基类</summary>
    public abstract record SettingsChangeCommand;

    /// <summary>应用设置变更命令 — 携带幂等键与 OnSuccess/OnFailure 回调，由 Consumer 串行处理</summary>
    private sealed record ApplySettingsCmd(
        IdempotencyKey IdempotencyKey,
        Action<bool> OnSuccess,
        Action<Exception> OnFailure,
        Action<BackpressureSignal> OnBackpressure
    ) : SettingsChangeCommand, IRequestCommand<bool> {
        /// <summary>从幂等缓存恢复结果 — 命中缓存时调用 OnSuccess 回调</summary>
        public bool TryRestoreFromCache(IIdempotencyStore store) {
            if (store.TryGetResult<bool>(IdempotencyKey, out var cached)) {
                OnSuccess(cached);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 手动触发设置重新加载 — 投递命令到 Actor 邮箱,等待 Consumer 处理完成。
    /// </summary>
    public async Task ApplySettingsChangeAsync(CancellationToken cancellationToken = default) {
        var key = new IdempotencyKey("apply-settings", Guid.NewGuid().ToString());
        var tcs = new TaskCompletionSource<bool>();
        ApplySettingsCmd? cmd = null;
        cmd = new ApplySettingsCmd(key, tcs.SetResult, tcs.SetException,
            CreateBackpressureHandler(() => { if (cmd is not null) TrySend(cmd); }));
        if (!TrySend(cmd)) {
            _logger?.LogWarning("SettingsChangeApplier 邮箱已满或已释放,跳过设置变更应用");
            return;
        }
        await tcs.Task.ConfigureAwait(false);
    }

    private void OnConfigChanged(object? sender, ConfigChangeEventArgs e) {
        var fileName = Path.GetFileName(e.FilePath);
        if (!string.Equals(fileName, AppDataConstants.SettingsFileName, StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        _logger?.LogInformation("检测到配置文件变更: {Path} ({ChangeType})", e.FilePath, e.ChangeType);
        TrySend(new ApplySettingsCmd(new IdempotencyKey("apply-settings", Guid.NewGuid().ToString()), _ => { }, _ => { }, CreateBackpressureHandler(() => { })));
    }

    /// <inheritdoc />
    protected override async ValueTask HandleAsync(SettingsChangeCommand cmd, CancellationToken ct) {
        if (cmd is not ApplySettingsCmd apply) return;

        _logger?.LogInformation("正在应用设置变更...");
        try {
            var context = new SettingsContext {
                FileSystem = _fs,
                Logger = _logger,
                TelemetryService = _telemetryService,
            };
            await _pipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
            _telemetryService?.RecordCount("guard.settings.apply.count", [], "count", "Settings apply count");
            _logger?.LogInformation("设置变更已应用");
            IdempotencyStore?.TryRegister(apply.IdempotencyKey, true);
            apply.OnSuccess(true);
        } catch (Exception ex) {
            _logger?.LogError(ex, "应用设置变更失败");
            _telemetryService?.RecordCount("guard.settings.apply.error.count", [], "count", "Settings apply error count");
            apply.OnSuccess(false);
        }
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync() {
        _configChangeNotifier.ConfigChanged -= OnConfigChanged;
        return base.DisposeAsync();
    }
}