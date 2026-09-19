namespace IO.Services;

/// <summary>
/// GitHub 服务 — 管理 PR 订阅列表，支持加载、订阅、取消订阅并持久化到配置
/// </summary>
[Register(typeof(IGitHubService), ServiceLifetime.Singleton)]
public sealed partial class GitHubService : ServiceEntity, IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<GitHubService>? _logger;
    private readonly AsyncLock _lock = new();
    private readonly Dictionary<string, PRSubscription> _subscriptions = new(StringComparer.Ordinal);

    /// <summary>
    /// 构造 GitHub 服务实例
    /// </summary>
    /// <param name="httpClient">用于访问 GitHub API 的 HTTP 客户端</param>
    /// <param name="configService">配置服务，用于持久化 PR 订阅</param>
    /// <param name="logger">日志记录器</param>
    public GitHubService(HttpClient httpClient, IConfigurationService? configService = null, ILogger<GitHubService>? logger = null)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// 异步列出所有 PR 订阅 — 首次调用时从配置加载
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前所有 PR 订阅列表</returns>
    public async Task<IReadOnlyList<PRSubscription>> ListSubscriptionsAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");
        await LoadSubscriptionsCoreAsync(ct).ConfigureAwait(false);
        return _subscriptions.Values.ToList();
    }

    /// <summary>
    /// 异步订阅指定 PR — PR 引用为空时抛出 ArgumentException[HND003]
    /// </summary>
    /// <param name="prRef">PR 引用，格式 owner/repo#number</param>
    /// <param name="events">订阅的事件类型，缺省 all</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新建的 PR 订阅</returns>
    public async Task<PRSubscription> SubscribeAsync(string prRef, string events = "all", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND003] PR 引用不能为空", nameof(prRef));

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

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

    /// <summary>
    /// 异步取消订阅指定 PR — PR 引用为空时抛出 ArgumentException[HND004]
    /// </summary>
    /// <param name="prRef">PR 引用，格式 owner/repo#number</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task UnsubscribeAsync(string prRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prRef))
            throw new ArgumentException("[HND004] PR 引用不能为空", nameof(prRef));

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

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

    /// <summary>
    /// 释放资源 — 释放异步锁
    /// </summary>
    public override void Dispose()
    {
        _lock.Dispose();
        base.Dispose();
    }
}

