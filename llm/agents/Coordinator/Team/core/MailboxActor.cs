namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱 Actor 命令基类 — 通过 Actor 邮箱串行化写操作，无需锁
/// </summary>
internal abstract record MailboxCommand;

/// <summary>追加消息命令</summary>
internal sealed record AppendMessageCmd(MailboxMessage Message, TaskCompletionSource<MailboxMessage> Tcs) : MailboxCommand;

/// <summary>标记已读命令</summary>
internal sealed record MarkAsReadCmd(HashSet<string> MessageIds, TaskCompletionSource Tcs) : MailboxCommand;

/// <summary>
/// 邮箱 Actor — 单 Consumer 线程独占访问邮箱文件，无锁串行化写操作。
/// <para>crossProcess=true 时，写操作用 FileMailboxLock 跨进程互斥。</para>
/// <para>crossProcess=false 时，纯进程内 Actor 串行化，零锁零跨进程开销。</para>
/// </summary>
internal sealed class MailboxActor : ActorBase<MailboxCommand, Unit>
{
    private readonly IFileSystem _fs;
    private readonly string _filePath;
    private readonly bool _crossProcess;
    private readonly ILogger? _logger;

    /// <summary>
    /// 创建邮箱 Actor
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="filePath">邮箱文件路径</param>
    /// <param name="crossProcess">是否启用跨进程锁</param>
    /// <param name="logger">日志记录器</param>
    public MailboxActor(IFileSystem fs, string filePath, bool crossProcess, ILogger? logger)
        : base(new ActorBackpressure(100, BoundedChannelFullMode.Wait), null)
    {
        _fs = fs;
        _filePath = filePath;
        _crossProcess = crossProcess;
        _logger = logger;
    }

    /// <summary>
    /// 处理邮箱命令 — Consumer 线程独占执行，无需锁
    /// </summary>
    protected override async ValueTask HandleAsync(MailboxCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case AppendMessageCmd append:
                await AppendMessageCoreAsync(append.Message, ct).ConfigureAwait(false);
                append.Tcs.SetResult(append.Message);
                break;
            case MarkAsReadCmd mark:
                await MarkAsReadCoreAsync(mark.MessageIds, ct).ConfigureAwait(false);
                mark.Tcs.SetResult();
                break;
        }
    }

    private async ValueTask AppendMessageCoreAsync(MailboxMessage message, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(message, MailboxJsonContext.Default.MailboxMessage);
        if (_crossProcess)
        {
            await using var fileLock = await FileMailboxLock.AcquireAsync(_filePath, TimeSpan.FromSeconds(30), ct, _logger).ConfigureAwait(false);
            await _fs.AppendAllTextAsync(_filePath, line + '\n', ct).ConfigureAwait(false);
        }
        else
        {
            await _fs.AppendAllTextAsync(_filePath, line + '\n', ct).ConfigureAwait(false);
        }
    }

    private async ValueTask MarkAsReadCoreAsync(HashSet<string> messageIds, CancellationToken ct)
    {
        if (!_fs.FileExists(_filePath)) return;

        var lines = await _fs.ReadAllLinesAsync(_filePath, ct).ConfigureAwait(false);
        var messages = new List<MailboxMessage>();
        var modified = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var msg = RelaxedJsonSerializer.Deserialize(line, MailboxJsonContext.Default.MailboxMessage);
                if (msg is null) continue;
                if (messageIds.Contains(msg.MessageId) && !msg.IsRead)
                {
                    msg.IsRead = true;
                    modified = true;
                }
                messages.Add(msg);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "MailboxActor: skipping malformed mailbox line");
            }
        }

        if (!modified) return;

        if (_crossProcess)
        {
            await using var fileLock = await FileMailboxLock.AcquireAsync(_filePath, TimeSpan.FromSeconds(30), ct, _logger).ConfigureAwait(false);
            await RewriteFileCoreAsync(messages, ct).ConfigureAwait(false);
        }
        else
        {
            await RewriteFileCoreAsync(messages, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask RewriteFileCoreAsync(IReadOnlyList<MailboxMessage> messages, CancellationToken ct)
    {
        await using var stream = _fs.CreateStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        for (var i = 0; i < messages.Count; i++)
        {
            var line = JsonSerializer.Serialize(messages[i], MailboxJsonContext.Default.MailboxMessage);
            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
    }
}
