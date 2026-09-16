namespace Core.Agents.Coordinator;

/// <summary>队友邮箱服务 — 基于 MailboxActor 串行化写操作，无锁防死锁。
/// <para>crossProcess=true 时，Actor 内部用 FileMailboxLock 跨进程互斥，支持多 jcc.exe 进程并发。</para>
/// <para>crossProcess=false 时（默认），纯 Actor 串行化，零锁零跨进程开销。</para>
/// <para>写操作通过 Actor 邮箱消息传递串行化，读操作直接读文件（幂等）。</para>
/// </summary>
[Register(typeof(ITeammateMailboxService), ServiceLifetime.Singleton)]
public sealed partial class TeammateMailboxService : ServiceEntity, ITeammateMailboxService, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly string _mailboxRoot;
    private readonly ILogger<TeammateMailboxService>? _logger;
    private readonly IClockService _clock;
    private readonly bool _crossProcess;
    private readonly ConcurrentDictionary<string, MailboxActor> _actors;
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

        _actors = new ConcurrentDictionary<string, MailboxActor>();
        _cursors = new ConcurrentDictionary<string, MailboxReadCursor>();
    }

    /// <summary>
    /// 异步发送邮箱消息到指定智能体 — 通过 Actor 邮箱串行化写入
    /// </summary>
    /// <param name="request">发送请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已发送的邮箱消息</returns>
    public async ValueTask<MailboxMessage> SendAsync(MailboxSendRequest request, CancellationToken ct = default)
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

        EnsureMailboxDirectoryExists(request.SessionId, request.ToAgentId);
        var actor = GetOrCreateActor(request.SessionId, request.ToAgentId);
        var tcs = new TaskCompletionSource<MailboxMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await actor.SendAsync(new AppendMessageCmd(message, tcs), ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to send mailbox message to {AgentId}", request.ToAgentId);
            return message;
        }
    }

    /// <summary>
    /// 异步读取指定智能体的未读消息，基于游标位置增量读取
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>未读消息只读列表</returns>
    public async ValueTask<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(
        string agentId, string sessionId, CancellationToken ct = default)
    {
        var cursor = await GetOrCreateCursorAsync(agentId, sessionId, ct).ConfigureAwait(false);
        return await ReadSinceAsync(agentId, sessionId, cursor.LastReadLineIndex, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步读取自指定行索引之后的所有消息 — 直接读文件，无锁
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="sinceLineIndex">起始行索引</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息只读列表</returns>
    public async ValueTask<IReadOnlyList<MailboxMessage>> ReadSinceAsync(
        string agentId, string sessionId, int sinceLineIndex, CancellationToken ct = default)
    {
        var filePath = GetMailboxFilePath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return Array.Empty<MailboxMessage>();
        }

        return await ReadMessagesFromFileAsync(filePath, sinceLineIndex, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步将指定消息标记为已读 — 通过 Actor 邮箱串行化读-改-写
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="messageIds">需标记为已读的消息标识集合</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async ValueTask MarkAsReadAsync(
        string agentId, string sessionId, IEnumerable<string> messageIds,
        CancellationToken ct = default)
    {
        var filePath = GetMailboxFilePath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return;
        }

        var idSet = new HashSet<string>(messageIds);
        if (idSet.Count == 0) return;

        var actor = GetOrCreateActor(sessionId, agentId);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await actor.SendAsync(new MarkAsReadCmd(idSet, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);

        var lines = await _fs.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        var lastLineIndex = lines.Length;
        var cursorKey = GetCursorKey(agentId, sessionId);
        _cursors.AddOrUpdate(cursorKey,
            _ => new MailboxReadCursor
            {
                AgentId = agentId, SessionId = sessionId, LastReadLineIndex = lastLineIndex
            },
            (_, existing) => existing with { LastReadLineIndex = lastLineIndex });
    }

    /// <summary>
    /// 异步获取指定智能体的未读消息数量
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>未读消息数量</returns>
    public async ValueTask<int> GetUnreadCountAsync(
        string agentId, string sessionId, CancellationToken ct = default)
    {
        var messages = await ReadUnreadAsync(agentId, sessionId, ct).ConfigureAwait(false);
        return messages.Count(m => !m.IsRead);
    }

    /// <summary>
    /// 异步获取或创建指定智能体与会话的邮箱读取游标
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>邮箱读取游标</returns>
    public ValueTask<MailboxReadCursor> GetOrCreateCursorAsync(
        string agentId, string sessionId, CancellationToken ct = default)
    {
        var cursorKey = GetCursorKey(agentId, sessionId);

        if (_cursors.TryGetValue(cursorKey, out var cursor))
        {
            return ValueTask.FromResult(cursor);
        }

        cursor = new MailboxReadCursor
        {
            AgentId = agentId,
            SessionId = sessionId,
            LastReadLineIndex = 0
        };

        _cursors[cursorKey] = cursor;
        return ValueTask.FromResult(cursor);
    }

    /// <summary>
    /// 异步读取指定智能体的全部消息
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息只读列表</returns>
    public async ValueTask<IReadOnlyList<MailboxMessage>> ReadAllAsync(
        string agentId, string sessionId, CancellationToken ct = default)
    {
        return await ReadSinceAsync(agentId, sessionId, 0, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MailboxMessage>> ReadMessagesFromFileAsync(
        string filePath, int sinceLineIndex, CancellationToken ct)
    {
        try
        {
            var lines = await _fs.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            var messages = new List<MailboxMessage>();

            for (var i = sinceLineIndex; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var msg = RelaxedJsonSerializer.Deserialize(line, MailboxJsonContext.Default.CoordinatorMessage);
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

    private MailboxActor GetOrCreateActor(string sessionId, string agentId)
    {
        var key = GetCursorKey(agentId, sessionId);
        return _actors.GetOrAdd(key, _ =>
        {
            var filePath = GetMailboxFilePath(sessionId, agentId);
            return new MailboxActor(_fs, filePath, _crossProcess, _logger);
        });
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

    /// <summary>释放资源 — 后台释放所有 MailboxActor</summary>
    public override void Dispose()
    {
        foreach (var actor in _actors.Values)
        {
            _ = Task.Run(async () => await actor.DisposeAsync().ConfigureAwait(false));
        }
        _actors.Clear();
            base.Dispose();
    }
}
