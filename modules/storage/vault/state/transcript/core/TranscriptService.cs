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
        var entries = await _writer.LoadTranscriptAsync(filePath, cancellationToken).ConfigureAwait(false);
        var adopted = await AdoptStreamingPartialAsync(sessionId, entries, cancellationToken).ConfigureAwait(false);
        return OrderByTurnSeq(adopted);
    }

    /// <summary>TASK135 A2 — 并发查询的落盘顺序是**完成顺序**，不等于提问顺序。
    /// 条目带提交时分配的 <c>TurnSeq</c> 时按它稳定排序（同号保持落盘内部顺序）；
    /// 旧记录无号，排在有号之前并保持原相对顺序。<c>OrderBy</c> 本身稳定。</summary>
    /// <param name="entries">加载到的记录。</param>
    /// <returns>按提问顺序排列的记录。</returns>
    private static IReadOnlyList<TranscriptEntry> OrderByTurnSeq(IReadOnlyList<TranscriptEntry> entries) {
        if (entries.Count < 2) return entries;
        if (!entries.Any(static entry => entry.TurnSeq.HasValue)) return entries;

        return entries.OrderBy(static entry => entry.TurnSeq ?? int.MinValue).ToList();
    }

    /// <inheritdoc />
    public async Task AppendStreamingSnapshotAsync(string sessionId, string? queryId, int? turnSeq, string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (string.IsNullOrEmpty(content)) return;

        var filePath = GetStreamingPath(sessionId, queryId);
        var snapshot = new TranscriptEntry {
            Role = "assistant",
            Content = content,
            Timestamp = _clock.GetUtcNow(),
            Type = StreamingEntryType,
            QueryId = queryId,
            TurnSeq = turnSeq,
        }.WithSessionId(sessionId);
        await _writer.AppendEntryAsync(filePath, snapshot, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[STREAM-PERSIST] 会话 {SessionId} 查询 {QueryId} 旁路快照 {Length} 字符",
            sessionId, queryId ?? "(legacy)", content.Length);
    }

    /// <inheritdoc />
    public Task ClearStreamingAsync(string sessionId, string? queryId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = GetStreamingPath(sessionId, queryId);
        try {
            if (_fs.FileExists(filePath)) {
                _fs.DeleteFile(filePath);
                _logger?.LogDebug("[STREAM-PERSIST] 会话 {SessionId} 查询 {QueryId} 旁路已清理（回合正常落盘）",
                    sessionId, queryId ?? "(legacy)");
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // 清理失败不影响会话：残留旁路下次 LoadTranscriptAsync 会被收编，最多多一条残稿
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 会话 {SessionId} 查询 {QueryId} 旁路清理失败",
                sessionId, queryId ?? "(legacy)");
        }

        return Task.CompletedTask;
    }

    /// <summary>收编中断回合的旁路残稿（ADR 0166 / TASK135 A1+A3）— 每个查询一份旁路文件，
    /// 取各自最后一行（累积全文，最全）作为一条 assistant 记录；主文件末条 assistant
    /// 已包含同一段内容时跳过。**收编即消费**：写入主 transcript 后删除该旁路，
    /// 否则每次加载都会重复收编同一条残稿。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="entries">主 transcript 已加载的记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>收编后的记录列表（无旁路或已收编时原样返回）。</returns>
    private async Task<IReadOnlyList<TranscriptEntry>> AdoptStreamingPartialAsync(
        string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken) {
        var sessionDir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(sessionDir)) return entries;

        string[] sidecars;
        try {
            sidecars = _fs.GetFiles(sessionDir, StreamingFilePattern, SearchOption.TopDirectoryOnly);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 会话 {SessionId} 旁路枚举失败，忽略残稿", sessionId);
            return entries;
        }

        if (sidecars.Length == 0) return entries;

        var adopted = new List<TranscriptEntry>(entries.Count + sidecars.Length);
        adopted.AddRange(entries);

        foreach (var streamingPath in sidecars) {
            var partial = await LoadLastSnapshotSafeAsync(streamingPath, cancellationToken).ConfigureAwait(false);
            if (partial is null) {
                await DeleteSidecarSafeAsync(streamingPath, sessionId).ConfigureAwait(false);
                continue;
            }

            // 判重只跟主文件已有内容比（多份旁路彼此独立，不能因为 A 是 B 的前缀就吞掉 B）
            var lastAssistant = entries.LastOrDefault(
                e => string.Equals(e.Role, "assistant", StringComparison.OrdinalIgnoreCase));
            if (lastAssistant is not null && lastAssistant.Content.Contains(partial.Content, StringComparison.Ordinal)) {
                // 完整回复已落盘，旁路只是收尾失败 —— 消费掉，避免每次加载重复比对
                await DeleteSidecarSafeAsync(streamingPath, sessionId).ConfigureAwait(false);
                continue;
            }

            var entry = partial with { SessionId = sessionId, Type = StreamingEntryType };
            adopted.Add(entry);

            // 收编即消费：先落主文件再删旁路。落盘失败保留旁路，下次加载可重试。
            try {
                await _writer.AppendEntryAsync(GetTranscriptPath(sessionId), entry, cancellationToken).ConfigureAwait(false);
                await DeleteSidecarSafeAsync(streamingPath, sessionId).ConfigureAwait(false);
                _logger?.LogWarning("[STREAM-PERSIST] 会话 {SessionId} 收编中断回合残稿 {Path}（{Length} 字符）",
                    sessionId, Path.GetFileName(streamingPath), entry.Content.Length);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogWarning(ex, "[STREAM-PERSIST] 会话 {SessionId} 残稿落盘失败，保留旁路待下次加载",
                    sessionId);
            }
        }

        return adopted;
    }

    /// <summary>读一份旁路的最后一条非空快照；文件损坏/为空返回 null。</summary>
    /// <param name="streamingPath">旁路文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最后一条快照或 null。</returns>
    private async Task<TranscriptEntry?> LoadLastSnapshotSafeAsync(string streamingPath, CancellationToken cancellationToken) {
        try {
            var partials = await _writer.LoadTranscriptAsync(streamingPath, cancellationToken).ConfigureAwait(false);
            return partials.LastOrDefault(e => !string.IsNullOrEmpty(e.Content));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 旁路读取失败，忽略残稿 {Path}", Path.GetFileName(streamingPath));
            return null;
        }
    }

    /// <summary>删除旁路文件；失败只记日志（下次加载再收编一次，不致命）。</summary>
    /// <param name="streamingPath">旁路文件路径。</param>
    /// <param name="sessionId">会话标识（日志用）。</param>
    private Task DeleteSidecarSafeAsync(string streamingPath, string sessionId) {
        try {
            if (_fs.FileExists(streamingPath)) {
                _fs.DeleteFile(streamingPath);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 会话 {SessionId} 旁路删除失败 {Path}",
                sessionId, Path.GetFileName(streamingPath));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit <= 0 || !_fs.DirectoryExists(_sessionsDirectory)) {
            return Array.Empty<TranscriptSummary>();
        }

        try {
            var selected = CollectTranscriptCandidates(cancellationToken)
                .OrderByDescending(candidate => candidate.LastModifiedAt) // 同时间戳保留目录枚举顺序。
                .Take(limit);
            var summaries = new List<TranscriptSummary>();

            foreach (var candidate in selected) {
                cancellationToken.ThrowIfCancellationRequested();
                summaries.Add(await ReadTranscriptSummaryAsync(candidate, cancellationToken).ConfigureAwait(false));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return summaries;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to list transcripts");
            return Array.Empty<TranscriptSummary>();
        }
    }

    /// <summary>用于排序和选取会话的元数据,不包含转录正文。</summary>
    private readonly record struct TranscriptCandidate(string SessionId, string Path, bool IsJsonl, DateTime CreatedAt, DateTime LastModifiedAt);

    /// <summary>按目录枚举顺序收集可读的候选元数据,不打开转录文件。</summary>
    private List<TranscriptCandidate> CollectTranscriptCandidates(CancellationToken cancellationToken) {
        var candidates = new List<TranscriptCandidate>();
        foreach (var dir in _fs.EnumerateDirectories(_sessionsDirectory, "*", SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var sessionId = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(sessionId)) continue;

                var (transcriptPath, isJsonl) = ResolveTranscriptFile(dir);
                if (transcriptPath is null) continue;

                candidates.Add(new TranscriptCandidate(sessionId, transcriptPath, isJsonl,
                    _fs.GetCreationTimeUtc(dir), _fs.GetLastWriteTimeUtc(transcriptPath)));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogWarning(ex, "TranscriptService: 跳过不可读会话目录");
            }
        }

        return candidates;
    }

    /// <summary>为选中的会话读取正文摘要;正文无法读取时仍保留会话元数据。</summary>
    private async Task<TranscriptSummary> ReadTranscriptSummaryAsync(TranscriptCandidate candidate, CancellationToken cancellationToken) {
        var entryCount = 0;
        string? preview = null;
        try {
            await using var stream = _fs.CreateStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, leaveOpen: true);
            (entryCount, preview) = candidate.IsJsonl
                ? await ExtractJsonlPreviewAsync(reader, cancellationToken).ConfigureAwait(false)
                : ExtractPreview(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogDebug(ex, "TranscriptService: 会话摘要读取失败");
        }

        return new TranscriptSummary {
            SessionId = candidate.SessionId,
            CreatedAt = candidate.CreatedAt,
            LastModifiedAt = candidate.LastModifiedAt,
            MessageCount = entryCount,
            LastMessagePreview = preview
        };
    }

    /// <summary>会话目录的转录文件解析 — .jsonl 优先,回退存量 .json(数组格式);都不存在返回 (null, false)。</summary>
    private (string? Path, bool IsJsonl) ResolveTranscriptFile(string sessionDir) {
        var jsonlPath = Path.Combine(sessionDir, "transcript.jsonl");
        if (_fs.FileExists(jsonlPath)) return (jsonlPath, true);
        var jsonPath = Path.Combine(sessionDir, "transcript.json");
        return _fs.FileExists(jsonPath) ? (jsonPath, false) : (null, false);
    }

    /// <summary>
    /// 从 JSONL 正文异步逐行统计非空行,仅解析尾行提取预览(截断至 80 字符,ADR 0144)。
    /// </summary>
    /// <param name="reader">JSONL 文件读取器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>(条目数, 预览文本);文本为空时返回 (0, null)。</returns>
    private static async Task<(int EntryCount, string? Preview)> ExtractJsonlPreviewAsync(StreamReader reader, CancellationToken cancellationToken) {
        var entryCount = 0;
        string? lastLine = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            entryCount++;
            lastLine = line;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (lastLine is null) return (entryCount, null);
        try {
            var entry = RelaxedJsonSerializer.Deserialize(lastLine, TranscriptJsonContext.Default.TranscriptEntry);
            return (entryCount, entry?.Content?.SafeTruncate(80));
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
            preview = preview.SafeTruncate(80);
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

    /// <summary>流式旁路条目类型（ADR 0166 — 读端按普通 assistant 消息消费，Type 只用于观测与识别）。</summary>
    public const string StreamingEntryType = "streaming-partial";

    /// <summary>流式旁路文件名（与会话目录内的 transcript.jsonl 并列）— 无查询身份时的遗留单文件。</summary>
    private const string StreamingFileName = "streaming.jsonl";

    /// <summary>旁路枚举模式 — 同时覆盖遗留 <c>streaming.jsonl</c> 与按查询分文件的 <c>streaming.&lt;queryId&gt;.jsonl</c>。</summary>
    private const string StreamingFilePattern = "streaming*.jsonl";

    /// <summary>流式旁路文件路径（ADR 0166 — 路径知识只住在本服务，不暴露给调用方）。
    /// TASK135 A1：按 queryId 分文件，同一会话的多条并行查询各写各的，不再互删/串台。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="queryId">查询标识；null 落到遗留单旁路文件。</param>
    /// <returns>旁路 JSONL 绝对路径。</returns>
    private string GetStreamingPath(string sessionId, string? queryId) {
        var dir = GetSessionDir(sessionId);
        if (string.IsNullOrEmpty(queryId)) {
            return Path.Combine(dir, StreamingFileName);
        }

        TranscriptFileWriter.ValidateId(queryId, nameof(queryId));
        return Path.Combine(dir, $"streaming.{queryId}.jsonl");
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