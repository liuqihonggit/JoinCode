namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateMailboxService {
    /// <summary>异步发送消息。</summary>
    ValueTask<MailboxMessage> SendAsync(MailboxSendRequest request, CancellationToken ct = default);

    /// <summary>异步读取未读消息。</summary>
    ValueTask<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(string agentId, string sessionId, CancellationToken ct = default);

    /// <summary>异步读取自指定行索引以来的消息。</summary>
    ValueTask<IReadOnlyList<MailboxMessage>> ReadSinceAsync(string agentId, string sessionId, int sinceLineIndex, CancellationToken ct = default);

    /// <summary>异步标记消息为已读。</summary>
    ValueTask MarkAsReadAsync(string agentId, string sessionId, IEnumerable<string> messageIds, CancellationToken ct = default);

    /// <summary>异步获取未读消息数。</summary>
    ValueTask<int> GetUnreadCountAsync(string agentId, string sessionId, CancellationToken ct = default);

    /// <summary>异步获取或创建读取游标。</summary>
    ValueTask<MailboxReadCursor> GetOrCreateCursorAsync(string agentId, string sessionId, CancellationToken ct = default);

    /// <summary>异步读取全部消息。</summary>
    ValueTask<IReadOnlyList<MailboxMessage>> ReadAllAsync(string agentId, string sessionId, CancellationToken ct = default);
}