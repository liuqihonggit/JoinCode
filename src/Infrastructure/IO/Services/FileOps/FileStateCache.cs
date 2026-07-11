using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Interfaces;

namespace IO;

/// <summary>
/// LRU-based file state cache that tracks file reads for write-before-read validation.
/// Mirrors TS FileStateCache with normalized path keys and size limits.
/// </summary>
[Register]
public sealed partial class FileStateCache : IFileStateCache
{
    private int _maxEntries;
    private long _maxSizeBytes;
    private LinkedList<string> _lruOrder = new();
    private Dictionary<string, LinkedListNode<string>> _keyNodes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileReadState> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _currentSizeBytes;

    private const int DefaultMaxEntries = 100;
    private const long DefaultMaxSizeBytes = 25 * 1024 * 1024; // 25MB

    public FileStateCache(int maxEntries = DefaultMaxEntries, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        _maxEntries = maxEntries;
        _maxSizeBytes = maxSizeBytes;
    }

    public void RecordRead(string filePath, string content, long timestampMs, int? offset = null, int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = NormalizePath(filePath);
        var entrySize = Math.Max(1, content.Length * sizeof(char));

        // Remove existing entry if present
        if (_cache.TryGetValue(normalizedPath, out var existing))
        {
            _currentSizeBytes -= Math.Max(1, existing.Content.Length * sizeof(char));
            RemoveFromLru(normalizedPath);
        }

        // Evict entries if over size limit
        while (_currentSizeBytes + entrySize > _maxSizeBytes && _lruOrder.Count > 0)
        {
            EvictOldest();
        }

        // Evict entries if over count limit
        while (_cache.Count >= _maxEntries && _lruOrder.Count > 0)
        {
            EvictOldest();
        }

        var state = new FileReadState
        {
            Content = content,
            TimestampMs = timestampMs,
            Offset = offset,
            Limit = limit,
            IsPartialView = offset.HasValue || limit.HasValue
        };

        _cache[normalizedPath] = state;
        _currentSizeBytes += entrySize;
        PromoteInLru(normalizedPath);
    }

    public bool HasBeenRead(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.ContainsKey(normalizedPath);
    }

    public long? GetReadTimestampMs(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state.TimestampMs : null;
    }

    public string? GetReadContent(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state.Content : null;
    }

    public FileReadState? GetReadState(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state : null;
    }

    public void Invalidate(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_cache.TryGetValue(normalizedPath, out var existing))
        {
            _currentSizeBytes -= Math.Max(1, existing.Content.Length * sizeof(char));
            _cache.Remove(normalizedPath);
            RemoveFromLru(normalizedPath);
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _keyNodes.Clear();
        _lruOrder.Clear();
        _currentSizeBytes = 0;
    }

    public IFileStateCache Clone()
    {
        var cloned = new FileStateCache(_maxEntries, _maxSizeBytes);
        cloned._cache = _cache.ToDictionary(k => k.Key, v => new FileReadState
        {
            Content = v.Value.Content,
            TimestampMs = v.Value.TimestampMs,
            Offset = v.Value.Offset,
            Limit = v.Value.Limit,
            IsPartialView = v.Value.IsPartialView
        }, StringComparer.OrdinalIgnoreCase);
        cloned._lruOrder = new LinkedList<string>(_lruOrder);
        // Rebuild _keyNodes with new list nodes
        foreach (var key in _lruOrder)
        {
            var node = cloned._lruOrder.Find(key);
            if (node is not null)
            {
                cloned._keyNodes[key] = node;
            }
        }
        cloned._currentSizeBytes = _currentSizeBytes;
        return cloned;
    }

    private void EvictOldest()
    {
        if (_lruOrder.Last is null) return;

        var oldestKey = _lruOrder.Last.Value;
        _lruOrder.RemoveLast();

        if (_keyNodes.TryGetValue(oldestKey, out var node) && node.List == _lruOrder)
        {
            _lruOrder.Remove(node);
        }
        _keyNodes.Remove(oldestKey);

        if (_cache.TryGetValue(oldestKey, out var existing))
        {
            _currentSizeBytes -= Math.Max(1, existing.Content.Length * sizeof(char));
            _cache.Remove(oldestKey);
        }
    }

    private void PromoteInLru(string key)
    {
        RemoveFromLru(key);
        var node = _lruOrder.AddFirst(key);
        _keyNodes[key] = node;
    }

    private void RemoveFromLru(string key)
    {
        if (_keyNodes.TryGetValue(key, out var node))
        {
            if (node.List is not null)
            {
                node.List.Remove(node);
            }
            _keyNodes.Remove(key);
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
