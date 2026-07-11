namespace JoinCode.Abstractions.Interfaces;

public interface IAgentMessageBroker
{
    void RegisterAgent(string agentId, string? sessionId = null);
    void UnregisterAgent(string agentId);
    Task<bool> SendMessageAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default);
    Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CoordinatorMessage> ReadMessagesAsync(string agentId, CancellationToken cancellationToken = default);
    IReadOnlyCollection<string> GetRegisteredAgents();
    string? GetSessionId(string agentId);
}
