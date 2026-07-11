namespace Core.Memdir;

[Register]
public sealed partial class ThinkingStore : IThinkingStore, IDisposable
{
    private readonly ConcurrentDictionary<string, List<ThinkingEntry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<ThinkingStore>? _logger;
    private readonly IClockService _clock;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    public ThinkingStore(IOptions<MemdirOptions> options, IFileOperationService fileOperationService, IFileSystem fs, ILogger<ThinkingStore>? logger = null, IClockService? clock = null)
    {
        _storagePath = options?.Value?.StoragePath ?? throw new ArgumentNullException(nameof(options));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StoreAsync(string sessionId, string content, string? modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId)) throw new ArgumentNullException(nameof(sessionId));
        if (string.IsNullOrEmpty(content)) return Task.CompletedTask;

        var entry = new ThinkingEntry
        {
            SessionId = sessionId,
            Content = content,
            ModelId = modelId,
            Timestamp = _clock.GetUtcNow()
        };

        var entries = _entries.GetOrAdd(sessionId, _ => []);
        lock (entries)
        {
            entries.Add(entry);
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogThinkingStore), sessionId, content.Length);

        _ = SaveAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThinkingEntry>> GetRecentAsync(string sessionId, int count, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(sessionId, out var entries))
        {
            return Task.FromResult<IReadOnlyList<ThinkingEntry>>([]);
        }

        lock (entries)
        {
            var result = entries.Skip(Math.Max(0, entries.Count - count)).ToList();
            return Task.FromResult<IReadOnlyList<ThinkingEntry>>(result);
        }
    }

    public Task<ThinkingEntry?> GetLatestAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(sessionId, out var entries))
        {
            return Task.FromResult<ThinkingEntry?>(null);
        }

        lock (entries)
        {
            return Task.FromResult(entries.Count > 0 ? entries[^1] : null);
        }
    }

    public Task ClearAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(sessionId, out _);
        _ = SaveAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        return Task.CompletedTask;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var filePath = GetFilePath();
        if (!_fileOperationService.FileExists(filePath))
        {
            return;
        }

        try
        {
            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.Content)) return;
            var json = result.Content;

            var data = JsonSerializer.Deserialize(json, ThinkingStoreJsonContext.Default.ThinkingStoreData);
            if (data?.Entries == null) return;

            foreach (var kvp in data.Entries)
            {
                _entries[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogThinkingLoadFailed));
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = new ThinkingStoreData();
            foreach (var kvp in _entries)
            {
                data.Entries[kvp.Key] = kvp.Value.ToList();
            }

            var filePath = GetFilePath();
            var dir = Path.GetDirectoryName(filePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, dir);

            var json = JsonSerializer.Serialize(data, ThinkingStoreJsonContext.Default.ThinkingStoreData);
            await _fileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.VaultLogThinkingSaveFailed));
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _saveLock.Dispose();
    }

    private string GetFilePath() => Path.Combine(_storagePath, "thinking_store.json");
}

internal sealed class ThinkingStoreData
{
    public Dictionary<string, List<ThinkingEntry>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(ThinkingStoreData))]
[JsonSerializable(typeof(ThinkingEntry))]
[JsonSerializable(typeof(List<ThinkingEntry>))]
[JsonSerializable(typeof(Dictionary<string, List<ThinkingEntry>>))]
internal sealed partial class ThinkingStoreJsonContext : JsonSerializerContext;
