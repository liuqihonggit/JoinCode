namespace Infrastructure.HotSpot;

/// <summary>
/// 意图收集器实现 — ConcurrentDictionary + per-key lock 线程安全
/// 按 filePath 索引意图，支持多 Worker 并发上报和清理
/// </summary>
[Register(typeof(IIntentCollector), ServiceLifetime.Singleton)]
public sealed class IntentCollector : IIntentCollector
{
    private readonly ConcurrentDictionary<string, List<FileModifyIntent>> _intentsByFile = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();
    private readonly IClockService _clock;

    public IntentCollector(IClockService? clock = null)
    {
        _clock = clock ?? SystemClockService.Instance;
    }

    public Task ReportAsync(string workerId, IReadOnlyList<FileModifyIntent> intents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(intents);

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = NormalizePath(intent.FilePath);
            lock (GetLock(key))
            {
                _intentsByFile.GetOrAdd(key, _ => []).Add(intent);
            }
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<FileModifyIntent> GetIntents(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var key = NormalizePath(filePath);
        lock (GetLock(key))
        {
            if (_intentsByFile.TryGetValue(key, out var list))
                return [.. list];
        }
        return [];
    }

    public IReadOnlyList<FileModifyIntent> GetAllIntents()
    {
        var all = new List<FileModifyIntent>();
        foreach (var kvp in _intentsByFile)
        {
            lock (GetLock(kvp.Key))
            {
                all.AddRange(kvp.Value);
            }
        }
        return all;
    }

    public Task RemoveWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var kvp in _intentsByFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (GetLock(kvp.Key))
            {
                kvp.Value.RemoveAll(x => x.WorkerId == workerId);
            }
        }

        return Task.CompletedTask;
    }

    private object GetLock(string filePath) => _locks.GetOrAdd(filePath, _ => new object());

    private static string NormalizePath(string filePath) => filePath.Replace('\\', '/');
}
