using System.Diagnostics.CodeAnalysis;

namespace PithosDB.Core.Core;

/// <summary>
/// Thread-safe S3-FIFO block cache.
/// <para>
/// S3-FIFO (Simple, Scalable SLFU with three FIFOs) uses two FIFO queues:
/// <list type="bullet">
/// <item><b>Small (S)</b> — ~10% of capacity. All new blocks enter here.</item>
/// <item><b>Main (M)</b>  — ~90% of capacity. Blocks promoted from S after a second access.</item>
/// </list>
/// A ghost set (G) records keys recently evicted from S. If a block in G is
/// requested again it skips S and enters M directly, bypassing the probation period.
/// </para>
/// <para>
/// On eviction from S: if the block's <c>freq</c> bit is 1, it is promoted to M
/// (freq reset to 0); otherwise it is evicted and its key added to G.
/// On eviction from M: if <c>freq</c> is 1, the block is re-inserted at the head
/// of M (one-chance); otherwise it is evicted.
/// A cache hit sets <c>freq = 1</c> without moving the block, making cache hits
/// O(1) with no structural mutation — only a single field write under the lock.
/// </para>
/// </summary>
public sealed class S3FifoBlockCache : IBlockCache
{
    private sealed class Entry
    {
        public required string Path;
        public required long   Offset;
        public required byte[] Data;
        public int  Freq;   // 0 or 1
        public bool InMain; // false → in Small
    }

    private readonly long _smallMax;
    private readonly long _mainMax;
    private long _smallUsed;
    private long _mainUsed;

    // FIFO queues: new entries at head (AddFirst), eviction candidates at tail (Last).
    private readonly LinkedList<Entry> _small = new();
    private readonly LinkedList<Entry> _main  = new();

    // Ghost: evicted-from-Small keys with no stored data.
    private readonly Queue<(string, long)>   _ghostQueue = new();
    private readonly HashSet<(string, long)> _ghostSet   = new();
    private readonly int _ghostMax;

    private readonly Dictionary<(string, long), LinkedListNode<Entry>> _map = new();
    private readonly object _sync = new();

    public S3FifoBlockCache(long maxBytes)
    {
        _smallMax = Math.Max(1, maxBytes / 10);
        _mainMax  = maxBytes - _smallMax;
        // Ghost holds roughly as many entries as Small can fit at the average block size.
        _ghostMax = (int)Math.Max(16, _smallMax / 4096);
    }

    public bool TryGet(string path, long offset, [NotNullWhen(true)] out byte[]? data)
    {
        lock (_sync)
        {
            if (_map.TryGetValue((path, offset), out var node))
            {
                node.Value.Freq = 1; // set without moving — O(1), no structural change
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
            var key = (path, offset);
            if (_map.ContainsKey(key)) return;

            if (_ghostSet.Contains(key))
            {
                // Re-admission: proven frequency — go straight to Main.
                MakeRoomInMain(data.Length);
                InsertIntoMain(key, data);
            }
            else
            {
                // First visit: enter Small on probation.
                MakeRoomInSmall(data.Length);
                InsertIntoSmall(key, data);
            }
        }
    }

    public void EvictFile(string path)
    {
        lock (_sync)
        {
            RemoveFromQueue(_small, ref _smallUsed, path);
            RemoveFromQueue(_main,  ref _mainUsed,  path);
        }
    }

    /// <inheritdoc/>
    public long CurrentSizeBytes { get { lock (_sync) return _smallUsed + _mainUsed; } }

    // ── Private helpers ────────────────────────────────────────────────────

    private void InsertIntoSmall((string, long) key, byte[] data)
    {
        var entry = new Entry { Path = key.Item1, Offset = key.Item2, Data = data, Freq = 0, InMain = false };
        _map[key] = _small.AddFirst(entry);
        _smallUsed += data.Length;
    }

    private void InsertIntoMain((string, long) key, byte[] data)
    {
        var entry = new Entry { Path = key.Item1, Offset = key.Item2, Data = data, Freq = 0, InMain = true };
        _map[key] = _main.AddFirst(entry);
        _mainUsed += data.Length;
    }

    private void MakeRoomInSmall(long needed)
    {
        while (_smallUsed + needed > _smallMax && _small.Last is not null)
            EvictFromSmall();
    }

    private void MakeRoomInMain(long needed)
    {
        while (_mainUsed + needed > _mainMax && _main.Last is not null)
            EvictFromMain();
    }

    private void EvictFromSmall()
    {
        var node = _small.Last;
        if (node is null) return;
        _small.RemoveLast();
        _smallUsed -= node.Value.Data.Length;

        var key = (node.Value.Path, node.Value.Offset);
        if (node.Value.Freq > 0)
        {
            // Promote: accessed at least once → goes to Main with freq reset.
            node.Value.Freq   = 0;
            node.Value.InMain = true;
            MakeRoomInMain(node.Value.Data.Length);
            // If item is larger than entire Main (edge case), just drop it.
            if (_mainUsed + node.Value.Data.Length <= _mainMax)
            {
                _map[key] = _main.AddFirst(node.Value);
                _mainUsed += node.Value.Data.Length;
            }
            else
            {
                _map.Remove(key);
            }
        }
        else
        {
            // Cold — evict and record in ghost so its next access goes to Main.
            _map.Remove(key);
            AddToGhost(key);
        }
    }

    private void EvictFromMain()
    {
        var node = _main.Last;
        if (node is null) return;
        _main.RemoveLast();
        _mainUsed -= node.Value.Data.Length;

        var key = (node.Value.Path, node.Value.Offset);
        if (node.Value.Freq > 0)
        {
            // One-chance: re-insert at head with freq cleared.
            node.Value.Freq = 0;
            _map[key] = _main.AddFirst(node.Value);
            _mainUsed += node.Value.Data.Length;
        }
        else
        {
            _map.Remove(key);
        }
    }

    private void AddToGhost((string, long) key)
    {
        if (_ghostSet.Contains(key)) return;
        if (_ghostQueue.Count >= _ghostMax)
            _ghostSet.Remove(_ghostQueue.Dequeue());
        _ghostQueue.Enqueue(key);
        _ghostSet.Add(key);
    }

    private void RemoveFromQueue(LinkedList<Entry> queue, ref long used, string path)
    {
        var node = queue.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.Path == path)
            {
                _map.Remove((node.Value.Path, node.Value.Offset));
                used -= node.Value.Data.Length;
                queue.Remove(node);
            }
            node = next;
        }
    }
}
