namespace Core.Memdir;

/// <summary>
/// 思考记录存储实现 — 按会话 ID 维护思考条目列表,支持加载、保存、查询最近/最新条目与清空操作。
/// </summary>
[Register(typeof(IThinkingStore), ServiceLifetime.Singleton)]
public sealed partial class ThinkingStore : ServiceEntity, IThinkingStore, IDisposable {
    private readonly ConcurrentDictionary<string, List<ThinkingEntry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly ILogger<ThinkingStore>? _logger;
    private readonly IClockService _clock;
    private readonly ThinkingStoreActor _actor;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    /// <summary>
    /// 构造函数 — 注入存储路径选项、文件操作服务、文件系统、日志与时钟等依赖。
    /// </summary>
    public ThinkingStore(IOptions<MemdirOptions> options, IFileOperationService fileOperationService, IFileSystem fs, ILogger<ThinkingStore>? logger = null, IClockService? clock = null) {
        _storagePath = options?.Value?.StoragePath ?? throw new ArgumentNullException(nameof(options));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _actor = new ThinkingStoreActor(this, logger);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StoreAsync(string sessionId, string content, string? modelId, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(sessionId)) throw new ArgumentNullException(nameof(sessionId));
        if (string.IsNullOrEmpty(content)) return Task.CompletedTask;

        var entry = new ThinkingEntry {
            SessionId = sessionId,
            Content = content,
            ModelId = modelId,
            Timestamp = _clock.GetUtcNow()
        };

        var entries = _entries.GetOrAdd(sessionId, _ => []);
        lock (entries) {
            entries.Add(entry);
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogThinkingStore), sessionId, content.Length);

        _ = SaveAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ThinkingEntry>> GetRecentAsync(string sessionId, int count, CancellationToken cancellationToken = default) {
        if (!_entries.TryGetValue(sessionId, out var entries)) {
            return Task.FromResult<IReadOnlyList<ThinkingEntry>>([]);
        }

        lock (entries) {
            var result = entries.Skip(Math.Max(0, entries.Count - count)).ToList();
            return Task.FromResult<IReadOnlyList<ThinkingEntry>>(result);
        }
    }

    /// <inheritdoc />
    public Task<ThinkingEntry?> GetLatestAsync(string sessionId, CancellationToken cancellationToken = default) {
        if (!_entries.TryGetValue(sessionId, out var entries)) {
            return Task.FromResult<ThinkingEntry?>(null);
        }

        lock (entries) {
            return Task.FromResult(entries.Count > 0 ? entries[^1] : null);
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string sessionId, CancellationToken cancellationToken = default) {
        _entries.TryRemove(sessionId, out _);
        _ = SaveAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        return Task.CompletedTask;
    }

    private async Task LoadAsync(CancellationToken cancellationToken) {
        var filePath = GetFilePath();
        if (!_fileOperationService.FileExists(filePath)) {
            return;
        }

        try {
            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.Content)) return;
            var json = result.Content;

            var data = RelaxedJsonSerializer.Deserialize(json, ThinkingStoreJsonContext.Default.ThinkingStoreData);
            if (data?.Entries == null) return;

            foreach (var kvp in data.Entries) {
                _entries[kvp.Key] = kvp.Value;
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogThinkingLoadFailed));
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken) {
        var reply = new TaskCompletionSource<Unit>();
        await _actor.SendAsync(new ThinkingSaveCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken) {
        try {
            var data = new ThinkingStoreData();
            foreach (var kvp in _entries) {
                data.Entries[kvp.Key] = kvp.Value.ToList();
            }

            var filePath = GetFilePath();
            var dir = Path.GetDirectoryName(filePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, dir);

            var json = RelaxedJsonSerializer.Serialize(data, ThinkingStoreJsonContext.Default);
            await _fileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogThinkingSaveFailed));
        }
    }

    /// <summary>
    /// 释放取消令牌等同步资源。
    /// </summary>
    public override void Dispose() {
        if (_disposed) return;
        _disposed = true;

        _disposeCts.CancelAndDisposeSafe(_logger);
        base.Dispose();
    }

    /// <summary>异步释放资源 — await Actor 完全退出，禁止 fire-and-forget（JCC9200）</summary>
    public override async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;

        _disposeCts.CancelAndDisposeSafe(_logger);
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private string GetFilePath() => Path.Combine(_storagePath, "thinking_store.json");

    /// <summary>
    /// 思考记录存储 Actor — 串行化文件写操作，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class ThinkingStoreActor : ActorBase<ThinkingStoreCommand, Unit> {
        private readonly ThinkingStore _owner;
        private readonly ILogger<ThinkingStore>? _logger;

        public ThinkingStoreActor(ThinkingStore owner, ILogger<ThinkingStore>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 ThinkingStore 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(ThinkingStoreCommand cmd, CancellationToken ct) {
            switch (cmd) {
                case ThinkingSaveCmd(var reply):
                await _owner.SaveInternalAsync(ct).ConfigureAwait(false);
                reply.SetResult(Unit.Value);
                break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "ThinkingStoreActor 命令处理异常");
    }
}

internal sealed class ThinkingStoreData {
    public Dictionary<string, List<ThinkingEntry>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}