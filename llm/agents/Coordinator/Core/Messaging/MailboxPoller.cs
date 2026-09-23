namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱轮询器 — 周期性从文件邮箱拉取未读消息并投递到进程内邮箱或消息接收器
/// </summary>
[Register(typeof(IMailboxPoller), ServiceLifetime.Singleton)]
public sealed partial class MailboxPoller : IMailboxPoller, IAsyncDisposable {
    private readonly ITeammateMailboxService _mailboxService;
    private readonly IMailbox _messageBroker;
    private readonly IMailboxMessageSink? _messageSink;
    private readonly ILogger<MailboxPoller>? _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pollingAgents;
    private Task _pendingPolling = Task.CompletedTask;
    private readonly TimeSpan _pollInterval;
    private int _isDisposed;

    /// <summary>
    /// 构造邮箱轮询器实例
    /// </summary>
    /// <param name="mailboxService">队友邮箱服务，提供未读消息读取与标记已读能力</param>
    /// <param name="messageBroker">进程内消息邮箱，用于将拉取到的消息投递给 Agent</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="pollInterval">可选轮询间隔，缺省 500 毫秒</param>
    /// <param name="messageSink">可选消息接收器，优先于 messageBroker 投递</param>
    public MailboxPoller(
        ITeammateMailboxService mailboxService,
        IMailbox messageBroker,
        ILogger<MailboxPoller>? logger = null,
        TimeSpan? pollInterval = null,
        IMailboxMessageSink? messageSink = null) {
        _mailboxService = mailboxService ?? throw new ArgumentNullException(nameof(mailboxService));
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _logger = logger;
        _pollingAgents = new ConcurrentDictionary<string, CancellationTokenSource>();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _messageSink = messageSink;
    }

    /// <summary>
    /// 启动指定 Agent 在指定会话下的邮箱轮询；同一键重复调用将被忽略
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">会话标识</param>
    public void StartPolling(string agentId, string sessionId) {
        var key = GetPollingKey(agentId, sessionId);
        if (_pollingAgents.ContainsKey(key)) {
            _logger?.LogDebug("Polling already active for {AgentId} in session {SessionId}", agentId, sessionId);
            return;
        }

        var cts = new CancellationTokenSource();
        if (!_pollingAgents.TryAdd(key, cts)) {
            cts.Dispose();
            return;
        }

        _pendingPolling = Task.WhenAll(_pendingPolling, PollLoopAsync(agentId, sessionId, cts.Token));

        _logger?.LogInformation("Mailbox polling started for {AgentId} in session {SessionId}", agentId, sessionId);
    }

    /// <summary>
    /// 等待所有 pending 轮询任务完成 — 调用方可选 await 以确保轮询落定
    /// </summary>
    public Task WaitForPendingPollingAsync() => _pendingPolling;

    /// <summary>
    /// 停止指定 Agent 在指定会话下的邮箱轮询
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">会话标识</param>
    public void StopPolling(string agentId, string sessionId) {
        var key = GetPollingKey(agentId, sessionId);
        if (_pollingAgents.TryRemove(key, out var cts)) {
            cts.Cancel();
            cts.Dispose();
            _logger?.LogInformation("Mailbox polling stopped for {AgentId} in session {SessionId}", agentId, sessionId);
        }
    }

    private async Task PollLoopAsync(string agentId, string sessionId, CancellationToken cancellationToken) {
        try {
            var cursor = await _mailboxService.GetOrCreateCursorAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);

                    var unreadMessages = await _mailboxService.ReadUnreadAsync(
                        agentId, sessionId, cancellationToken).ConfigureAwait(false);

                    if (unreadMessages.Count == 0) continue;

                    var messageIds = new List<string>(unreadMessages.Count);

                    for (var i = 0; i < unreadMessages.Count; i++) {
                        var mailboxMsg = unreadMessages[i];
                        messageIds.Add(mailboxMsg.MessageId);

                        if (_messageSink is not null) {
                            await _messageSink.DeliverAsync(agentId, mailboxMsg, cancellationToken).ConfigureAwait(false);
                        } else {
                            await _messageBroker.SendAsync(agentId, mailboxMsg, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await _mailboxService.MarkAsReadAsync(agentId, sessionId, messageIds, cancellationToken).ConfigureAwait(false);

                    _logger?.LogDebug("Polled {Count} new messages for {AgentId}", unreadMessages.Count, agentId);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "Error during mailbox polling for {AgentId}", agentId);
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, "Mailbox polling loop terminated unexpectedly for {AgentId}", agentId);
        }
    }

    private static string GetPollingKey(string agentId, string sessionId) {
        return $"{sessionId}:{agentId}";
    }

    /// <summary>
    /// 异步释放轮询器，取消所有活跃轮询任务并清理资源
    /// </summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return ValueTask.CompletedTask;

        var tasks = new List<Task>();
        foreach (var kvp in _pollingAgents) {
            var cts = kvp.Value;
            tasks.Add(cts.CancelAsync().ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                cts,
                TaskContinuationOptions.ExecuteSynchronously));
        }

        _pollingAgents.Clear();
        return tasks.Count == 0 ? ValueTask.CompletedTask : new ValueTask(Task.WhenAll(tasks));
    }
}