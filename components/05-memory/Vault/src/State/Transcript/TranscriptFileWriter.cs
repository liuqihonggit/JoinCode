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

    public TranscriptFileWriter(IFileSystem fs, string sessionsDirectory, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory;
        _logger = logger;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 追加单条记录到 JSONL 文件
    /// </summary>
    public async Task AppendEntryAsync(string filePath, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            EnsureFileExists(filePath);
            var line = JsonSerializer.Serialize(entry, TranscriptJsonContext.Default.TranscriptEntry);
            await _fs.AppendAllTextAsync(filePath, line + '\n', cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entry to {FilePath}", filePath);
            // 不重新抛出 — transcript 写入失败不应中断主流程
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 追加多条记录到 JSONL 文件
    /// </summary>
    /// <remarks>
    /// ⚠️ 不使用 FileMode.Append — 在 .NET 5+ 中文件不存在时抛 FileNotFoundException (与 .NET Framework 不同)
    /// 即使 EnsureFileExists 试图先创建文件,也可能因竞态/权限问题失败被吞,导致后续 Append 抛错
    /// 使用 FileMode.OpenOrCreate 避免 FileNotFoundException,文件不存在时自动创建
    /// </remarks>
    public async Task AppendEntriesAsync(string filePath, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return;

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            EnsureFileExists(filePath);

            // FileMode.OpenOrCreate: 文件不存在时创建,存在时打开
            // 不用 FileMode.Append 是因为 .NET 5+ 中文件不存在时抛 FileNotFoundException (与 .NET Framework 不同)
            // 即使 EnsureFileExists 试图先创建文件,也可能因竞态/权限问题失败被吞,导致后续 Append 抛错
            await using var stream = _fs.CreateStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            // OpenOrCreate 打开后指针在文件开头,需要 seek 到末尾才能追加
            stream.Seek(0, SeekOrigin.End);
            await using var writer = new StreamWriter(stream);

            foreach (var entry in entries)
            {
                var line = JsonSerializer.Serialize(entry, TranscriptJsonContext.Default.TranscriptEntry);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("{Count} transcript entries appended to {FilePath}", entries.Count, filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to append transcript entries to {FilePath}", filePath);
            // 不重新抛出 — transcript 写入失败不应中断主流程
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
                        entries.Add(entry);
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
    /// 确保文件存在 — FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException
    /// </summary>
    private void EnsureFileExists(string filePath)
    {
        if (!_fs.FileExists(filePath))
        {
            try
            {
                _fs.CreateStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
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
}
