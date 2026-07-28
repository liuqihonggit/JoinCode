
namespace Services.OAuth;

/// <summary>
/// Token 刷新调度器接口
/// 在 Token 过期前自动刷新
/// </summary>
public interface ITokenRefreshScheduler
{
    /// <summary>
    /// 开始监控 Token
    /// </summary>
    /// <param name="provider">OAuth 提供商</param>
    /// <param name="token">当前 Token</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartMonitoringAsync(string provider, OAuthToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止监控 Token
    /// </summary>
    /// <param name="provider">OAuth 提供商</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopMonitoringAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Token 即将过期事件
    /// </summary>
    event EventHandler<TokenRefreshEventArgs>? TokenRefreshRequired;
}

/// <summary>
/// Token 刷新事件参数
/// </summary>
public sealed partial class TokenRefreshEventArgs : EventArgs
{
    /// <summary>
    /// OAuth 提供商
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// 当前 Token
    /// </summary>
    public required OAuthToken CurrentToken { get; init; }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    public string? RefreshToken { get; init; }
}

/// <summary>
/// Token 刷新调度器实现
/// </summary>
[Register(typeof(ITokenRefreshScheduler))]
public sealed partial class TokenRefreshScheduler : ITokenRefreshScheduler, IDisposable
{
    [Inject] private readonly ILogger<TokenRefreshScheduler>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, TokenMonitor> _monitors = new();
    private readonly TimeSpan _refreshBuffer;

    public event EventHandler<TokenRefreshEventArgs>? TokenRefreshRequired;

    public TokenRefreshScheduler(ILogger<TokenRefreshScheduler>? logger = null, TimeSpan? refreshBuffer = null, IClockService? clock = null)
    {
        _logger = logger;
        _refreshBuffer = refreshBuffer ?? TimeSpan.FromMinutes(5);
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public Task StartMonitoringAsync(string provider, OAuthToken token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentNullException.ThrowIfNull(token);

        // 停止现有的监控
        StopMonitoringAsync(provider);

        // 计算刷新时间（过期前 buffer 时间）
        var refreshTime = token.ExpiresAt - _refreshBuffer;
        var delay = refreshTime - _clock.GetUtcNowOffset();

        if (delay <= TimeSpan.Zero)
        {
            // Token 即将过期或已过期，立即触发刷新
            _logger?.LogWarning("Token for {Provider} is about to expire or already expired, triggering immediate refresh", provider);
            TriggerRefresh(provider, token);
            return Task.CompletedTask;
        }

        // 创建定时器
        var timer = new System.Timers.Timer(delay.TotalMilliseconds);
        timer.AutoReset = false;
        timer.Elapsed += (sender, e) =>
        {
            TriggerRefresh(provider, token);
        };

        var monitor = new TokenMonitor
        {
            Provider = provider,
            Timer = timer,
            Token = token
        };

        _monitors[provider] = monitor;
        timer.Start();

        _logger?.LogInformation(
            "Started monitoring token for {Provider}, will refresh in {Delay} (at {RefreshTime})",
            provider, delay, refreshTime);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopMonitoringAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (_monitors.TryRemove(provider, out var monitor))
        {
            monitor.Timer.Stop();
            monitor.Timer.Dispose();
            _logger?.LogInformation("Stopped monitoring token for {Provider}", provider);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 触发刷新
    /// </summary>
    private void TriggerRefresh(string provider, OAuthToken token)
    {
        _logger?.LogInformation("Token refresh triggered for {Provider}", provider);

        TokenRefreshRequired?.Invoke(this, new TokenRefreshEventArgs
        {
            Provider = provider,
            CurrentToken = token,
            RefreshToken = token.RefreshToken
        });

        // 移除监控
        _monitors.TryRemove(provider, out _);
    }

    public void Dispose()
    {
        foreach (var monitor in _monitors.Values)
        {
            monitor.Timer.Stop();
            monitor.Timer.Dispose();
        }

        _monitors.Clear();
    }
}

/// <summary>
/// Token 监控信息
/// </summary>
internal sealed class TokenMonitor
{
    public required string Provider { get; init; }
    public required System.Timers.Timer Timer { get; init; }
    public required OAuthToken Token { get; init; }
}
