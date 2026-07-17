
namespace JoinCode.Abstractions.Services;

public abstract class RemoteCacheRefreshServiceBase<TItem> : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ITelemetryService? _telemetryService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;
    private readonly ConcurrentDictionary<string, TItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly IClockService _clock;
    private DateTime _lastFetchTime = DateTime.MinValue;
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
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        RefreshOptions = options;
        Logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(options.ApiEndpoint))
        {
            _refreshTimer = new Timer(
                _ => { if (_disposed == 0) _ = RefreshAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false); },
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

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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

                _lastFetchTime = _clock.GetUtcNow();
                Logger?.LogInformation("已刷新 {Count} 条{Label}", result.Items.Count, RefreshLogLabel);
                RecordMetrics("refresh", true);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "刷新{Label}失败", RefreshLogLabel);
            RecordMetrics("refresh", false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    protected async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache.IsEmpty || (RefreshOptions.EnableCache && _clock.GetUtcNow() - _lastFetchTime > RefreshOptions.CacheExpiration))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected void RecordMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount($"{MetricsPrefix}.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: $"{MetricsPrefix} operation count");

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
        _disposeCts.Dispose();
    }
}

public sealed class RemoteRefreshResult<TItem>
{
    public Dictionary<string, TItem>? Items { get; init; }
}
