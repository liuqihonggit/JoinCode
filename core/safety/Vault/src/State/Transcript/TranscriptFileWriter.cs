namespace State;

/// <summary>
/// 共享的 Transcript 文件写入器 — 提取自 TranscriptService 和 AgentTranscriptService
/// 封装 JSONL 格式的追加写入和读取逻辑，消除两个服务间的重复代码
/// </summary>
internal sealed class TranscriptFileWriter
{
    private readonly SemaphoreSlim _writeLock;
    private readonly string _sessionsDirectory;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IPasteStore? _pasteStore;

    /// <summary>
    /// 大文本阈值 — 对齐 TS MAX_PASTED_CONTENT_LENGTH
    /// 超过此长度的 Content 不内联存储，而是存到 paste-cache/ 目录
    /// </summary>
    private const int MaxPastedContentLength = 1024;

    public TranscriptFileWriter(IFileSystem fs, string sessionsDirectory, ILogger? logger = null, IPasteStore? pasteStore = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory;
        _logger = logger;
        _pasteStore = pasteStore;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 追加单条记录到 JSONL 文件
    /// </summary>
    public async Task AppendEntryAsync(string filePath, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entryToWrite = MaybeOffloadToPasteStore(entry);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));

            var maxRetries = 3;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await using var stream = OpenForAppend(filePath);
                    await using var writer = new StreamWriter(stream);
                    var line = JsonSerializer.Serialize(entryToWrite, TranscriptJsonContext.Default.TranscriptEntry);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
                {
                    _logger?.LogWarning(ex, "Transient UnauthorizedAccessException writing {FilePath}, retry {Attempt}/{Max}", filePath, attempt + 1, maxRetries);
                    await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    _logger?.LogWarning(ex, "IOException writing {FilePath}, retry {Attempt}/{Max}", filePath, attempt + 1, maxRetries);
                    await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entry to {FilePath}", filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 追加多条记录到 JSONL 文件
    /// </summary>
    public async Task AppendEntriesAsync(string filePath, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return;

        _logger?.LogWarning("[DIAG] AppendEntriesAsync called: filePath={FilePath}, count={Count}, _fs={FsType}", filePath, entries.Count, _fs.GetType().FullName);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));

            var maxRetries = 3;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await using var stream = OpenForAppend(filePath);
                    await using var writer = new StreamWriter(stream);

                    foreach (var entry in entries)
                    {
                        var entryToWrite = MaybeOffloadToPasteStore(entry);
                        var line = JsonSerializer.Serialize(entryToWrite, TranscriptJsonContext.Default.TranscriptEntry);
                        await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                    _logger?.LogDebug("{Count} transcript entries appended to {FilePath}", entries.Count, filePath);
                    break;
                }
                catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
                {
                    _logger?.LogWarning(ex, "Transient UnauthorizedAccessException writing {FilePath}, retry {Attempt}/{Max}", filePath, attempt + 1, maxRetries);
                    await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    _logger?.LogWarning(ex, "IOException writing {FilePath}, retry {Attempt}/{Max}", filePath, attempt + 1, maxRetries);
                    await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entries to {FilePath}", filePath);
        }
        finally
        {
            _writeLock.Release();
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
            var lines = await _fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
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
    /// 以追加模式打开文件 — 逐步降级 FileShare 策略
    /// 1. 先用 FileShare.ReadWrite（允许并发读取者）
    /// 2. 如果被拒绝，降级为 FileShare.Read（只允许读取）
    /// 3. 如果仍被拒绝，降级为 FileShare.None（独占，因 _writeLock 保护同进程并发）
    /// </summary>
    private Stream OpenForAppend(string filePath)
    {
        _logger?.LogWarning("[DIAG] OpenForAppend: _fs type={FsType}, filePath={FilePath}", _fs.GetType().FullName, filePath);

        var shareLevels = new[] { FileShare.ReadWrite, FileShare.Read, FileShare.None };
        Exception? lastEx = null;
        foreach (var share in shareLevels)
        {
            try
            {
                var stream = _fs.CreateStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, share);
                stream.Seek(0, SeekOrigin.End);
                return stream;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastEx = ex;
                _logger?.LogWarning("[DIAG] OpenForAppend FileShare={Share} failed for {FilePath}, HResult=0x{HResult:X8}, Message={Message}", share, filePath, ex.HResult, ex.Message);
                continue;
            }
            catch (IOException ex)
            {
                lastEx = ex;
                _logger?.LogWarning("[DIAG] OpenForAppend FileShare={Share} IOException for {FilePath}, HResult=0x{HResult:X8}, Message={Message}", share, filePath, ex.HResult, ex.Message);
                continue;
            }
        }

        throw new IOException($"Unable to open transcript file for append after trying all share modes: {filePath}", lastEx);
    }

    /// <summary>
    /// 确保文件存在 — FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException
    /// </summary>
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
                // TOCTOU 竞态：其他进程在我们检查和创建之间已创建了文件 — 安全忽略
                _logger?.LogDebug(ex, "Transcript file already exists (created by another process): {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 文件可能被其他进程锁定，或目录权限不足 — 降级为日志
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
