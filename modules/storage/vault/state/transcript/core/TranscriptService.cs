namespace State;

/// <summary>
/// 会话转录服务实现 — 负责将 TranscriptEntry 持久化到每会话子目录,支持加载、列表、删除、自定义标题与旧格式迁移。
/// </summary>
[Register(typeof(ITranscriptService), ServiceLifetime.Singleton)]
public sealed partial class TranscriptService : ServiceEntity, ITranscriptService, IDisposable {
    private readonly string _sessionsDirectory;
    private readonly ILogger<TranscriptService>? _logger;
    private readonly IClockService _clock;
    private readonly TranscriptFileWriter _writer;
    private readonly IFileSystem _fs;
    private bool _disposed;

    /// <summary>
    /// 构造函数 — 注入文件系统、会话目录、日志、时钟与粘贴存储等依赖。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="sessionsDirectory">会话根目录,缺省时使用 AppData 下的 sessions 目录。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="clock">时钟服务,缺省时使用系统时钟。</param>
    /// <param name="pasteStore">粘贴内容存储。</param>
    public TranscriptService(IFileSystem fs, string? sessionsDirectory = null, ILogger<TranscriptService>? logger = null, IClockService? clock = null, IPasteStore? pasteStore = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                AppDataConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _writer = new TranscriptFileWriter(_fs, _sessionsDirectory, logger, pasteStore);
    }

    /// <inheritdoc />
    public async Task AppendEntryAsync(string sessionId, TranscriptEntry entry, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entry);

        var filePath = GetTranscriptPath(sessionId);
        var entryWithSessionId = entry.WithSessionId(sessionId);
        await _writer.AppendEntryAsync(filePath, entryWithSessionId, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Transcript entry appended for session {SessionId}", sessionId);
    }

    /// <inheritdoc />
    public async Task AppendEntriesAsync(string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0) return;

        var filePath = GetTranscriptPath(sessionId);
        var entriesWithSessionId = entries.Select(e => e.WithSessionId(sessionId)).ToList();
        await _writer.AppendEntriesAsync(filePath, entriesWithSessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var filePath = GetTranscriptPath(sessionId);
        return await _writer.LoadTranscriptAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default) {
        if (!_fs.DirectoryExists(_sessionsDirectory)) {
            return Array.Empty<TranscriptSummary>();
        }

        try {
            var summaries = new List<TranscriptSummary>();

            foreach (var dir in _fs.EnumerateDirectories(_sessionsDirectory, "*", SearchOption.TopDirectoryOnly)) {
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    var sessionId = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(sessionId)) continue;

                    var (transcriptPath, isJsonl) = ResolveTranscriptFile(dir);
                    if (transcriptPath is null) continue;

                    var entryCount = 0;
                    string? preview = null;

                    try {
                        await using var stream = _fs.CreateStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        (entryCount, preview) = isJsonl ? ExtractJsonlPreview(text) : ExtractPreview(text);
                    } catch (Exception ex) {
                        // preview extraction 非关键，保留日志可见性
                        _logger?.LogDebug(ex, "TranscriptService: 会话摘要读取失败");
                    }

                    summaries.Add(new TranscriptSummary {
                        SessionId = sessionId,
                        CreatedAt = _fs.GetCreationTimeUtc(dir),
                        LastModifiedAt = _fs.GetLastWriteTimeUtc(transcriptPath),
                        MessageCount = entryCount,
                        LastMessagePreview = preview
                    });
                } catch (Exception ex) {
                    // 跳过不可读目录，保留日志可见性
                    _logger?.LogWarning(ex, "TranscriptService: 跳过不可读会话目录");
                }
            }

            var result = summaries
                .OrderByDescending(s => s.LastModifiedAt)
                .Take(limit)
                .ToList();

