
namespace JoinCode.Abstractions.Services;

/// <summary>远程缓存刷新命令标记接口</summary>
public interface IRemoteCacheRefreshCommand;

/// <summary>刷新缓存命令 — 携带幂等键与回复通道，由 Consumer 串行处理</summary>
public sealed record RefreshCacheCmd(IdempotencyKey IdempotencyKey) : IRemoteCacheRefreshCommand, IRequestCommand {
    /// <summary>回复通道 — Consumer 处理完成后写入结果，调用方通过 Reader.ReadAsync 拉取</summary>
    public Channel<Unit> ReplyChannel { get; } = Channel.CreateUnbounded<Unit>();

    /// <summary>从幂等缓存恢复结果 — 命中缓存时写入 ReplyChannel 并返回 true</summary>
    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<Unit>(IdempotencyKey, out var cached)) {
            ReplyChannel.Writer.TryWrite(cached);
            return true;
        }
        return false;
    }
}

public abstract class RemoteCacheRefreshServiceBase<TItem> : ActorBase<IRemoteCacheRefreshCommand, Unit> {
    private readonly HttpClient _httpClient;
    private readonly ITelemetryService? _telemetryService;
    private readonly Timer _refreshTimer;
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
        : base() {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        RefreshOptions = options;
        Logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        IdempotencyStore = new IdempotencyStore();

        if (!string.IsNullOrEmpty(options.ApiEndpoint)) {
            _refreshTimer = new Timer(
                _ => { if (Volatile.Read(ref _disposed) == 0) TrySend(new RefreshCacheCmd(new IdempotencyKey("refresh-cache-timer", Guid.NewGuid().ToString()))); },
                null,
                options.RefreshInterval,
                options.RefreshInterval);
        } else {
            _refreshTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
        var cmd = new RefreshCacheCmd(key);
        await SendAsync(cmd, ct).ConfigureAwait(false);
        await cmd.ReplyChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
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

    protected override async ValueTask HandleAsync(IRemoteCacheRefreshCommand command, CancellationToken ct) {
        switch (command) {
            case RefreshCacheCmd refresh:
            await DoRefreshAsync(ct).ConfigureAwait(false);
            IdempotencyStore?.TryRegister(refresh.IdempotencyKey, Unit.Value);
            refresh.ReplyChannel.Writer.TryWrite(Unit.Value);
            break;
        }
    }

    protected override void OnConsumerError(Exception ex) {
    }

    public override async ValueTask DisposeAsync() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        _disposeCts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class RemoteRefreshResult<TItem> {
    /// <summary>获取刷新得到的条目字典。</summary>
    public Dictionary<string, TItem> Items { get; init; } = [];
}