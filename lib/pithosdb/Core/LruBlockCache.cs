using System.Diagnostics.CodeAnalysis;

namespace PithosDB.Core.Core;

/// <summary>
/// Thread-safe LRU block cache shared across all <see cref="Storage.SSTableReader"/>
/// instances. Caches raw block bytes keyed by (file path, block offset) so hot blocks
/// are served from memory rather than re-read from disk on every lookup.
/// </summary>
public sealed class LruBlockCache : IBlockCache
{
    private sealed class Entry
    {
        public required string Path;
        public required long Offset;
        public required byte[] Data;
    }

    private readonly long _maxBytes;
    private long _usedBytes;
    private readonly Dictionary<(string, long), LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly object _sync = new();

    public LruBlockCache(long maxBytes) => _maxBytes = maxBytes;

    public bool TryGet(string path, long offset, [NotNullWhen(true)] out byte[]? data)
    {
        lock (_sync)
        {
            if (_map.TryGetValue((path, offset), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                data = node.Value.Data;
                return true;
            }
        }
        data = null;
        return false;
    }

    public void Put(string path, long offset, byte[] data)
    {
        lock (_sync)
        {
            if (_map.ContainsKey((path, offset))) return;

            while (_usedBytes + data.Length > _maxBytes && _lru.Last is not null)
                Remove(_lru.Last);

            var entry = new Entry { Path = path, Offset = offset, Data = data };
            var node = _lru.AddFirst(entry);
            _map[(path, offset)] = node;
            _usedBytes += data.Length;
        }
    }

    /// <summary>
    /// Evicts all cached blocks belonging to <paramref name="path"/>. Call this
    /// before deleting the underlying SSTable file.
    /// </summary>
    public void EvictFile(string path)
    {
        lock (_sync)
        {
            var node = _lru.First;
            while (node is not null)
            {
                var next = node.Next;
                if (node.Value.Path == path) Remove(node);
                node = next;
            }
        }
    }

    /// <inheritdoc/>
    public long CurrentSizeBytes { get { lock (_sync) return _usedBytes; } }

    private void Remove(LinkedListNode<Entry> node)
    {
        _map.Remove((node.Value.Path, node.Value.Offset));
        _usedBytes -= node.Value.Data.Length;
        _lru.Remove(node);
    }
}
