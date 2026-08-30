namespace IO.Services;

[Register(typeof(IGitHubService), ServiceLifetime.Singleton)]
public sealed partial class GitHubService : ServiceEntity, IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<GitHubService>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, PRSubscription> _subscriptions = new(StringComparer.Ordinal);

    public GitHubService(HttpClient httpClient, IConfigurationService? configService = null, ILogger<GitHubService>? logger = null)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PRSubscription>> ListSubscriptionsAsync(CancellationToken ct = default)
    {
        await LoadSubscriptionsAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _subscriptions.Values.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PRSubscription> SubscribeAsync(string prRef, string events = "all", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND003] PR 引用不能为空", nameof(prRef));

        await LoadSubscriptionsAsync(ct).ConfigureAwait(false);

        var subscription = new PRSubscription
        {
            PrRef = prRef,
            Events = events,
            SubscribedAt = DateTime.UtcNow
        };

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _subscriptions[prRef] = subscription;
        }
        finally
        {
            _lock.Release();
        }

        await SaveSubscriptionsAsync(ct).ConfigureAwait(false);

        _logger?.LogInformation("已订阅 PR: {PrRef}，事件: {Events}", prRef, events);
        return subscription;
    }

    public async Task UnsubscribeAsync(string prRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND004] PR 引用不能为空", nameof(prRef));

        await LoadSubscriptionsAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _subscriptions.Remove(prRef);
        }
        finally
        {
            _lock.Release();
        }

        await SaveSubscriptionsAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("已取消订阅 PR: {PrRef}", prRef);
    }

    private async Task LoadSubscriptionsAsync(CancellationToken ct)
    {
        if (_configService == null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_subscriptions.Count > 0) return;

            try
            {
                var json = await _configService.GetAsync("github.pr_subscriptions", ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json)) return;

                var loaded = JsonSerializer.Deserialize(json, GitHubSubscriptionContext.Default.ListPRSubscription);
                if (loaded != null)
                {
                    _subscriptions.Clear();
                    foreach (var sub in loaded)
                        _subscriptions[sub.PrRef] = sub;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "加载 PR 订阅失败");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveSubscriptionsAsync(CancellationToken ct)
    {
        if (_configService == null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = RelaxedJsonSerializer.Serialize(_subscriptions.Values.ToList(), GitHubSubscriptionContext.Default);
            await _configService.SetAsync("github.pr_subscriptions", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 PR 订阅失败");
        }
        finally
        {
            _lock.Release();
        }
    }

    protected override void OnDispose() => _lock.Dispose();
}

