namespace JoinCode.CodeIndex.Ast;

public sealed class TreeCache : IDisposable
{
    private readonly int _maxEntries;
    private readonly LinkedList<string> _lruList;
    private readonly Dictionary<string, CacheEntry> _entries;
    private readonly Threading.TimeoutLock _lock;
    private int _disposed;

    private static void Log(string message)
    {
        System.Diagnostics.Trace.WriteLine(message);
    }

    public TreeCache(int maxEntries = 1000)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxEntries = maxEntries;
        _lruList = new LinkedList<string>();
        _entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        _lock = new Threading.TimeoutLock("TreeCache", TimeSpan.FromSeconds(30), Log);
    }

    public int Count
    {
        get
        {
            using var scope = _lock.Acquire();
            return _entries.Count;
        }
    }

    public bool TryGet(string filePath, out Tree? tree)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var scope = _lock.Acquire();
        if (_entries.TryGetValue(filePath, out var entry))
        {
            _lruList.Remove(entry.LruNode);
            entry.LruNode = _lruList.AddLast(filePath);
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

        using var scope = _lock.Acquire();
        return _entries.TryGetValue(filePath, out var entry) ? entry.Source : null;
    }

    public void Add(string filePath, Tree tree, string source)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var scope = _lock.Acquire();
        if (_entries.TryGetValue(filePath, out var existing))
        {
            _lruList.Remove(existing.LruNode);
            existing.Tree.Dispose();
            existing.Tree = tree;
            existing.Source = source;
            existing.LruNode = _lruList.AddLast(filePath);
            return;
        }

        while (_entries.Count >= _maxEntries)
        {
            EvictOldest();
        }

        var node = _lruList.AddLast(filePath);
        _entries[filePath] = new CacheEntry(tree, source, node);
    }

    public void Remove(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var scope = _lock.Acquire();
        if (_entries.Remove(filePath, out var entry))
        {
            _lruList.Remove(entry.LruNode);
            entry.Tree.Dispose();
        }
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        using var scope = _lock.Acquire();
        foreach (var entry in _entries.Values)
        {
            entry.Tree.Dispose();
        }

        _entries.Clear();
        _lruList.Clear();
    }

    private void EvictOldest()
    {
        if (_lruList.First is null) return;

        var oldestPath = _lruList.First.Value;
        _lruList.RemoveFirst();

        if (_entries.Remove(oldestPath, out var entry))
        {
            entry.Tree.Dispose();
        }
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        {
            using var scope = _lock.Acquire();
            foreach (var entry in _entries.Values)
            {
                entry.Tree.Dispose();
            }

            _entries.Clear();
            _lruList.Clear();
        }

        _lock.Dispose();
    }

    private sealed class CacheEntry(Tree tree, string source, LinkedListNode<string> lruNode)
    {
        public Tree Tree = tree;
        public string Source = source;
        public LinkedListNode<string> LruNode = lruNode;
    }
}
