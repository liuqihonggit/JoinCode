
namespace JoinCode.Abstractions.Services;

/// <summary>远程缓存刷新命令标记接口</summary>
public interface IRemoteCacheRefreshCommand;

/// <summary>定时刷新命令 — ActorTimers 定时触发，Handle 内生成动态 IdempotencyKey。</summary>
public sealed record TimerRefreshCmd : IRemoteCacheRefreshCommand;

/// <summary>刷新缓存命令 — 携带幂等键与 OnSuccess/OnFailure 回调，由 Consumer 串行处理</summary>
public sealed record RefreshCacheCmd(
    IdempotencyKey IdempotencyKey,
    Action<Unit> OnSuccess,
    Action<Exception> OnFailure,
    Action<BackpressureSignal> OnBackpressure
) : IRemoteCacheRefreshCommand, IRequestCommand<Unit> {
    /// <summary>从幂等缓存恢复结果 — 命中缓存时调用 OnSuccess 回调</summary>
    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<Unit>(IdempotencyKey, out var cached)) {
            OnSuccess(cached);
            return true;
        }
        return false;
    }
}

public abstract class RemoteCacheRefreshServiceBase<TItem> : ActorBase<IRemoteCacheRefreshCommand, Unit> {
    private readonly HttpClient _httpClient;
    private readonly ITelemetryService? _telemetryService;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly IClockService _clock;
    private ImmutableHamT<string, TItem> _cache = ImmutableHamT<string, TItem>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private long _lastFetchTicks;
    private int _disposed;

    protected HttpClient Http => _httpClient;
    protected IClockService Clock => _clock;
    protected ILogger? Logger { get; }
    protected IRemoteRefreshOptions RefreshOptions { get; }
    protected ImmutableHamT<string, TItem> Cache => Volatile.Read(ref _cache);

    protected abstract string MetricsPrefix { get; }
    protected abstract string RefreshLogLabel { get; }
    protected abstract Task<RemoteRefreshResult<TItem>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken);

    protected RemoteCacheRefreshServiceBase(
        HttpClient httpClient,
        IRemoteRefreshOptions options,
        ILogger? logger,
        ITelemetryService? telemetryService,
        IClockService? clock)
        : base(idempotencyStore: new IdempotencyStore()) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        RefreshOptions = options;
        Logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(options.ApiEndpoint)) {
            Timers.StartPeriodicTimer("refresh", new TimerRefreshCmd(), options.RefreshInterval, options.RefreshInterval);
        }
    }

    /// <summary>异步刷新缓存。</summary>
    /// <param name="ct">取消令牌。</param>
    public virtual async Task RefreshAsync(CancellationToken ct = default) {
        if (string.IsNullOrEmpty(RefreshOptions.ApiEndpoint)) {
            Logger?.LogDebug("未配置{Label} API 端点，跳过刷新", RefreshLogLabel);
            return;
        }

        var key = new IdempotencyKey("refresh-cache", Guid.NewGuid().ToString());
        var tcs = new TaskCompletionSource<Unit>();
        RefreshCacheCmd? cmd = null;
        cmd = new RefreshCacheCmd(key, tcs.SetResult, tcs.SetException,
            CreateBackpressureHandler(() => { if (cmd is not null) TrySend(cmd); }));
        Tell(cmd);
        await tcs.Task.ConfigureAwait(false);
    }

    protected async Task EnsureCacheAsync(CancellationToken cancellationToken) {
        var lastTicks = Volatile.Read(ref _lastFetchTicks);
        var lastFetch = lastTicks == 0 ? DateTime.MinValue : new DateTime(lastTicks, DateTimeKind.Utc);
        if (Volatile.Read(ref _cache).IsEmpty || (RefreshOptions.EnableCache && _clock.GetUtcNow() - lastFetch > RefreshOptions.CacheExpiration)) {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected void RecordMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, $"{MetricsPrefix}.count", operation, isSuccess);

    private async Task DoRefreshAsync(CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(RefreshOptions.ApiEndpoint)) {
            Logger?.LogDebug("未配置{Label} API 端点，跳过刷新", RefreshLogLabel);
            return;
        }

        try {
            Logger?.LogDebug("正在刷新{Label}配置", RefreshLogLabel);

            var requestUrl = RefreshOptions.ApiEndpoint!;
            if (!string.IsNullOrEmpty(RefreshOptions.ClientKey)) {
                var separator = requestUrl.Contains('?') ? "&" : "?";
                requestUrl = $"{requestUrl}{separator}clientKey={Uri.EscapeDataString(RefreshOptions.ClientKey)}";
            }

            var result = await FetchAndDeserializeAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (result.Items != null) {
                var next = ImmutableHamT<string, TItem>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in result.Items) {
                    next = next.Add(kvp.Key, kvp.Value);
                }
                Interlocked.Exchange(ref _cache, next);

                Volatile.Write(ref _lastFetchTicks, _clock.GetUtcNow().Ticks);
                Logger?.LogInformation("已刷新 {Count} 条{Label}", result.Items.Count, RefreshLogLabel);
                RecordMetrics("refresh", true);
            }
        } catch (Exception ex) {
            Logger?.LogError(ex, "刷新{Label}失败", RefreshLogLabel);
            RecordMetrics("refresh", false);
        }
    }

    protected override void Handle(IRemoteCacheRefreshCommand command, CancellationToken ct) {
        RegisterInFlight(HandleAsyncImpl(command, ct).AsTask());
    }

    private async ValueTask HandleAsyncImpl(IRemoteCacheRefreshCommand command, CancellationToken ct) {
        switch (command) {
            case TimerRefreshCmd:
            await DoRefreshAsync(ct).ConfigureAwait(false);
            break;

            case RefreshCacheCmd refresh:
            await DoRefreshAsync(ct).ConfigureAwait(false);
            IdempotencyStore?.TryRegister(refresh.IdempotencyKey, Unit.Value);
            refresh.OnSuccess(Unit.Value);
            break;
        }
    }

    protected override void OnConsumerError(Exception ex) {
    }

    public override async ValueTask DisposeAsync() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class RemoteRefreshResult<TItem> {
    /// <summary>获取刷新得到的条目字典。</summary>
    public Dictionary<string, TItem> Items { get; init; } = [];
}