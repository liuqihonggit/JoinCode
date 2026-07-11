namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateMailboxService
{
    Task<MailboxMessage> SendAsync(MailboxSendRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailboxMessage>> ReadSinceAsync(string agentId, string sessionId, int sinceLineIndex, CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(string agentId, string sessionId, IEnumerable<string> messageIds, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);

    Task<MailboxReadCursor> GetOrCreateCursorAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailboxMessage>> ReadAllAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);
}
