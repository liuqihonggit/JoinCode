namespace Infrastructure.HotSpot;

/// <summary>
/// 意图上报器实现 — 收集到 IntentCollector + 热文件契约改发 IMailbox 通知队长 + 中低优先级标记延迟邮件投递
/// 分流: HotFileConflict(热文件契约改)实时 IMailbox, TestFileConflict/ResourceRefChange 延迟 DeferredMail
/// </summary>
[Register(typeof(IIntentReporter), ServiceLifetime.Singleton)]
public sealed class IntentReporter : IIntentReporter
{
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IMailbox _mailbox;
    private readonly IDeferredMailService _deferredMailService;

    public IntentReporter(IIntentCollector intentCollector, IHotFileDetector hotFileDetector, IMailbox mailbox, IDeferredMailService deferredMailService)
    {
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _deferredMailService = deferredMailService ?? throw new ArgumentNullException(nameof(deferredMailService));
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

        if (hotFileContractIntents.Count > 0)
        {
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

        var deferredIntents = intents
            .Where(i => i.Marker.HasFlag(MailMarker.TestFileConflict) || i.Marker.HasFlag(MailMarker.ResourceRefChange))
            .ToList();

        foreach (var i in deferredIntents)
        {
            var mail = new DeferredMail
            {
                To = captainId,
                From = workerId,
                Subject = FormattableString.Invariant($"延迟通知: {i.FilePath}"),
                Body = FormattableString.Invariant($"Worker {workerId} 上报 {i.Marker}: {i.FilePath}"),
                OpenAfterTurns = 20,
                Marker = i.Marker,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _deferredMailService.DeferAsync(mail, cancellationToken).ConfigureAwait(false);
        }
    }
}
