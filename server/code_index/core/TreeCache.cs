namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// TreeSitter 解析树缓存 — 无锁结构（ConcurrentDictionary），消除 AsyncLock 嵌套死锁。
/// 淘汰策略：达到 maxEntries 后不再缓存新条目（调用方 dispose 新 tree）。
/// 线程安全：ConcurrentDictionary 保证并发读写安全；同实例并发访问由上层 _parseLock 串行化。
/// </summary>
public sealed class TreeCache : IDisposable
{
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries;
    private readonly ILogger? _logger;
    private int _disposed;

    private void Log(string message)
    {
        _logger?.LogDebug(message);
    }

    public TreeCache(int maxEntries = 1000, ILogger? logger = null)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxEntries = maxEntries;
        _entries = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public int Count => _entries.Count;

    public bool TryGet(string filePath, out Tree? tree)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_entries.TryGetValue(filePath, out var entry))
        {
            tree = entry.Tree;
            return true;
        }

        tree = null;
        return false;
    }

    public string? GetSource(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        return _entries.TryGetValue(filePath, out var entry) ? entry.Source : null;
    }

    /// <summary>
    /// 添加或更新缓存条目。达到 maxEntries 后新文件不缓存（dispose tree 避免泄漏）。
    /// 更新已存在条目时原子替换并 dispose 旧 Tree。
    /// </summary>
    public void Add(string filePath, Tree tree, string source)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var newEntry = new CacheEntry(tree, source);
        while (true)
        {
            if (_entries.TryGetValue(filePath, out var existing))
            {
                if (_entries.TryUpdate(filePath, newEntry, existing))
                {
                    existing.Tree.Dispose();
                    return;
                }
                continue;
            }

            if (_entries.Count >= _maxEntries)
            {
                tree.Dispose();
                return;
            }

            if (_entries.TryAdd(filePath, newEntry))
                return;
        }
    }

    public void Remove(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_entries.TryRemove(filePath, out var entry))
        {
            entry.Tree.Dispose();
        }
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        foreach (var entry in _entries.Values)
        {
            entry.Tree.Dispose();
        }

        _entries.Clear();
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        foreach (var entry in _entries.Values)
        {
            entry.Tree.Dispose();
        }

        _entries.Clear();
    }

    private sealed record CacheEntry(Tree Tree, string Source);
}