            return result;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to list transcripts");
            return Array.Empty<TranscriptSummary>();
        }
    }

    /// <summary>会话目录的转录文件解析 — .jsonl 优先,回退存量 .json(数组格式);都不存在返回 (null, false)。</summary>
    private (string? Path, bool IsJsonl) ResolveTranscriptFile(string sessionDir) {
        var jsonlPath = Path.Combine(sessionDir, "transcript.jsonl");
        if (_fs.FileExists(jsonlPath)) return (jsonlPath, true);
        var jsonPath = Path.Combine(sessionDir, "transcript.json");
        return _fs.FileExists(jsonPath) ? (jsonPath, false) : (null, false);
    }

    /// <summary>
    /// 从 JSONL 文本流式提取条目数与最后一条消息预览(截断至 80 字符) — 只解析尾行,不整块反序列化(ADR 0144)。
    /// </summary>
    /// <param name="text">JSONL 文件全文。</param>
    /// <returns>(条目数, 预览文本);文本为空时返回 (0, null)。</returns>
    private static (int EntryCount, string? Preview) ExtractJsonlPreview(string text) {
        if (string.IsNullOrWhiteSpace(text)) return (0, null);

        var entryCount = 0;
        string? lastLine = null;
        foreach (var line in text.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            entryCount++;
            lastLine = line;
        }

        if (entryCount is 0 || lastLine is null) return (entryCount, null);
        try {
            var entry = RelaxedJsonSerializer.Deserialize(lastLine, TranscriptJsonContext.Default.TranscriptEntry);
            if (entry is null) return (entryCount, null);
            var preview = entry.Content;
            if (preview is not null && preview.Length > 80) {
                preview = preview[..80] + "...";
            }
            return (entryCount, preview);
        } catch (JsonException) {
            return (entryCount, null);
        }
    }

    /// <summary>
    /// 从转录 JSON 中提取条目数与最后一条消息预览(截断至 80 字符)。
    /// </summary>
    /// <param name="json">转录文件 JSON 文本。</param>
    /// <returns>(条目数, 预览文本);JSON 为空或反序列化失败时返回 (0, null)。</returns>
    private static (int EntryCount, string? Preview) ExtractPreview(string json) {
        if (string.IsNullOrWhiteSpace(json)) return (0, null);
        var entries = RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.ListTranscriptEntry);
        if (entries is null) return (0, null);
        var entryCount = entries.Count;
        if (entryCount is 0) return (entryCount, null);
        var preview = entries[^1].Content;
        if (preview is not null && preview.Length > 80) {
            preview = preview[..80] + "...";
        }
        return (entryCount, preview);
    }

    /// <inheritdoc />
    public Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessionDir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(sessionDir)) {
            return Task.FromResult(false);
        }

        try {
            _fs.DeleteDirectory(sessionDir, recursive: true);
            _logger?.LogInformation("Session directory deleted for {SessionId}", sessionId);
            return Task.FromResult(true);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to delete session directory for {SessionId}", sessionId);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> TranscriptExistsAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return Task.FromResult(_fs.FileExists(GetTranscriptPath(sessionId)) || _fs.FileExists(GetLegacyTranscriptPath(sessionId)));
    }

    /// <inheritdoc />
    public async Task SaveCustomTitleAsync(string sessionId, string customTitle, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(customTitle);

        var entry = new TranscriptEntry {
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

    /// <inheritdoc />
    public async Task<string?> GetCustomTitleAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var entries = await LoadTranscriptAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // 从后往前扫描，找到最近的 custom-title 条目
        for (var i = entries.Count - 1; i >= 0; i--) {
            if (entries[i].Type == "custom-title" && !string.IsNullOrEmpty(entries[i].CustomTitle)) {
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
    public async Task InsertContentReplacementAsync(string sessionId, IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord> records, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return;

        var json = JsonSerializer.Serialize(records, ContentReplacementRecordListJsonContext.Default.ListContentReplacementRecord);
        var entry = new TranscriptEntry {
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
    public async Task<IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord>> LoadContentReplacementsAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var entries = await LoadTranscriptAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var records = new List<JoinCode.Abstractions.LLM.Chat.ContentReplacementRecord>();

        foreach (var entry in entries) {
            if (entry.Type == "content-replacement" && !string.IsNullOrEmpty(entry.Content)) {
                try {
                    var deserialized = RelaxedJsonSerializer.Deserialize(entry.Content, ContentReplacementRecordListJsonContext.Default.ListContentReplacementRecord);
                    if (deserialized is not null) {
                        records.AddRange(deserialized);
                    }
                } catch (JsonException ex) {
                    _logger?.LogWarning(ex, "Skipping malformed content-replacement entry in session {SessionId}", sessionId);
                }
            }
        }

        return records;
    }

    private string GetTranscriptPath(string sessionId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId, "transcript.jsonl");
    }

    /// <summary>存量会话路径(JSON 数组格式,ADR 0144 前的会话) — 读取回退与迁移源。</summary>
    private string GetLegacyTranscriptPath(string sessionId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId, "transcript.json");
    }

    private string GetSessionDir(string sessionId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId);
    }

    private string GetMetaPath(string sessionId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        return Path.Combine(_sessionsDirectory, sessionId, "meta.json");
    }

    /// <summary>
    /// 保存会话信息到 {sessionId}/meta.json — 统一入口,替代 SessionData 直写
    /// </summary>
    public async Task SaveSessionInfoAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(info);

        var metaPath = GetMetaPath(sessionId);
        var dir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(dir)) {
            DirectoryHelper.EnsureDirectoryExists(_fs, dir);
        }

        var infoWithId = info with { Id = sessionId };
        if (string.IsNullOrEmpty(infoWithId.ProjectName) && !string.IsNullOrEmpty(infoWithId.ProjectPath)) {
            infoWithId = infoWithId with { ProjectName = Path.GetFileName(infoWithId.ProjectPath) };
        }
        var json = RelaxedJsonSerializer.Serialize(infoWithId, TranscriptJsonContext.Default);
        await _fs.WriteAllTextAsync(metaPath, json, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Session info saved for {SessionId}", sessionId);
    }

    /// <summary>
    /// 加载会话信息 — 不存在或损坏返回 null
    /// </summary>
    public async Task<SessionInfo?> GetSessionInfoAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var metaPath = GetMetaPath(sessionId);
        if (!_fs.FileExists(metaPath)) return null;

        try {
            var json = await _fs.ReadAllTextAsync(metaPath, cancellationToken).ConfigureAwait(false);
            return RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.SessionInfo);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "Failed to read session info for {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// 确保会话实体存在（幂等）— 建目录 + meta.json + 空 transcript.jsonl；已存在不覆盖。
    /// </summary>
    public async Task<bool> EnsureSessionAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(info);

        var created = !_fs.FileExists(GetTranscriptPath(sessionId));
        if (!_fs.FileExists(GetMetaPath(sessionId))) {
            await SaveSessionInfoAsync(sessionId, info, cancellationToken).ConfigureAwait(false);
        }
        if (created) {
            await _writer.WriteEmptyTranscriptAsync(GetTranscriptPath(sessionId), cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Session entity ensured for {SessionId}", sessionId);
        }
        return created;
    }

    /// <summary>
    /// 迁移存量转录到 JSONL(ADR 0144) — 双源,幂等,不删旧文件:
    /// ①旧扁平 .json(sessions 根目录,JSONL 行格式) → {id}/transcript.jsonl
    /// ②子目录 {id}/transcript.json(JSON 数组) → 同目录 {id}/transcript.jsonl
    /// 目标 .jsonl 已存在则跳过(幂等)。
    /// </summary>
    public async Task MigrateLegacyAsync(CancellationToken cancellationToken = default) {
        if (!_fs.DirectoryExists(_sessionsDirectory)) return;

        await MigrateFlatFilesAsync(cancellationToken).ConfigureAwait(false);
        await MigrateSessionJsonArraysAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>迁移源①:旧扁平 .json(JSONL 行) → {id}/transcript.jsonl。</summary>
    private async Task MigrateFlatFilesAsync(CancellationToken cancellationToken) {
        foreach (var file in _fs.EnumerateFiles(_sessionsDirectory, "*.json", SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(sessionId)) continue;

            try {
                var entries = await ReadLegacyJsonlEntriesAsync(file, cancellationToken).ConfigureAwait(false);
                await WriteJsonlEntriesAsync(sessionId, entries, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogWarning(ex, "Failed to migrate legacy transcript {SessionId}", sessionId);
            }
        }
    }

    /// <summary>迁移源②:子目录 transcript.json(JSON 数组) → 同目录 transcript.jsonl。</summary>
    private async Task MigrateSessionJsonArraysAsync(CancellationToken cancellationToken) {
        foreach (var dir in _fs.EnumerateDirectories(_sessionsDirectory, "*", SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(sessionId)) continue;

            try {
                var jsonPath = Path.Combine(dir, "transcript.json");
                if (!_fs.FileExists(jsonPath)) continue;

                var json = await _fs.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) continue;

                var entries = RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.ListTranscriptEntry);
                if (entries is null || entries.Count == 0) continue;

                await WriteJsonlEntriesAsync(sessionId, entries, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Migrated session transcript {SessionId} to JSONL (json→jsonl)", sessionId);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogWarning(ex, "Failed to migrate session transcript {SessionId} to JSONL", sessionId);
            }
        }
    }

    /// <summary>读旧 JSONL 行文件(逐行解析,坏行跳过)。</summary>
    private async Task<List<TranscriptEntry>> ReadLegacyJsonlEntriesAsync(string filePath, CancellationToken cancellationToken) {
        var lines = await _fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var entries = new List<TranscriptEntry>(lines.Length);
        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try {
                var entry = RelaxedJsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
                if (entry is not null) entries.Add(entry);
            } catch (JsonException ex) {
                _logger?.LogWarning(ex, "Skipping malformed line in legacy transcript {FilePath}", filePath);
            }
        }

        return entries;
    }

    /// <summary>写 JSONL 条目到 {id}/transcript.jsonl — 目标已存在跳过(幂等);空条目列表跳过。</summary>
    private async Task WriteJsonlEntriesAsync(string sessionId, List<TranscriptEntry> entries, CancellationToken cancellationToken) {
        var newDir = GetSessionDir(sessionId);
        var newPath = GetTranscriptPath(sessionId);
        if (_fs.FileExists(newPath)) return; // 幂等
        if (entries.Count == 0) return;

        if (!_fs.DirectoryExists(newDir)) {
            DirectoryHelper.EnsureDirectoryExists(_fs, newDir);
        }

        var builder = new StringBuilder();
        foreach (var entry in entries) {
            builder.Append(RelaxedJsonSerializer.Serialize(entry, TranscriptJsonlContext.Default)).Append('\n');
        }

        await _fs.WriteAllTextAsync(newPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Migrated legacy transcript {SessionId} to session directory JSONL", sessionId);
    }

    /// <summary>
    /// 释放转录文件写入器资源。
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        await _writer.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}