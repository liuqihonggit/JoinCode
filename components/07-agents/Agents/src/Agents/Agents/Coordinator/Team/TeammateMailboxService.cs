namespace Core.Agents.Coordinator;

[Register]
public sealed partial class TeammateMailboxService : ITeammateMailboxService, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly string _mailboxRoot;
    [Inject] private readonly ILogger<TeammateMailboxService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly SemaphoreSlim _writeLock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentLocks;
    private readonly ConcurrentDictionary<string, MailboxReadCursor> _cursors;
    private int _messageCounter;

    public TeammateMailboxService(
        IFileSystem fs,
        string? mailboxRoot = null,
        ILogger<TeammateMailboxService>? logger = null,
        IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _mailboxRoot = mailboxRoot
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataConstants.AppDataFolder,
                AppDataConstants.MailboxFolderName);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _writeLock = new SemaphoreSlim(1, 1);
        _agentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _cursors = new ConcurrentDictionary<string, MailboxReadCursor>();
    }

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

        var agentLock = _agentLocks.GetOrAdd(request.ToAgentId, _ => new SemaphoreSlim(1, 1));
        await agentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureMailboxDirectoryExists(request.SessionId, request.ToAgentId);
            var filePath = GetMailboxFilePath(request.SessionId, request.ToAgentId);
            var line = JsonSerializer.Serialize(message, MailboxJsonContext.Default.MailboxMessage);
            await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to send mailbox message to {AgentId}", request.ToAgentId);
        }
        finally
        {
            agentLock.Release();
        }

        _logger?.LogDebug("Mailbox message sent: {MessageId} from {FromId} to {ToId}",
            message.MessageId, message.FromAgentId, message.ToAgentId);

        return message;
    }

    public async Task<IReadOnlyList<MailboxMessage>> ReadUnreadAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var cursor = await GetOrCreateCursorAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);
        return await ReadSinceAsync(agentId, sessionId, cursor.LastReadLineIndex, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MailboxMessage>> ReadSinceAsync(
        string agentId, string sessionId, int sinceLineIndex, CancellationToken cancellationToken = default)
    {
        var filePath = GetMailboxFilePath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return Array.Empty<MailboxMessage>();
        }

        var agentLock = _agentLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await agentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadMessagesFromFileAsync(filePath, sinceLineIndex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            agentLock.Release();
        }
    }

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

        var agentLock = _agentLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await agentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            agentLock.Release();
        }
    }

    public async Task<int> GetUnreadCountAsync(
        string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var messages = await ReadUnreadAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);
        return messages.Count(m => !m.IsRead);
    }

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
                    var msg = JsonSerializer.Deserialize(line, MailboxJsonContext.Default.MailboxMessage);
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
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = _fs.CreateStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);

            for (var i = 0; i < messages.Count; i++)
            {
                var line = JsonSerializer.Serialize(messages[i], MailboxJsonContext.Default.MailboxMessage);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string GetMailboxFilePath(string sessionId, string agentId)
    {
        ValidateId(sessionId, nameof(sessionId));
        ValidateId(agentId, nameof(agentId));
        return Path.Combine(_mailboxRoot, sessionId, $"{agentId}.jsonl");
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

    public void Dispose()
    {
        _writeLock.Dispose();
        foreach (var agentLock in _agentLocks.Values)
        {
            agentLock.Dispose();
        }
        _agentLocks.Clear();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(MailboxMessage))]
[JsonSerializable(typeof(MailboxReadCursor))]
[JsonSerializable(typeof(MailboxSendRequest))]
[JsonSerializable(typeof(List<MailboxMessage>))]
public sealed partial class MailboxJsonContext : JsonSerializerContext;
