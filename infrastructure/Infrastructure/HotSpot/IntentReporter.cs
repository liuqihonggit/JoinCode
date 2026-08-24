namespace Infrastructure.HotSpot;

/// <summary>
/// 意图上报器实现 — 收集到 IntentCollector + 热文件契约改发 IMailbox 通知队长
/// 纯新增服务，Worker 执行流在单元C接入时调用
/// </summary>
[Register(typeof(IIntentReporter), ServiceLifetime.Singleton)]
public sealed class IntentReporter : IIntentReporter
{
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IMailbox _mailbox;

    public IntentReporter(IIntentCollector intentCollector, IHotFileDetector hotFileDetector, IMailbox mailbox)
    {
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
    }

    public async Task ReportModifyIntentsAsync(string workerId, string captainId, IReadOnlyList<FileModifyIntent> intents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(captainId);
        ArgumentNullException.ThrowIfNull(intents);

        await _intentCollector.ReportAsync(workerId, intents, cancellationToken).ConfigureAwait(false);

        var hotFileContractIntents = intents
            .Where(i => i.Intent == ModifyIntent.ContractChange && _hotFileDetector.IsHotFile(i.FilePath))
            .ToList();

        if (hotFileContractIntents.Count == 0)
            return;

        var distinctFiles = hotFileContractIntents.Select(i => i.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var message = new CoordinatorMessage
        {
            FromAgentId = workerId,
            ToAgentId = captainId,
            MessageType = TeammateMessageTypeConstants.IntentReport,
            Content = FormattableString.Invariant($"Worker {workerId} 上报热文件契约修改: {string.Join(", ", distinctFiles)}"),
            StructuredType = TeammateMessageType.IntentReport
        };

        await _mailbox.SendAsync(captainId, message, cancellationToken).ConfigureAwait(false);
    }
}
