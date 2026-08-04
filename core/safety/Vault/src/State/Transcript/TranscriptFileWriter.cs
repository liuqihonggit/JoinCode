namespace State;

/// <summary>
/// 共享的 Transcript 文件写入器 — 提取自 TranscriptService 和 AgentTranscriptService
/// 封装 JSONL 格式的追加写入和读取逻辑，消除两个服务间的重复代码
/// 跨进程并发保护：Named Mutex（Global\jcc-transcript-{sessionId}）
/// 同进程并发保护：SemaphoreSlim（_writeLock）
/// </summary>
internal sealed class TranscriptFileWriter
{
    private readonly SemaphoreSlim _writeLock;
    private readonly string _sessionsDirectory;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IPasteStore? _pasteStore;

    private const int MaxPastedContentLength = 1024;
    private const int MutexTimeoutMs = 5000;

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
            await WithCrossProcessMutexAsync(filePath, async () =>
            {
                EnsureDirectoryExists(Path.GetDirectoryName(filePath));
                EnsureFileExists(filePath);
                var line = JsonSerializer.Serialize(entryToWrite, TranscriptJsonContext.Default.TranscriptEntry);
                await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
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

        _logger?.LogDebug("AppendEntriesAsync: filePath={FilePath}, count={Count}", filePath, entries.Count);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WithCrossProcessMutexAsync(filePath, async () =>
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
            }, cancellationToken).ConfigureAwait(false);
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
        // InMemoryFileSystem 的 CreateStream(FileMode.Open) 可能与写入路径不一致
        // 优先尝试 ReadAllLinesAsync（FileShare.Read），失败时再用 ReadWrite 模式
        try
        {
            return await _fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            // FileShare.Read 冲突时，降级为 ReadWrite 模式
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
    /// 跨进程互斥写入 — 用 Named Mutex 保护同一 session 文件的并发追加
    /// Mutex 名称: Global\jcc-transcript-{文件名不含扩展名}
    /// 超时后放弃写入并记录错误，避免死锁
    /// </summary>
    private async Task WithCrossProcessMutexAsync(string filePath, Func<Task> action, CancellationToken cancellationToken)
    {
        var mutexName = GetMutexName(filePath);
        Mutex? mutex = null;
        bool owned = false;

        try
        {
            mutex = new Mutex(initiallyOwned: false, name: mutexName);
            owned = mutex.WaitOne(MutexTimeoutMs);

            if (!owned)
            {
                _logger?.LogWarning("Cross-process mutex timeout for {FilePath} (mutex={MutexName}), skipping write", filePath, mutexName);
                return;
            }

            await action().ConfigureAwait(false);
        }
        catch (AbandonedMutexException)
        {
            _logger?.LogDebug("Acquired abandoned mutex for {FilePath} (previous owner crashed), proceeding with write", filePath);
            owned = true;
            await action().ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Cannot acquire cross-process mutex for {FilePath} (mutex={MutexName}), writing without lock", filePath, mutexName);
            await action().ConfigureAwait(false);
        }
        finally
        {
            if (mutex is not null)
            {
                try
                {
                    if (owned) mutex.ReleaseMutex();
                    mutex.Dispose();
                }
                catch (ObjectDisposedException) { _logger?.LogDebug("Mutex already disposed for {FilePath}", filePath); }
            }
        }
    }

    /// <summary>
    /// 从文件路径生成 Named Mutex 名称 — Global\jcc-transcript-{sessionId}
    /// </summary>
    private static string GetMutexName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return $@"Global\jcc-transcript-{fileName}";
    }

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
