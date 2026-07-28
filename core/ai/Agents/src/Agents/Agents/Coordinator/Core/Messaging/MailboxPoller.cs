namespace Core.Agents.Coordinator;

[Register]
public sealed partial class MailboxPoller : IMailboxPoller, IAsyncDisposable
{
    private readonly ITeammateMailboxService _mailboxService;
    private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ILogger<MailboxPoller>? _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pollingAgents;
    private readonly TimeSpan _pollInterval;
    private int _isDisposed;

    public MailboxPoller(
        ITeammateMailboxService mailboxService,
        IAgentMessageBroker messageBroker,
        ILogger<MailboxPoller>? logger = null,
        TimeSpan? pollInterval = null)
    {
        _mailboxService = mailboxService ?? throw new ArgumentNullException(nameof(mailboxService));
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _logger = logger;
        _pollingAgents = new ConcurrentDictionary<string, CancellationTokenSource>();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public void StartPolling(string agentId, string sessionId)
    {
        var key = GetPollingKey(agentId, sessionId);
        if (_pollingAgents.ContainsKey(key))
        {
            _logger?.LogDebug("Polling already active for {AgentId} in session {SessionId}", agentId, sessionId);
            return;
        }

        var cts = new CancellationTokenSource();
        if (!_pollingAgents.TryAdd(key, cts))
        {
            cts.Dispose();
            return;
        }

        _ = PollLoopAsync(agentId, sessionId, cts.Token);

        _logger?.LogInformation("Mailbox polling started for {AgentId} in session {SessionId}", agentId, sessionId);
    }

    public void StopPolling(string agentId, string sessionId)
    {
        var key = GetPollingKey(agentId, sessionId);
        if (_pollingAgents.TryRemove(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger?.LogInformation("Mailbox polling stopped for {AgentId} in session {SessionId}", agentId, sessionId);
        }
    }

    private async Task PollLoopAsync(string agentId, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var cursor = await _mailboxService.GetOrCreateCursorAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);

                    var unreadMessages = await _mailboxService.ReadUnreadAsync(
                        agentId, sessionId, cancellationToken).ConfigureAwait(false);

                    if (unreadMessages.Count == 0) continue;

                    var messageIds = new List<string>(unreadMessages.Count);

                    for (var i = 0; i < unreadMessages.Count; i++)
                    {
                        var mailboxMsg = unreadMessages[i];
                        messageIds.Add(mailboxMsg.MessageId);

                        var brokerMessage = new CoordinatorAgentMessage
                        {
                            FromAgentId = mailboxMsg.FromAgentId,
                            ToAgentId = mailboxMsg.ToAgentId,
                            MessageType = mailboxMsg.MessageType,
                            Content = mailboxMsg.Content
                        };

                        await _messageBroker.SendMessageAsync(agentId, brokerMessage, cancellationToken).ConfigureAwait(false);
                    }

                    await _mailboxService.MarkAsReadAsync(agentId, sessionId, messageIds, cancellationToken).ConfigureAwait(false);

                    _logger?.LogDebug("Polled {Count} new messages for {AgentId}", unreadMessages.Count, agentId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error during mailbox polling for {AgentId}", agentId);
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Mailbox polling loop terminated unexpectedly for {AgentId}", agentId);
        }
    }

    private static string GetPollingKey(string agentId, string sessionId)
    {
        return $"{sessionId}:{agentId}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        foreach (var kvp in _pollingAgents)
        {
            await kvp.Value.CancelAsync().ConfigureAwait(false);
            kvp.Value.Dispose();
        }

        _pollingAgents.Clear();
    }
}
