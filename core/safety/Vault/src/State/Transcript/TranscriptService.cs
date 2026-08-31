namespace State;

[Register(typeof(ITranscriptService), ServiceLifetime.Singleton)]
public sealed partial class TranscriptService : ServiceEntity, ITranscriptService, IDisposable
{
    private readonly string _sessionsDirectory;
    private readonly ILogger<TranscriptService>? _logger;
    private readonly IClockService _clock;
    private readonly TranscriptFileWriter _writer;
    private readonly IFileSystem _fs;

    public TranscriptService(IFileSystem fs, string? sessionsDirectory = null, ILogger<TranscriptService>? logger = null, IClockService? clock = null, IPasteStore? pasteStore = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _writer = new TranscriptFileWriter(_fs, _sessionsDirectory, logger, pasteStore);
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

    public async Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_sessionsDirectory))
        {
            return Array.Empty<TranscriptSummary>();
        }

        try
        {
            var summaries = new List<TranscriptSummary>();

            foreach (var dir in _fs.EnumerateDirectories(_sessionsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var sessionId = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(sessionId)) continue;

                    var transcriptPath = Path.Combine(dir, "transcript.json");
                    if (!_fs.FileExists(transcriptPath)) continue;

                    int entryCount = 0;
                    string? preview = null;

                    try
                    {
                        using var stream = _fs.CreateStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var entries = RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.ListTranscriptEntry);
                            if (entries is not null)
                            {
                                entryCount = entries.Count;
                                if (entries.Count > 0)
                                {
                                    preview = entries[^1].Content;
                                    if (preview is not null && preview.Length > 80)
                                    {
                                        preview = preview[..80] + "...";
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // preview extraction 非关键，保留日志可见性
                        _logger?.LogDebug(ex, "TranscriptService: 会话摘要读取失败");
                    }

                    summaries.Add(new TranscriptSummary
                    {
                        SessionId = sessionId,
                        CreatedAt = _fs.GetCreationTimeUtc(dir),
                        LastModifiedAt = _fs.GetLastWriteTimeUtc(transcriptPath),
                        MessageCount = entryCount,
                        LastMessagePreview = preview
                    });
                }
                catch (Exception ex)
                {
                    // 跳过不可读目录，保留日志可见性
                    _logger?.LogWarning(ex, "TranscriptService: 跳过不可读会话目录");
                }
            }

            var result = summaries
                .OrderByDescending(s => s.LastModifiedAt)
                .Take(limit)
                .ToList();

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to list transcripts");
            return Array.Empty<TranscriptSummary>();
        }
    }

    public Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessionDir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(sessionDir))
        {
            return Task.FromResult(false);
        }

        try
        {
            _fs.DeleteDirectory(sessionDir, recursive: true);
            _logger?.LogInformation("Session directory deleted for {SessionId}", sessionId);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to delete session directory for {SessionId}", sessionId);
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
                    var deserialized = RelaxedJsonSerializer.Deserialize(entry.Content, ContentReplacementRecordListJsonContext.Default.ListContentReplacementRecord);
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
        return Path.Combine(_sessionsDirectory, sessionId, "transcript.json");
    }

    private string GetSessionDir(string sessionId)
    {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId);
    }

    private string GetMetaPath(string sessionId)
    {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId, "meta.json");
    }

    /// <summary>
    /// 保存会话信息到 {sessionId}/meta.json — 统一入口,替代 SessionData 直写
    /// </summary>
    public async Task SaveSessionInfoAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(info);

        var metaPath = GetMetaPath(sessionId);
        var dir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(dir))
        {
            DirectoryHelper.EnsureDirectoryExists(_fs, dir);
        }

        var infoWithId = info with { Id = sessionId };
        if (string.IsNullOrEmpty(infoWithId.ProjectName) && !string.IsNullOrEmpty(infoWithId.ProjectPath))
        {
            infoWithId = infoWithId with { ProjectName = Path.GetFileName(infoWithId.ProjectPath) };
        }
        var json = RelaxedJsonSerializer.Serialize(infoWithId, TranscriptJsonContext.Default);
        await _fs.WriteAllTextAsync(metaPath, json, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Session info saved for {SessionId}", sessionId);
    }

    /// <summary>
    /// 加载会话信息 — 不存在或损坏返回 null
    /// </summary>
    public async Task<SessionInfo?> GetSessionInfoAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var metaPath = GetMetaPath(sessionId);
        if (!_fs.FileExists(metaPath)) return null;

        try
        {
            var json = await _fs.ReadAllTextAsync(metaPath, cancellationToken).ConfigureAwait(false);
            return RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.SessionInfo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to read session info for {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// 迁移旧扁平 .json(直接在 sessions 根目录)到每会话子目录 {id}/transcript.json — 幂等,不删旧文件
    /// 旧格式为 JSONL(每行一个 JSON),新格式为 JSON 数组(带缩进,人类可读)
    /// </summary>
    public async Task MigrateLegacyAsync(CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_sessionsDirectory)) return;

        foreach (var file in _fs.EnumerateFiles(_sessionsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(sessionId)) continue;

            try
            {
                var newDir = GetSessionDir(sessionId);
                var newPath = Path.Combine(newDir, "transcript.json");
                if (_fs.FileExists(newPath)) continue; // 幂等

                // 读旧 JSONL(逐行解析)转 JSON 数组
                var lines = await _fs.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
                var entries = new List<TranscriptEntry>(lines.Length);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = RelaxedJsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
                        if (entry is not null) entries.Add(entry);
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning(ex, "Skipping malformed line in legacy transcript {SessionId}", sessionId);
                    }
                }

                if (!_fs.DirectoryExists(newDir))
                {
                    DirectoryHelper.EnsureDirectoryExists(_fs, newDir);
                }
                var json = RelaxedJsonSerializer.Serialize(entries, TranscriptJsonContext.Default);
                await _fs.WriteAllTextAsync(newPath, json, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Migrated legacy transcript {SessionId} to session directory (jsonl→json)", sessionId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Failed to migrate legacy transcript {SessionId}", sessionId);
            }
        }
    }

    protected override void OnDispose() => _writer.Dispose();
}
