namespace Core.Agents.Coordinator;

/// <summary>队友邮箱服务 — 管理各队友邮箱目录的读写、消息投递与持久化，实现 IDisposable 释放写锁。
/// <para>crossProcess=true 时，写操作用 FileMailboxLock 跨进程互斥，支持多 jcc.exe 进程并发。</para>
/// <para>crossProcess=false 时（默认），写操作仅用 AsyncLock 进程内互斥，零跨进程开销。</para>
/// </summary>
[Register(typeof(ITeammateMailboxService), ServiceLifetime.Singleton)]
public sealed partial class TeammateMailboxService : ServiceEntity, ITeammateMailboxService, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly string _mailboxRoot;
    private readonly ILogger<TeammateMailboxService>? _logger;
    private readonly IClockService _clock;
    private readonly bool _crossProcess;
    private readonly AsyncLock _writeLock = new();
    private readonly ConcurrentDictionary<string, AsyncLock> _agentLocks;
    private readonly ConcurrentDictionary<string, MailboxReadCursor> _cursors;
    private int _messageCounter;

    /// <summary>
    /// 初始化 Teammate 邮箱服务
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="mailboxRoot">邮箱根目录（默认用户目录下 jcc/mailbox）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="crossProcess">是否启用跨进程锁（多 jcc.exe 进程并发时设为 true）</param>
    public TeammateMailboxService(
        IFileSystem fs,
        string? mailboxRoot = null,
        ILogger<TeammateMailboxService>? logger = null,
        IClockService? clock = null,
        bool crossProcess = false)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _mailboxRoot = mailboxRoot
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                AppDataConstants.AppDataFolder,
                AppDataConstants.MailboxFolderName);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _crossProcess = crossProcess;

        _agentLocks = new ConcurrentDictionary<string, AsyncLock>();
        _cursors = new ConcurrentDictionary<string, MailboxReadCursor>();
    }

    /// <summary>
    /// 异步发送邮箱消息到指定智能体，追加写入邮箱文件
    /// </summary>
    /// <param name="request">发送请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已发送的邮箱消息</returns>
    public async Task<MailboxMessage> SendAsync(MailboxSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToAgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);

        var message = new MailboxMessage
        {
            MessageId = GenerateMessageId(),
            FromAgentId = request.FromAgentId,
            ToAgentId = request.ToAgentId,
            MessageType = request.MessageType,
            Content = request.Content,
            SessionId = request.SessionId,
            Timestamp = _clock.GetUtcNow(),
            IsRead = false
        };

        var agentLock = _agentLocks.GetOrAdd(request.ToAgentId, _ => new AsyncLock(nameof(TeammateMailboxService)));
        using var guard = await agentLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{agentLock.Name}' 等待超时");
        try
        {
            EnsureMailboxDirectoryExists(request.SessionId, request.ToAgentId);
            var filePath = GetMailboxFilePath(request.SessionId, request.ToAgentId);
            var line = JsonSerializer.Serialize(message, MailboxJsonContext.Default.MailboxMessage);

            if (_crossProcess)
            {
                await using var fileLock = await FileMailboxLock.AcquireAsync(filePath, TimeSpan.FromSeconds(30), cancellationToken, _logger).ConfigureAwait(false);
                await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to send mailbox message to {AgentId}", request.ToAgentId);
        }

        _logger?.LogDebug("Mailbox message sent: {MessageId} from {FromId} to {ToId}",
            message.MessageId, message.FromAgentId, message.ToAgentId);

        return message;
    }

    /// <summary>
    /// 异步读取指定智能体的未读消息，基于游标位置增量读取
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>未读消息只读列表</returns>
    public async Task<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var cursor = await GetOrCreateCursorAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);
        return await ReadSinceAsync(agentId, sessionId, cursor.LastReadLineIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步读取自指定行索引之后的所有消息
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="sinceLineIndex">起始行索引</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>消息只读列表</returns>
    public async Task<IReadOnlyList<MailboxMessage>> ReadSinceAsync(
        string agentId, string sessionId, int sinceLineIndex, CancellationToken cancellationToken = default)
    {
        var filePath = GetMailboxFilePath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return Array.Empty<MailboxMessage>();
        }

        var agentLock = _agentLocks.GetOrAdd(agentId, _ => new AsyncLock(nameof(TeammateMailboxService)));
        using var guard = await agentLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{agentLock.Name}' 等待超时");
        return await ReadMessagesFromFileAsync(filePath, sinceLineIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步将指定消息标记为已读，并更新游标位置
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="messageIds">需标记为已读的消息标识集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task MarkAsReadAsync(
        string agentId, string sessionId, IEnumerable<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetMailboxFilePath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return;
        }

        var idSet = new HashSet<string>(messageIds);
        if (idSet.Count == 0) return;

        var agentLock = _agentLocks.GetOrAdd(agentId, _ => new AsyncLock(nameof(TeammateMailboxService)));
        using var guard = await agentLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{agentLock.Name}' 等待超时");
        {
            var allMessages = await ReadMessagesFromFileAsync(filePath, 0, cancellationToken).ConfigureAwait(false);
            var modified = false;

            for (var i = 0; i < allMessages.Count; i++)
            {
                if (idSet.Contains(allMessages[i].MessageId) && !allMessages[i].IsRead)
                {
                    allMessages[i].IsRead = true;
                    modified = true;
                }
            }

            if (modified)
            {
                await RewriteMailboxFileAsync(filePath, allMessages, cancellationToken).ConfigureAwait(false);
            }

            var lastLineIndex = allMessages.Count;
            var cursorKey = GetCursorKey(agentId, sessionId);
            _cursors.AddOrUpdate(cursorKey,
                _ => new MailboxReadCursor
                {
                    AgentId = agentId, SessionId = sessionId, LastReadLineIndex = lastLineIndex
                },
                (_, existing) => existing with { LastReadLineIndex = lastLineIndex });
        }
    }

    /// <summary>
    /// 异步获取指定智能体的未读消息数量
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>未读消息数量</returns>
    public async Task<int> GetUnreadCountAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var messages = await ReadUnreadAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);
        return messages.Count(m => !m.IsRead);
    }

    /// <summary>
    /// 异步获取或创建指定智能体与会话的邮箱读取游标
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>邮箱读取游标</returns>
    public Task<MailboxReadCursor> GetOrCreateCursorAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var cursorKey = GetCursorKey(agentId, sessionId);

        if (_cursors.TryGetValue(cursorKey, out var cursor))
        {
            return Task.FromResult(cursor);
        }

        cursor = new MailboxReadCursor
        {
            AgentId = agentId,
            SessionId = sessionId,
            LastReadLineIndex = 0
        };

        _cursors[cursorKey] = cursor;
        return Task.FromResult(cursor);
    }

    /// <summary>
    /// 异步读取指定智能体的全部消息
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>消息只读列表</returns>
    public async Task<IReadOnlyList<MailboxMessage>> ReadAllAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        return await ReadSinceAsync(agentId, sessionId, 0, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MailboxMessage>> ReadMessagesFromFileAsync(
        string filePath, int sinceLineIndex, CancellationToken cancellationToken)
    {
        try
        {
            var lines = await _fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var messages = new List<MailboxMessage>();

            for (var i = sinceLineIndex; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var msg = RelaxedJsonSerializer.Deserialize(line, MailboxJsonContext.Default.MailboxMessage);
                    if (msg is not null)
                    {
                        messages.Add(msg);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "Skipping malformed mailbox line at index {LineIndex}", i);
                }
            }

            return messages;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to read mailbox file: {FilePath}", filePath);
            return Array.Empty<MailboxMessage>();
        }
    }

    private async Task RewriteMailboxFileAsync(
        string filePath, IReadOnlyList<MailboxMessage> messages, CancellationToken cancellationToken)
    {
        using var guard = await _writeLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_writeLock.Name}' 等待超时");

        if (_crossProcess)
        {
            await using var fileLock = await FileMailboxLock.AcquireAsync(filePath, TimeSpan.FromSeconds(30), cancellationToken, _logger).ConfigureAwait(false);
            await WriteMessagesAsync(filePath, messages, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteMessagesAsync(filePath, messages, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteMessagesAsync(
        string filePath, IReadOnlyList<MailboxMessage> messages, CancellationToken cancellationToken)
    {
        await using var stream = _fs.CreateStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);

        for (var i = 0; i < messages.Count; i++)
        {
            var line = JsonSerializer.Serialize(messages[i], MailboxJsonContext.Default.MailboxMessage);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private string GetMailboxFilePath(string sessionId, string agentId)
    {
        ValidateId(sessionId, nameof(sessionId));
        ValidateId(agentId, nameof(agentId));
        return Path.Combine(_mailboxRoot, sessionId, $"{agentId}.json");
    }

    private void EnsureMailboxDirectoryExists(string sessionId, string agentId)
    {
        var dir = Path.Combine(_mailboxRoot, sessionId);
        if (!_fs.DirectoryExists(dir))
        {
            _fs.CreateDirectory(dir);
        }
    }

    private static string GetCursorKey(string agentId, string sessionId)
    {
        return $"{sessionId}:{agentId}";
    }

    private string GenerateMessageId()
    {
        var counter = Interlocked.Increment(ref _messageCounter);
        return $"mail_{counter:D6}_{_clock.GetUtcNow():yyyyMMddHHmmssfff}";
    }

    private static void ValidateId(string id, string paramName)
    {
        foreach (var c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                throw new ArgumentException($"ID contains invalid character: '{c}'", paramName);
            }
        }
    }

    /// <summary>释放资源 — 释放邮箱写锁</summary>
    protected override void OnDispose()
    {
        _writeLock.Dispose();
        foreach (var agentLock in _agentLocks.Values)
        {
            agentLock.Dispose();
        }
        _agentLocks.Clear();
    }
}

