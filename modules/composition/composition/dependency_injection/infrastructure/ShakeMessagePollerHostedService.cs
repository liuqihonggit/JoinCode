namespace Core.DependencyInjection;

/// <summary>
/// 震动消息轮询托管服务 — 作为 <see cref="IHostedService"/> 在应用启动时开始轮询文件邮箱，
/// 读取跨进程 shake 消息并执行本地震动 — ADR 0109。
/// <para>轮询间隔 2 秒，读取 <c>shake-broadcast/all-agents</c> 邮箱的未读消息。</para>
/// <para>收到 <c>MessageType="shake"</c> 消息后，经 <see cref="IWindowShakeCoordinator"/> 去抖，</para>
/// <para>调用 <see cref="IWindowShakeService.ShakeWindowAsync"/> 震动窗口，然后标记已读。</para>
/// <para>后台循环用 <c>volatile bool _stopping</c> + <see cref="PeriodicTimer"/> 模式，避免 CTS 竞态。</para>
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed partial class ShakeMessagePollerHostedService : ServiceEntity, IHostedService {
    private const string MailboxAgentId = "all-agents";
    private const string MailboxSessionId = "shake-broadcast";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ITeammateMailboxService? _mailboxService;
    private readonly IWindowShakeService? _shakeService;
    private readonly IWindowShakeCoordinator _coordinator;
    private readonly ILogger<ShakeMessagePollerHostedService>? _logger;
    private volatile bool _stopping;
    private Task? _pollTask;

    /// <summary>
    /// 初始化 <see cref="ShakeMessagePollerHostedService"/> 实例。
    /// </summary>
    /// <param name="coordinator">震动去抖协调器。</param>
    /// <param name="mailboxService">队友邮箱服务（可选，未注册时不轮询）。</param>
    /// <param name="shakeService">窗口震动服务（可选，未注册时仅标记已读不震动）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public ShakeMessagePollerHostedService(
        IWindowShakeCoordinator coordinator,
        ITeammateMailboxService? mailboxService = null,
        IWindowShakeService? shakeService = null,
        ILogger<ShakeMessagePollerHostedService>? logger = null) {
        _coordinator = coordinator;
        _mailboxService = mailboxService;
        _shakeService = shakeService;
        _logger = logger;
    }

    /// <summary>
    /// 启动轮询 — 邮箱服务未注册时直接返回。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task StartAsync(CancellationToken cancellationToken) {
        if (_mailboxService is null) {
            _logger?.LogDebug("ShakeMessagePoller: mailbox service not registered, skipping");
            return Task.CompletedTask;
        }

        _pollTask = Task.Run(() => PollLoopAsync(CancellationToken.None), CancellationToken.None);
        _logger?.LogInformation("ShakeMessagePoller started");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止轮询 — 设置 _stopping 标志，等待轮询任务退出。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StopAsync(CancellationToken cancellationToken) {
        _stopping = true;
        if (_pollTask is not null) {
            try { await _pollTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _logger?.LogInformation("ShakeMessagePoller stopped");
    }

    /// <summary>
    /// 轮询循环 — PeriodicTimer + volatile bool 模式，避免 CTS Dispose 竞态。
    /// </summary>
    private async Task PollLoopAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(PollInterval);
        while (!_stopping && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) {
            try {
                await PollOnceAsync(ct).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogWarning(ex, "ShakeMessagePoller: poll failed");
            }
        }
    }

    /// <summary>
    /// 单次轮询 — 读取未读消息，过滤 shake 类型，去抖震动，标记已读。
    /// </summary>
    internal async Task PollOnceAsync(CancellationToken ct) {
        if (_mailboxService is null) return;

        var messages = await _mailboxService.ReadUnreadAsync(MailboxAgentId, MailboxSessionId, ct).ConfigureAwait(false);
        if (messages.Count == 0) return;

        var shakeMessages = messages.Where(m => m.MessageType == "shake").ToList();
        if (shakeMessages.Count == 0) return;

        if (_coordinator.IsShakeEnabled && _coordinator.TryAcquireShakeSlot() && _shakeService is not null) {
            var result = await _shakeService.ShakeWindowAsync(ct).ConfigureAwait(false);
            _logger?.LogDebug("ShakeMessagePoller: received {Count} shake messages, shook window: {Result}",
                shakeMessages.Count, result);
        }

        var messageIds = shakeMessages.Select(m => m.MessageId).ToList();
        await _mailboxService.MarkAsReadAsync(MailboxAgentId, MailboxSessionId, messageIds, ct).ConfigureAwait(false);
    }
}