namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateMailboxService {
    ValueTask<MailboxMessage> SendAsync(MailboxSendRequest request, CancellationToken ct = default);

    ValueTask<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(string agentId, string sessionId, CancellationToken ct = default);

    ValueTask<IReadOnlyList<MailboxMessage>> ReadSinceAsync(string agentId, string sessionId, int sinceLineIndex, CancellationToken ct = default);

    ValueTask MarkAsReadAsync(string agentId, string sessionId, IEnumerable<string> messageIds, CancellationToken ct = default);

    ValueTask<int> GetUnreadCountAsync(string agentId, string sessionId, CancellationToken ct = default);

    ValueTask<MailboxReadCursor> GetOrCreateCursorAsync(string agentId, string sessionId, CancellationToken ct = default);

    ValueTask<IReadOnlyList<MailboxMessage>> ReadAllAsync(string agentId, string sessionId, CancellationToken ct = default);
}