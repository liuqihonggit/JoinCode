namespace State;


/// <summary>
/// 共享的 Transcript 文件写入器 — 提取自 TranscriptService 和 AgentTranscriptService
/// 封装 JSONL 格式的追加写入和读取逻辑，消除两个服务间的重复代码
/// 并发保护：Actor 邮箱管道串行化写操作，消除显式锁 — ADR 0115
/// </summary>
internal sealed class TranscriptFileWriter : IAsyncDisposable
{
    private readonly TranscriptFileWriterActor _actor;
    private readonly string _sessionsDirectory;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IPasteStore? _pasteStore;
    private int _disposed;

    private const int MaxPastedContentLength = 1024;

    private bool? _isFileSystemRestricted;

    public TranscriptFileWriter(IFileSystem fs, string sessionsDirectory, ILogger? logger = null, IPasteStore? pasteStore = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory;
        _logger = logger;
        _pasteStore = pasteStore;
        _actor = new TranscriptFileWriterActor(this, logger);
    }

    private bool IsFileSystemRestricted
    {
        get
        {
            if (_isFileSystemRestricted.HasValue) return _isFileSystemRestricted.Value;

            try
            {
                var probePath = Path.Combine(_sessionsDirectory, $".probe_{Guid.NewGuid():N}");
                _fs.WriteAllText(probePath, "p");
                if (_fs.FileExists(probePath)) _fs.DeleteFile(probePath);
                _isFileSystemRestricted = false;
            }
            catch
            {
                _isFileSystemRestricted = true;
                _logger?.LogInformation("[Transcript] 检测到文件系统受限（沙箱环境），会话记录写入将被跳过");
            }

            return _isFileSystemRestricted.Value;
        }
    }

    /// <summary>
    /// 追加单条记录到 JSON 文件(整存整取:读旧数组+追加+写新数组)
    /// </summary>
    public async Task AppendEntryAsync(string filePath, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var reply = new TaskCompletionSource<Unit>();
        await _actor.SendAsync(new AppendEntryCmd(filePath, entry, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendEntryInternalAsync(string filePath, TranscriptEntry entry, CancellationToken cancellationToken)
    {
        var entryToWrite = MaybeOffloadToPasteStore(entry);
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            var entries = await ReadJsonAsync(filePath, cancellationToken).ConfigureAwait(false);
            entries.Add(entryToWrite);
            await WriteJsonAsync(filePath, entries, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            LogRestrictedWarning("[Transcript:WRITE:71]", filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError("[Transcript:WRITE:75] 追加单条记录失败: {FilePath} | {Message}", filePath, ex.Message);
        }
    }

    /// <summary>
    /// 追加多条记录到 JSON 文件(整存整取:读旧数组+追加+写新数组)
    /// </summary>
    public async Task AppendEntriesAsync(string filePath, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return;

        _logger?.LogDebug("AppendEntriesAsync: filePath={FilePath}, count={Count}", filePath, entries.Count);

        var reply = new TaskCompletionSource<Unit>();
        await _actor.SendAsync(new AppendEntriesCmd(filePath, entries, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendEntriesInternalAsync(string filePath, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            var existing = await ReadJsonAsync(filePath, cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                existing.Add(MaybeOffloadToPasteStore(entry));
            }
            await WriteJsonAsync(filePath, existing, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("{Count} transcript entries appended to {FilePath}", entries.Count, filePath);
        }
        catch (UnauthorizedAccessException)
        {
            LogRestrictedWarning("[Transcript:WRITE:108]", filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError("[Transcript:WRITE:112] 追加多条记录失败: {FilePath} | {Message}", filePath, ex.Message);
        }
    }

    /// <summary>
    /// 从 JSON 文件加载所有记录(整存整取:一次反序列化为数组)
    /// </summary>
    public async Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
        {
            return Array.Empty<TranscriptEntry>();
        }

        try
        {
            var entries = await ReadJsonAsync(filePath, cancellationToken).ConfigureAwait(false);
            return entries;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load transcript from {FilePath}", filePath);
            return Array.Empty<TranscriptEntry>();
        }
    }

    /// <summary>读 JSON 文件为 List(不存在返回空列表)</summary>
    private async Task<List<TranscriptEntry>> ReadJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!_fs.FileExists(filePath)) return new List<TranscriptEntry>();

        var json = await ReadAllTextWithWriteShareAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return new List<TranscriptEntry>();

        var entries = RelaxedJsonSerializer.Deserialize(json, TranscriptJsonContext.Default.ListTranscriptEntry);
        if (entries is null) return new List<TranscriptEntry>();

        var result = new List<TranscriptEntry>(entries.Count);
        foreach (var e in entries)
        {
            result.Add(ResolveFromPasteStore(e));
        }
        return result;
    }

    /// <summary>写 List 为 JSON 文件(带缩进,人类可读)</summary>
    private async Task WriteJsonAsync(string filePath, List<TranscriptEntry> entries, CancellationToken cancellationToken)
    {
        var json = RelaxedJsonSerializer.Serialize(entries, TranscriptJsonContext.Default);
        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
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
    /// 用 FileShare.ReadWrite 读取所有文本 — 允许并发写入者,避免 ReadAllTextAsync 的 FileShare.Read 阻止写入
    /// </summary>
    private async Task<string> ReadAllTextWithWriteShareAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                await using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
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
            catch (UnauthorizedAccessException)
            {
                LogRestrictedWarning("[Transcript:CREATE:223]", filePath);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Transcript 文件写入 Actor — 串行化写操作，消除显式锁 — ADR 0115
    /// </summary>
    private sealed class TranscriptFileWriterActor : ActorBase<TranscriptFileWriterCommand, Unit>
    {
        private readonly TranscriptFileWriter _owner;
        private readonly ILogger? _logger;

        public TranscriptFileWriterActor(TranscriptFileWriter owner, ILogger? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(TranscriptFileWriterCommand cmd, CancellationToken ct)
        {
            switch (cmd)
            {
                case AppendEntryCmd(var filePath, var entry, var reply):
                    await _owner.AppendEntryInternalAsync(filePath, entry, ct).ConfigureAwait(false);
                    reply.SetResult(Unit.Value);
                    break;
                case AppendEntriesCmd(var filePath, var entries, var reply):
                    await _owner.AppendEntriesInternalAsync(filePath, entries, ct).ConfigureAwait(false);
                    reply.SetResult(Unit.Value);
                    break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "TranscriptFileWriterActor 命令处理异常");
    }

    private void LogRestrictedWarning(string marker, string filePath)
    {
        if (IsFileSystemRestricted)
        {
            _logger?.LogDebug("{Marker} 沙箱环境, 跳过: {FilePath}", marker, filePath);
        }
        else
        {
            _logger?.LogWarning("{Marker} 文件权限不足或沙箱拦截: {FilePath}", marker, filePath);
        }
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
