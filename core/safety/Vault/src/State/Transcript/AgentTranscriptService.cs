namespace State;

[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentTranscriptService))]
public sealed partial class AgentTranscriptService : JoinCode.Abstractions.Interfaces.IAgentTranscriptService, IDisposable
{
    private readonly string _sessionsDirectory;
    [Inject] private readonly ILogger<AgentTranscriptService>? _logger;
    private readonly TranscriptFileWriter _writer;
    private readonly SemaphoreSlim _metaLock;
    private readonly IFileSystem _fs;

    public AgentTranscriptService(IFileSystem fs, string? sessionsDirectory = null, ILogger<AgentTranscriptService>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
        _writer = new TranscriptFileWriter(_fs, _sessionsDirectory, logger);
        _metaLock = new SemaphoreSlim(1, 1);
    }

    public async Task AppendEntryAsync(string sessionId, string agentId, TranscriptEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        var entryWithMeta = entry.WithAgentMeta(sessionId, agentId);
        await _writer.AppendEntryAsync(filePath, entryWithMeta, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendEntriesAsync(string sessionId, string agentId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0) return;

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        var entriesWithMeta = entries.Select(e => e.WithAgentMeta(sessionId, agentId)).ToList();
        await _writer.AppendEntriesAsync(filePath, entriesWithMeta, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var filePath = GetAgentTranscriptPath(sessionId, agentId);
        return await _writer.LoadTranscriptAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMetadataAsync(string sessionId, JoinCode.Abstractions.Interfaces.AgentMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(metadata);

        await _metaLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAgentDirectoryExists(sessionId);
            var filePath = GetAgentMetadataPath(sessionId, metadata.AgentId);
            var json = JsonSerializer.Serialize(metadata, AgentMetadataJsonContext.Default.AgentMetadata);
            await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to save agent metadata for {AgentId}", metadata.AgentId);
        }
        finally
        {
            _metaLock.Release();
        }
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentMetadata?> LoadMetadataAsync(string sessionId, string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var filePath = GetAgentMetadataPath(sessionId, agentId);
        if (!_fs.FileExists(filePath))
        {
            return null;
        }

        try
        {
            return await _fs.ReadAndDeserializeAsync(filePath, AgentMetadataJsonContext.Default.AgentMetadata, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load agent metadata for {AgentId}", agentId);
            return null;
        }
    }

    public async Task<IReadOnlyList<JoinCode.Abstractions.Interfaces.AgentMetadata>> ListMetadataAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var dir = Path.Combine(_sessionsDirectory, sessionId, "subagents");
        if (!_fs.DirectoryExists(dir))
        {
            return [];
        }

        try
        {
            var metaFiles = _fs.GetFiles(dir, "*.meta.json", SearchOption.TopDirectoryOnly);
            var results = new List<JoinCode.Abstractions.Interfaces.AgentMetadata>(metaFiles.Length);

            foreach (var filePath in metaFiles)
            {
                try
                {
                    var metadata = await _fs.ReadAndDeserializeAsync(filePath, AgentMetadataJsonContext.Default.AgentMetadata, cancellationToken).ConfigureAwait(false);
                    if (metadata is not null)
                    {
                        results.Add(metadata);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "Skipping malformed metadata file: {FilePath}", filePath);
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to list agent metadata for session {SessionId}", sessionId);
            return [];
        }
    }

    private string GetAgentTranscriptPath(string sessionId, string agentId)
    {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        TranscriptFileWriter.ValidateId(agentId, nameof(agentId));
        return Path.Combine(_sessionsDirectory, sessionId, "subagents", $"agent-{agentId}.jsonl");
    }

    private string GetAgentMetadataPath(string sessionId, string agentId)
    {
        TranscriptFileWriter.ValidateId(sessionId, nameof(sessionId));
        TranscriptFileWriter.ValidateId(agentId, nameof(agentId));
        return Path.Combine(_sessionsDirectory, sessionId, "subagents", $"agent-{agentId}.meta.json");
    }

    private void EnsureAgentDirectoryExists(string sessionId)
    {
        var dir = Path.Combine(_sessionsDirectory, sessionId, "subagents");
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _metaLock.Dispose();
    }
}

