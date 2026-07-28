namespace Core.Agents.Coordinator;

[Register]
public sealed partial class AgentMessageBroker : IAgentMessageBroker
{
    private readonly ILogger? _logger;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly ConcurrentDictionary<string, Channel<CoordinatorAgentMessage>> _messageChannels;
    private readonly ConcurrentDictionary<string, string> _agentSessions;

    public AgentMessageBroker(ILogger? logger = null, ITeammateMailboxService? mailboxService = null)
    {
        _logger = logger;
        _mailboxService = mailboxService;
        _messageChannels = new ConcurrentDictionary<string, Channel<CoordinatorAgentMessage>>();
        _agentSessions = new ConcurrentDictionary<string, string>();
    }

    public void RegisterAgent(string agentId, string? sessionId = null)
    {
        _messageChannels[agentId] = Channel.CreateUnbounded<CoordinatorAgentMessage>();

        if (sessionId is not null)
        {
            _agentSessions[agentId] = sessionId;
        }
    }

    public void UnregisterAgent(string agentId)
    {
        if (_messageChannels.TryRemove(agentId, out var channel))
        {
            channel.Writer.Complete();
        }

        _agentSessions.TryRemove(agentId, out _);
    }

    public async Task<bool> SendMessageAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var channelDelivered = false;

        if (_messageChannels.TryGetValue(agentId, out var channel))
        {
            await channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            channelDelivered = true;
        }

        await PersistToMailboxAsync(agentId, message, cancellationToken).ConfigureAwait(false);

        return channelDelivered;
    }

    public async Task BroadcastAsync(CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var tasks = _messageChannels
            .Where(kvp => kvp.Key != message.FromAgentId)
            .Select(kvp => SendMessageAsync(kvp.Key, message, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public IAsyncEnumerable<CoordinatorAgentMessage> ReadMessagesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_messageChannels.TryGetValue(agentId, out var channel))
        {
            return channel.Reader.ReadAllAsync(cancellationToken);
        }
        return AsyncEnumerable.Empty<CoordinatorAgentMessage>();
    }

    public IReadOnlyCollection<string> GetRegisteredAgents()
    {
        return _messageChannels.Keys.ToList();
    }

    public string? GetSessionId(string agentId)
    {
        return _agentSessions.GetValueOrDefault(agentId);
    }

    private async Task PersistToMailboxAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken)
    {
        if (_mailboxService is null) return;

        var sessionId = _agentSessions.GetValueOrDefault(agentId);
        if (string.IsNullOrEmpty(sessionId)) return;

        try
        {
            var request = new MailboxSendRequest
            {
                FromAgentId = message.FromAgentId,
                ToAgentId = agentId,
                MessageType = message.MessageType,
                Content = message.Content,
                SessionId = sessionId
            };

            await _mailboxService.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to persist message to mailbox for {AgentId}", agentId);
        }
    }
}
