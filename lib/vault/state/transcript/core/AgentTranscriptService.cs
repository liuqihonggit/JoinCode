namespace State;

/// <summary>
/// Agent 转录服务 — 管理子 Agent 的转录条目与元数据的追加式读写
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentTranscriptService), ServiceLifetime.Singleton)]
public sealed partial class AgentTranscriptService : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentTranscriptService, IDisposable {
    private readonly string _sessionsDirectory;
    private readonly ILogger<AgentTranscriptService>? _logger;
    private readonly TranscriptFileWriter _writer;
    private readonly AgentTranscriptActor _actor;
    private readonly IFileSystem _fs;
    private bool _disposed;

    /// <summary>
    /// 构造 Agent 转录服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="sessionsDirectory">会话目录（可选，默认使用应用数据目录下的 sessions 子目录）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="pasteStore">粘贴存储（可选，用于大内容外置存储）</param>
    public AgentTranscriptService(IFileSystem fs, string? sessionsDirectory = null, ILogger<AgentTranscriptService>? logger = null, IPasteStore? pasteStore = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                AppDataConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
        _writer = new TranscriptFileWriter(_fs, _sessionsDirectory, logger, pasteStore);
        _actor = new AgentTranscriptActor(this, logger);
    }

    /// <inheritdoc/>
    public async Task AppendEntryAsync(string sessionId, string agentId, TranscriptEntry entry, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        var entryWithMeta = entry.WithAgentMeta(sessionId, agentId);
        await _writer.AppendEntryAsync(filePath, entryWithMeta, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AppendEntriesAsync(string sessionId, string agentId, IEnumerable<TranscriptEntry> entries, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entries);

        if (!entries.Any()) return;

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        var entriesWithMeta = entries.Select(e => e.WithAgentMeta(sessionId, agentId)).ToList();
        await _writer.AppendEntriesAsync(filePath, entriesWithMeta, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TranscriptEntry>> LoadTranscriptAsync(string sessionId, string agentId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        return await _writer.LoadTranscriptAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveMetadataAsync(string sessionId, JoinCode.Abstractions.Interfaces.AgentMetadata metadata, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(metadata);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new SaveMetadataCmd(sessionId, metadata, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存元数据内部实现 — Actor Consumer 线程独占执行，无需锁
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="metadata">Agent 元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task SaveMetadataInternalAsync(
        string sessionId, JoinCode.Abstractions.Interfaces.AgentMetadata metadata, CancellationToken cancellationToken) {
        try {
            EnsureAgentDirectoryExists(sessionId, metadata.AgentId);
            var filePath = GetAgentMetadataPath(sessionId, metadata.AgentId);
            var json = RelaxedJsonSerializer.Serialize(metadata, AgentMetadataJsonContext.Default);
            await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to save agent metadata for {AgentId}", metadata.AgentId);
        }
    }

    /// <inheritdoc/>
    public async Task<JoinCode.Abstractions.Interfaces.AgentMetadata?> LoadMetadataAsync(string sessionId, string agentId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var filePath = GetAgentMetadataPath(sessionId, agentId);
        if (!_fs.FileExists(filePath)) {
            return null;
        }

        try {
            return await _fs.ReadAndDeserializeAsync(filePath, AgentMetadataJsonContext.Default.AgentMetadata, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to load agent metadata for {AgentId}", agentId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<JoinCode.Abstractions.Interfaces.AgentMetadata>> ListMetadataAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try {
            var subagentsDir = Path.Combine(_sessionsDirectory, sessionId, "subagents");
            if (!_fs.DirectoryExists(subagentsDir)) {
                return [];
            }

            var agentDirs = _fs.EnumerateDirectories(subagentsDir, "*", SearchOption.TopDirectoryOnly);
            var results = new List<JoinCode.Abstractions.Interfaces.AgentMetadata>();

            foreach (var agentDir in agentDirs) {
                cancellationToken.ThrowIfCancellationRequested();
                var metaPath = Path.Combine(agentDir, "meta.json");
                if (!_fs.FileExists(metaPath)) continue;

                try {
                    var metadata = await _fs.ReadAndDeserializeAsync(metaPath, AgentMetadataJsonContext.Default.AgentMetadata, cancellationToken).ConfigureAwait(false);
                    if (metadata is not null) {
                        results.Add(metadata);
                    }
                } catch (JsonException ex) {
                    _logger?.LogWarning(ex, "Skipping malformed metadata file: {FilePath}", metaPath);
                }
            }

            return results;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Failed to list agent metadata for session {SessionId}", sessionId);
            return [];
        }
    }

    private string GetAgentTranscriptPath(string sessionId, string agentId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        TranscriptFileWriter.ValidateId(agentId, nameof(agentId));
        return Path.Combine(_sessionsDirectory, sessionId, "subagents", agentId, "transcript.json");
    }

    private string GetAgentMetadataPath(string sessionId, string agentId) {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        TranscriptFileWriter.ValidateId(agentId, nameof(agentId));
        return Path.Combine(_sessionsDirectory, sessionId, "subagents", agentId, "meta.json");
    }

    private void EnsureAgentDirectoryExists(string sessionId, string agentId) {
        var dir = Path.Combine(_sessionsDirectory, sessionId, "subagents", agentId);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        await _actor.DisposeAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Agent 转录服务 Actor — 串行化元数据写操作，消除显式锁
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class AgentTranscriptActor : ActorBase<AgentTranscriptCommand, Unit> {
        private readonly AgentTranscriptService _owner;
        private readonly ILogger<AgentTranscriptService>? _logger;

        public AgentTranscriptActor(AgentTranscriptService owner, ILogger<AgentTranscriptService>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 AgentTranscriptService 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(AgentTranscriptCommand cmd, CancellationToken ct) {
            switch (cmd) {
                case SaveMetadataCmd(var sessionId, var metadata, var reply):
                await _owner.SaveMetadataInternalAsync(sessionId, metadata, ct).ConfigureAwait(false);
                reply.SetResult();
                break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "AgentTranscriptActor 命令处理异常");
    }
}