
namespace Core.Memdir;

[Register(typeof(ISessionTagService), ServiceLifetime.Singleton)]
public sealed partial class SessionTagService : ServiceEntity, ISessionTagService, IDisposable
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly ILogger<SessionTagService>? _logger;
    private readonly AsyncLock _saveLock = new();
    private readonly CancellationTokenSource _disposeCts = new();

    public SessionTagService(IOptions<MemdirOptions> options, IFileOperationService fileOperationService, ILogger<SessionTagService>? logger = null)
    {
        var storagePath = options?.Value?.StoragePath ?? throw new ArgumentNullException(nameof(options));
        _storagePath = Path.Combine(storagePath, "session_tags.json");
        _fileOperationService = fileOperationService;
        _logger = logger;
    }

    public bool AddTag(string sessionId, string tag)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(tag);

        var tags = _tags.GetOrAdd(sessionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (tags)
        {
            var added = tags.Add(tag);
            if (added)
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogSessionAddTag), sessionId, tag);
                FireAndForgetSave();
            }
            return added;
        }
    }

    public bool RemoveTag(string sessionId, string tag)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(tag);

        if (!_tags.TryGetValue(sessionId, out var tags)) return false;

        lock (tags)
        {
            var removed = tags.Remove(tag);
            if (removed)
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogSessionRemoveTag), sessionId, tag);
                if (tags.Count == 0)
                {
                    _tags.TryRemove(sessionId, out _);
                }
                FireAndForgetSave();
            }
            return removed;
        }
    }

    public IEnumerable<string> GetTags(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (!_tags.TryGetValue(sessionId, out var tags)) return [];

        lock (tags)
        {
            return tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetAllTags()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _tags)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.Count > 0)
                {
                    result[kvp.Key] = kvp.Value.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }
        return result;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.Content)) return;

            var data = RelaxedJsonSerializer.Deserialize(result.Content, SessionTagJsonContext.Default.SessionTagData);
            if (data?.Entries == null) return;

            foreach (var kvp in data.Entries)
            {
                var tags = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
                _tags[kvp.Key] = tags;
            }

            _logger?.LogDebug(L.T(StringKey.VaultLogLoadedSessionTags), data.Entries.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogLoadSessionTagsFailed));
        }
    }

    private void FireAndForgetSave()
    {
        _ = SaveAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        using var guard = await _saveLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");
        try
        {
            var data = new SessionTagData
            {
                Entries = _tags.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList())
            };

            var json = RelaxedJsonSerializer.Serialize(data, SessionTagJsonContext.Default);
            await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnDispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _saveLock.Dispose();
    }
}

internal sealed class SessionTagData
{
    public Dictionary<string, List<string>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

