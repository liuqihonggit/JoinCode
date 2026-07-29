namespace State;

[Register]
public sealed partial class TranscriptService : ITranscriptService, IDisposable
{
    private readonly string _sessionsDirectory;
    [Inject] private readonly ILogger<TranscriptService>? _logger;
    private readonly IClockService _clock;
    private readonly TranscriptFileWriter _writer;
    private readonly IFileSystem _fs;

    public TranscriptService(IFileSystem fs, string? sessionsDirectory = null, ILogger<TranscriptService>? logger = null, IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _writer = new TranscriptFileWriter(_fs, _sessionsDirectory, logger);
    }

    public async Task AppendEntryAsync(string sessionId, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entry);

        var filePath = GetTranscriptPath(sessionId);
        var entryWithSessionId = entry.WithSessionId(sessionId);
        await _writer.AppendEntryAsync(filePath, entryWithSessionId, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Transcript entry appended for session {SessionId}", sessionId);
    }

    public async Task AppendEntriesAsync(string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0) return;

        var filePath = GetTranscriptPath(sessionId);
        var entriesWithSessionId = entries.Select(e => e.WithSessionId(sessionId)).ToList();
        await _writer.AppendEntriesAsync(filePath, entriesWithSessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var filePath = GetTranscriptPath(sessionId);
        return await _writer.LoadTranscriptAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_sessionsDirectory))
        {
            return Task.FromResult<IReadOnlyList<TranscriptSummary>>(Array.Empty<TranscriptSummary>());
        }

        try
        {
            var summaries = new List<TranscriptSummary>();

            foreach (var file in _fs.EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var sessionId = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(sessionId)) continue;

                    var lineCount = 0;
                    string? lastLine = null;

                    using var stream = _fs.CreateStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);

                    while (reader.ReadLine() is { } line)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        lineCount++;
                        lastLine = line;
                    }

                    string? preview = null;
                    if (lastLine is not null)
                    {
                        try
                        {
                            var lastEntry = JsonSerializer.Deserialize(lastLine, TranscriptJsonContext.Default.TranscriptEntry);
                            preview = lastEntry?.Content;
                            if (preview is not null && preview.Length > 80)
                            {
                                preview = preview[..80] + "...";
                            }
                        }
                        catch (Exception ex)
                        {
                            // ignore - preview extraction is non-critical
                            System.Diagnostics.Trace.WriteLine($"TranscriptService: Failed to extract preview from last entry: {ex.Message}");
                        }
                    }

                    summaries.Add(new TranscriptSummary
                    {
                        SessionId = sessionId,
                        CreatedAt = _fs.GetCreationTimeUtc(file),
                        LastModifiedAt = _fs.GetLastWriteTimeUtc(file),
                        MessageCount = lineCount,
                        LastMessagePreview = preview
                    });
                }
                catch (Exception ex)
                {
                    // skip unreadable files
                    System.Diagnostics.Trace.WriteLine($"TranscriptService: Skipping unreadable session file: {ex.Message}");
                }
            }

            var result = summaries
                .OrderByDescending(s => s.LastModifiedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<TranscriptSummary>>(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to list transcripts");
            return Task.FromResult<IReadOnlyList<TranscriptSummary>>(Array.Empty<TranscriptSummary>());
        }
    }

    public Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var filePath = GetTranscriptPath(sessionId);
        if (!_fs.FileExists(filePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            _fs.DeleteFile(filePath);
            _logger?.LogInformation("Transcript deleted for session {SessionId}", sessionId);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to delete transcript for session {SessionId}", sessionId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> TranscriptExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var filePath = GetTranscriptPath(sessionId);
        return Task.FromResult(_fs.FileExists(filePath));
    }

    public async Task SaveCustomTitleAsync(string sessionId, string customTitle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(customTitle);

        var entry = new TranscriptEntry
        {
            SessionId = sessionId,
            Role = "system",
            Content = $"Session renamed to: {customTitle}",
            Timestamp = _clock.GetUtcNow(),
            Type = "custom-title",
            CustomTitle = customTitle
        };

        await AppendEntryAsync(sessionId, entry, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Custom title saved for session {SessionId}: {Title}", sessionId, customTitle);
    }

    public async Task<string?> GetCustomTitleAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var entries = await LoadTranscriptAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // 从后往前扫描，找到最近的 custom-title 条目
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Type == "custom-title" && !string.IsNullOrEmpty(entries[i].CustomTitle))
            {
                return entries[i].CustomTitle;
            }
        }

        return null;
    }

    /// <summary>
    /// 对齐 TS recordContentReplacement — 持久化内容替换记录到 transcript
    /// 使用 TranscriptEntry.Type = "content-replacement" 存储
    /// Content 字段存储 JSON 序列化的记录数组
    /// </summary>
    public async Task InsertContentReplacementAsync(string sessionId, IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return;

        var json = JsonSerializer.Serialize(records, ContentReplacementRecordListJsonContext.Default.ListContentReplacementRecord);
        var entry = new TranscriptEntry
        {
            SessionId = sessionId,
            Role = "system",
            Type = "content-replacement",
            Content = json,
            Timestamp = _clock.GetUtcNow(),
        };

        await AppendEntryAsync(sessionId, entry, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Content replacement records persisted for session {SessionId}: {Count} records", sessionId, records.Count);
    }

    /// <summary>
    /// 对齐 TS loadTranscriptFile — 从 transcript 加载内容替换记录
    /// </summary>
    public async Task<IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord>> LoadContentReplacementsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var entries = await LoadTranscriptAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var records = new List<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord>();

        foreach (var entry in entries)
        {
            if (entry.Type == "content-replacement" && !string.IsNullOrEmpty(entry.Content))
            {
                try
                {
                    var deserialized = JsonSerializer.Deserialize(entry.Content, ContentReplacementRecordListJsonContext.Default.ListContentReplacementRecord);
                    if (deserialized is not null)
                    {
                        records.AddRange(deserialized);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "Skipping malformed content-replacement entry in session {SessionId}", sessionId);
                }
            }
        }

        return records;
    }

    private string GetTranscriptPath(string sessionId)
    {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, $"{sessionId}.jsonl");
    }

    public void Dispose() => _writer.Dispose();
}
