namespace State;

using Core.Utils;

/// <summary>
/// 共享的 Transcript 文件写入器 — 提取自 TranscriptService 和 AgentTranscriptService
/// 封装 JSONL 格式的追加写入和读取逻辑，消除两个服务间的重复代码
/// 并发保护：AsyncLock（SemaphoreSlim(1,1)），异步友好，无线程亲和问题
/// </summary>
internal sealed class TranscriptFileWriter : IDisposable
{
    private readonly AsyncLock _writeLock;
    private readonly string _sessionsDirectory;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IPasteStore? _pasteStore;

    private const int MaxPastedContentLength = 1024;

    public TranscriptFileWriter(IFileSystem fs, string sessionsDirectory, ILogger? logger = null, IPasteStore? pasteStore = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory;
        _logger = logger;
        _pasteStore = pasteStore;
        _writeLock = new AsyncLock();
    }

    /// <summary>
    /// 追加单条记录到 JSONL 文件
    /// </summary>
    public async Task AppendEntryAsync(string filePath, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entryToWrite = MaybeOffloadToPasteStore(entry);

        using var guard = await _writeLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            EnsureFileExists(filePath);
            var line = JsonSerializer.Serialize(entryToWrite, TranscriptJsonContext.Default.TranscriptEntry);
            await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entry to {FilePath}", filePath);
        }
    }

    /// <summary>
    /// 追加多条记录到 JSONL 文件
    /// </summary>
    public async Task AppendEntriesAsync(string filePath, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return;

        _logger?.LogDebug("AppendEntriesAsync: filePath={FilePath}, count={Count}", filePath, entries.Count);

        using var guard = await _writeLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            EnsureFileExists(filePath);

            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                var entryToWrite = MaybeOffloadToPasteStore(entry);
                var line = JsonSerializer.Serialize(entryToWrite, TranscriptJsonContext.Default.TranscriptEntry);
                sb.AppendLine(line);
            }

            await _fs.AppendAllTextAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("{Count} transcript entries appended to {FilePath}", entries.Count, filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entries to {FilePath}", filePath);
        }
    }

    /// <summary>
    /// 从 JSONL 文件加载所有记录
    /// </summary>
    public async Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
        {
            return Array.Empty<TranscriptEntry>();
        }

        try
        {
            var lines = await ReadAllLinesWithWriteShareAsync(filePath, cancellationToken).ConfigureAwait(false);
            var entries = new List<TranscriptEntry>(lines.Length);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
                    if (entry is not null)
                    {
                        entries.Add(ResolveFromPasteStore(entry));
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "Skipping malformed transcript line in {FilePath}", filePath);
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load transcript from {FilePath}", filePath);
            return Array.Empty<TranscriptEntry>();
        }
    }

    /// <summary>
    /// 验证 ID 只包含合法字符
    /// </summary>
    public static void ValidateId(string id, string paramName)
    {
        foreach (var c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                throw new ArgumentException($"ID contains invalid character: '{c}'", paramName);
            }
        }
    }

    private void EnsureDirectoryExists(string? directory)
    {
        if (!string.IsNullOrEmpty(directory))
        {
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);
        }
    }

    /// <summary>
    /// 用 FileShare.ReadWrite 读取所有行 — 允许并发写入者，避免 ReadAllLinesAsync 的 FileShare.Read 阻止写入
    /// </summary>
    private async Task<string[]> ReadAllLinesWithWriteShareAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                await using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    lines.Add(line);
                }
                return lines.ToArray();
            }
            catch (FileNotFoundException)
            {
                return [];
            }
        }
    }

    private void EnsureFileExists(string filePath)
    {
        if (!_fs.FileExists(filePath))
        {
            try
            {
                _fs.CreateStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite).Dispose();
            }
            catch (IOException ex) when (_fs.FileExists(filePath))
            {
                _logger?.LogDebug(ex, "Transcript file already exists (created by another process): {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogDebug(ex, "Cannot create transcript file {FilePath}, will retry on append", filePath);
            }
        }
    }

    public void Dispose() => _writeLock.Dispose();

    /// <summary>
    /// 序列化前：大文本(>1024字符)存到 paste-cache，Content 置空，设 ContentHash — 对齐 TS addToPromptHistory
    /// </summary>
    private TranscriptEntry MaybeOffloadToPasteStore(TranscriptEntry entry)
    {
        if (_pasteStore is null || string.IsNullOrEmpty(entry.Content) || entry.Content.Length <= MaxPastedContentLength)
        {
            return entry;
        }

        try
        {
            var hash = _pasteStore.HashPastedText(entry.Content);
            _pasteStore.StorePastedText(hash, entry.Content);
            return entry with { Content = string.Empty, ContentHash = hash };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "粘贴内容卸载到 paste-cache 失败，将内联存储");
            return entry;
        }
    }

    /// <summary>
    /// 反序列化后：如果有 ContentHash 引用，从 paste-cache 还原 Content — 对齐 TS resolveStoredPastedContent
    /// </summary>
    private TranscriptEntry ResolveFromPasteStore(TranscriptEntry entry)
    {
        if (_pasteStore is null || string.IsNullOrEmpty(entry.ContentHash))
        {
            return entry;
        }

        try
        {
            var content = _pasteStore.RetrievePastedText(entry.ContentHash);
            if (content is not null)
            {
                return entry with { Content = content, ContentHash = null };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "从 paste-cache 还原内容失败: {Hash}", entry.ContentHash);
        }

        return entry;
    }
}
