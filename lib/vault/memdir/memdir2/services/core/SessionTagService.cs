
namespace Core.Memdir;

/// <summary>
/// 会话标签服务 — 管理会话的标签集合,支持增删查与持久化
/// </summary>
[Register(typeof(ISessionTagService), ServiceLifetime.Singleton)]
public sealed partial class SessionTagService : ServiceEntity, ISessionTagService, IDisposable {
    private ImmutableDictionary<string, ImmutableHashSet<string>> _tags = ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly ILogger<SessionTagService>? _logger;
    private readonly SessionTagActor _actor;

    /// <summary>
    /// 创建会话标签服务实例
    /// </summary>
    /// <param name="options">Memdir 配置选项,提供存储路径</param>
    /// <param name="fileOperationService">文件操作服务,用于持久化标签</param>
    /// <param name="logger">可选的日志记录器</param>
    public SessionTagService(IOptions<MemdirOptions> options, IFileOperationService fileOperationService, ILogger<SessionTagService>? logger = null) {
        var storagePath = options?.Value?.StoragePath ?? throw new ArgumentNullException(nameof(options));
        _storagePath = Path.Combine(storagePath, "session_tags.json");
        _fileOperationService = fileOperationService;
        _logger = logger;
        _actor = new SessionTagActor(this, logger);
    }

    /// <inheritdoc />
    public bool AddTag(string sessionId, string tag) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(tag);

        var added = false;
        ImmutableInterlocked.Update(ref _tags, d => {
            var existing = d.GetValueOrDefault(sessionId) ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
            if (existing.Contains(tag)) return d;
            added = true;
            return d.SetItem(sessionId, existing.Add(tag));
        });

        if (added) {
            _logger?.LogDebug(L.T(StringKey.VaultLogSessionAddTag), sessionId, tag);
            FireAndForgetSave();
        }
        return added;
    }

    /// <inheritdoc />
    public bool RemoveTag(string sessionId, string tag) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(tag);

        var removed = false;
        ImmutableInterlocked.Update(ref _tags, d => {
            if (!d.TryGetValue(sessionId, out var existing)) return d;
            if (!existing.Contains(tag)) return d;
            removed = true;
            var updated = existing.Remove(tag);
            return updated.Count == 0 ? d.Remove(sessionId) : d.SetItem(sessionId, updated);
        });

        if (removed) {
            _logger?.LogDebug(L.T(StringKey.VaultLogSessionRemoveTag), sessionId, tag);
            FireAndForgetSave();
        }
        return removed;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetTags(string sessionId) {
        ArgumentNullException.ThrowIfNull(sessionId);

        var tags = Volatile.Read(ref _tags).GetValueOrDefault(sessionId);
        return tags is null ? [] : tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetAllTags() {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in Volatile.Read(ref _tags)) {
            if (kvp.Value.Count > 0) {
                result[kvp.Key] = kvp.Value.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default) {
        try {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.Content)) return;

            var data = RelaxedJsonSerializer.Deserialize(result.Content, SessionTagJsonContext.Default.SessionTagData);
            if (data?.Entries == null) return;

            foreach (var kvp in data.Entries) {
                var tags = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, kvp.Value);
                ImmutableInterlocked.Update(ref _tags, d => d.SetItem(kvp.Key, tags));
            }

            _logger?.LogDebug(L.T(StringKey.VaultLogLoadedSessionTags), data.Entries.Count);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogLoadSessionTagsFailed));
        }
    }

    private void FireAndForgetSave() {
        _actor.TrySend(new SessionTagSaveCmd());
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken) {
        try {
            var data = new SessionTagData {
                Entries = _tags.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList())
            };

            var json = RelaxedJsonSerializer.Serialize(data, SessionTagJsonContext.Default);
            await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 异步释放资源 — await Actor 完全退出，禁止 fire-and-forget（JCC9200）
    /// </summary>
    public override async ValueTask DisposeAsync() {
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 会话标签 Actor — 串行化文件写操作，消除显式锁
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class SessionTagActor : ActorBase<SessionTagCommand, Unit> {
        private readonly SessionTagService _owner;
        private readonly ILogger<SessionTagService>? _logger;

        /// <summary>构造会话标签 Actor。</summary>
        /// <param name="owner">所属的会话标签服务。</param>
        /// <param name="logger">可选的日志记录器。</param>
        public SessionTagActor(SessionTagService owner, ILogger<SessionTagService>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        protected override async ValueTask HandleAsync(SessionTagCommand cmd, CancellationToken ct) {
            switch (cmd) {
                case SessionTagSaveCmd:
                await _owner.SaveInternalAsync(ct).ConfigureAwait(false);
                break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "SessionTagActor 命令处理异常");
    }
}

internal sealed class SessionTagData {
    /// <summary>获取或设置会话标签条目字典。</summary>
    public Dictionary<string, List<string>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}