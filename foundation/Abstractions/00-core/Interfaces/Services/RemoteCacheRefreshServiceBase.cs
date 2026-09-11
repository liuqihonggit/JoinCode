
namespace JoinCode.Abstractions.Services;

public interface IRemoteCacheRefreshCommand;

public sealed record RefreshCacheCmd(TaskCompletionSource? Tcs) : IRemoteCacheRefreshCommand;

public abstract class RemoteCacheRefreshServiceBase<TItem> : ActorBase<IRemoteCacheRefreshCommand, Unit>, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ITelemetryService? _telemetryService;
    private readonly Timer _refreshTimer;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, TItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _lastFetchTicks;
    private int _disposed;

    protected HttpClient Http => _httpClient;
    protected IClockService Clock => _clock;
    protected ILogger? Logger { get; }
    protected IRemoteRefreshOptions RefreshOptions { get; }
    protected ConcurrentDictionary<string, TItem> Cache => _cache;

    protected abstract string MetricsPrefix { get; }
    protected abstract string RefreshLogLabel { get; }
    protected abstract Task<RemoteRefreshResult<TItem>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken);

    protected RemoteCacheRefreshServiceBase(
        HttpClient httpClient,
        IRemoteRefreshOptions options,
        ILogger? logger,
        ITelemetryService? telemetryService,
        IClockService? clock)
        : base()
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        RefreshOptions = options;
        Logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(options.ApiEndpoint))
        {
            _refreshTimer = new Timer(
                _ => { if (Volatile.Read(ref _disposed) == 0) TrySend(new RefreshCacheCmd(null)); },
                null,
                options.RefreshInterval,
                options.RefreshInterval);
        }
        else
        {
            _refreshTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public virtual async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(RefreshOptions.ApiEndpoint))
        {
            Logger?.LogDebug("未配置{Label} API 端点，跳过刷新", RefreshLogLabel);
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new RefreshCacheCmd(tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    protected async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        var lastTicks = Volatile.Read(ref _lastFetchTicks);
        var lastFetch = lastTicks == 0 ? DateTime.MinValue : new DateTime(lastTicks, DateTimeKind.Utc);
        if (_cache.IsEmpty || (RefreshOptions.EnableCache && _clock.GetUtcNow() - lastFetch > RefreshOptions.CacheExpiration))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected void RecordMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, $"{MetricsPrefix}.count", operation, isSuccess);

    private async Task DoRefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(RefreshOptions.ApiEndpoint))
        {
            Logger?.LogDebug("未配置{Label} API 端点，跳过刷新", RefreshLogLabel);
            return;
        }

        try
        {
            Logger?.LogDebug("正在刷新{Label}配置", RefreshLogLabel);

            var requestUrl = RefreshOptions.ApiEndpoint!;
            if (!string.IsNullOrEmpty(RefreshOptions.ClientKey))
            {
                var separator = requestUrl.Contains('?') ? "&" : "?";
                requestUrl = $"{requestUrl}{separator}clientKey={Uri.EscapeDataString(RefreshOptions.ClientKey)}";
            }

            var result = await FetchAndDeserializeAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (result.Items != null)
            {
                _cache.Clear();
                foreach (var kvp in result.Items)
                {
                    _cache[kvp.Key] = kvp.Value;
                }

                Volatile.Write(ref _lastFetchTicks, _clock.GetUtcNow().Ticks);
                Logger?.LogInformation("已刷新 {Count} 条{Label}", result.Items.Count, RefreshLogLabel);
                RecordMetrics("refresh", true);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "刷新{Label}失败", RefreshLogLabel);
            RecordMetrics("refresh", false);
        }
    }

    protected override async ValueTask HandleAsync(IRemoteCacheRefreshCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case RefreshCacheCmd refresh:
                await DoRefreshAsync(ct).ConfigureAwait(false);
                refresh.Tcs?.TrySetResult();
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _disposeCts.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
    }
}

public sealed class RemoteRefreshResult<TItem>
{
    public Dictionary<string, TItem> Items { get; init; } = [];
}
