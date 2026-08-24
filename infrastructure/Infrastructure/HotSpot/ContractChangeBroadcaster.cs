namespace Infrastructure.HotSpot;

/// <summary>
/// 契约变更广播器实现 — 热文件变就通过 IMailbox 定向发给依赖 Worker
/// 非热文件不广播；复用现有 IMailbox 不新建 Broadcaster
/// </summary>
[Register(typeof(IContractChangeBroadcaster), ServiceLifetime.Singleton)]
public sealed class ContractChangeBroadcaster : IContractChangeBroadcaster
{
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IMailbox _mailbox;

    public ContractChangeBroadcaster(IHotFileDetector hotFileDetector, IMailbox mailbox)
    {
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
    }

    public async Task<int> BroadcastContractChangeAsync(string captainId, string filePath, IReadOnlyList<string> dependentWorkers, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(dependentWorkers);

        if (!_hotFileDetector.IsHotFile(filePath))
            return 0;

        if (dependentWorkers.Count == 0)
            return 0;

        var sentCount = 0;
        foreach (var workerId in dependentWorkers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var msg = new CoordinatorMessage
            {
                FromAgentId = captainId,
                ToAgentId = workerId,
                MessageType = TeammateMessageTypeConstants.ContractChanged,
                Content = FormattableString.Invariant($"队长 {captainId} push 热文件 {filePath} 契约变更，请 git pull 同步主干后继续"),
                StructuredType = TeammateMessageType.ContractChanged
            };
            if (await _mailbox.SendAsync(workerId, msg, cancellationToken).ConfigureAwait(false))
                sentCount++;
        }

        return sentCount;
    }
}
