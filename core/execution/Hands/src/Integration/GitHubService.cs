namespace IO.Services;

[Register(typeof(IGitHubService), ServiceLifetime.Singleton)]
public sealed partial class GitHubService : ServiceEntity, IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<GitHubService>? _logger;
    private readonly AsyncLock _lock = new();
    private readonly Dictionary<string, PRSubscription> _subscriptions = new(StringComparer.Ordinal);

    public GitHubService(HttpClient httpClient, IConfigurationService? configService = null, ILogger<GitHubService>? logger = null)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PRSubscription>> ListSubscriptionsAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.LockAsync(ct).ConfigureAwait(false);
        await LoadSubscriptionsCoreAsync(ct).ConfigureAwait(false);
        return _subscriptions.Values.ToList();
    }

    public async Task<PRSubscription> SubscribeAsync(string prRef, string events = "all", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND003] PR 引用不能为空", nameof(prRef));

        using var guard = await _lock.LockAsync(ct).ConfigureAwait(false);

        await LoadSubscriptionsCoreAsync(ct).ConfigureAwait(false);

        var subscription = new PRSubscription
        {
            PrRef = prRef,
            Events = events,
            SubscribedAt = DateTime.UtcNow
        };

        _subscriptions[prRef] = subscription;

        await SaveSubscriptionsCoreAsync(ct).ConfigureAwait(false);

        _logger?.LogInformation("已订阅 PR: {PrRef}，事件: {Events}", prRef, events);
        return subscription;
    }

    public async Task UnsubscribeAsync(string prRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND004] PR 引用不能为空", nameof(prRef));

        using var guard = await _lock.LockAsync(ct).ConfigureAwait(false);

        await LoadSubscriptionsCoreAsync(ct).ConfigureAwait(false);

        _subscriptions.Remove(prRef);

        await SaveSubscriptionsCoreAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("已取消订阅 PR: {PrRef}", prRef);
    }

    private async Task LoadSubscriptionsCoreAsync(CancellationToken ct)
    {
        if (_configService == null) return;
        if (_subscriptions.Count > 0) return;

        try
        {
            var json = await _configService.GetAsync("github.pr_subscriptions", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return;

            var loaded = RelaxedJsonSerializer.Deserialize(json, GitHubSubscriptionContext.Default.ListPRSubscription);
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

    private async Task SaveSubscriptionsCoreAsync(CancellationToken ct)
    {
        if (_configService == null) return;

        try
        {
            var json = RelaxedJsonSerializer.Serialize(_subscriptions.Values.ToList(), GitHubSubscriptionContext.Default);
            await _configService.SetAsync("github.pr_subscriptions", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 PR 订阅失败");
        }
    }

    protected override void OnDispose() => _lock.Dispose();
}

