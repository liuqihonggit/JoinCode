using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;
using PithosDB.Core.Core;

namespace PithosDB.Core.Storage;

/// <summary>
/// Reads an immutable SSTable file written by <see cref="SSTableWriter"/>.
/// Loads the file into a pinned byte array for zero-copy span access — no
/// MemoryMappedFile is used, so multiple readers can open the same SST file
/// concurrently without Windows file-lock conflicts (cross-process safe).
/// On open, the sparse index and bloom filter are parsed from the buffer;
/// data blocks are sliced directly (no syscall, no ArrayPool).
/// </summary>
public sealed unsafe class SSTableReader : IDisposable
{
    private readonly byte[] _buffer;
    private readonly GCHandle _handle;
    private readonly byte* _ptr;
    private readonly long _length;
    private readonly List<(byte[] firstKey, long offset)> _index;
    private readonly BloomFilter _bloom;
    private readonly long _bloomOffset;
    private readonly IBlockCache? _blockCache;
    private int _disposed;

    /// <summary>Absolute path to the SSTable file.</summary>
    public string Path { get; }

    /// <summary>
    /// Opens the SSTable at <paramref name="path"/>, reads it into a pinned
    /// buffer, and loads its index and bloom filter into memory.
    /// </summary>
    /// <param name="path">Absolute path to the SSTable file.</param>
    /// <param name="blockCache">
    /// Optional shared block cache. When provided, decompressed block bytes are
    /// served from cache on subsequent accesses.
    /// </param>
    /// <remarks>
    /// 工厂方法（ADR 0129）：GCHandle.Alloc 后若 ReadMetadata 失败，在 catch 中 Free handle，
    /// 避免半构造化导致 GCHandle 泄漏（原构造函数此处有泄漏 bug）。
    /// </remarks>
    public static SSTableReader Open(string path, IBlockCache? blockCache = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"SSTable file not found: '{path}'. The file may have been deleted by a concurrent compaction or the manifest is stale. " +
                "Hint: ensure DisableCompaction=true when opening for read-only access, and never share a kvstore directory across concurrent PithosDb instances.");

        long length = new FileInfo(path).Length;
        if (length == 0)
            throw new InvalidDataException(
                $"SSTable file is empty (0 bytes): '{path}'. The file was likely partially written or corrupted during a flush. " +
                "Hint: delete the empty .sst file and rebuild the index, or restore from a backup.");

        byte[] buffer;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            buffer = new byte[length];
            fs.ReadExactly(buffer);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"Cannot read SSTable file '{path}' — it may be locked by another process. " +
                "Hint: SSTableReader opens with FileShare.ReadWrite, so this should not happen under normal operation. " +
                "If it does, check whether an external tool (antivirus, backup, indexer) is holding an exclusive lock on the .sst file.",
                ex);
        }

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var ptr = (byte*)handle.AddrOfPinnedObject();
        try
        {
            var (index, bloom, bloomOffset) = ReadMetadata(ptr, length);
            return new SSTableReader(path, blockCache, buffer, handle, ptr, length, index, bloom, bloomOffset);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            handle.Free();
            throw new InvalidDataException(
                $"SSTable file '{path}' is corrupted — metadata (index/bloom filter) could not be parsed. " +
                "Hint: the file may have been truncated or written by an incompatible version. Delete it and rebuild the index.",
                ex);
        }
    }

    /// <summary>
    /// 私有构造函数 — 仅字段赋值，不执行任何可能抛异常的 IO（ADR 0129）。
    /// </summary>
    private SSTableReader(string path, IBlockCache? blockCache, byte[] buffer, GCHandle handle, byte* ptr, long length,
        List<(byte[] firstKey, long offset)> index, BloomFilter bloom, long bloomOffset)
    {
        Path = path;
        _blockCache = blockCache;
        _buffer = buffer;
        _handle = handle;
        _ptr = ptr;
        _length = length;
        _index = index;
        _bloom = bloom;
        _bloomOffset = bloomOffset;
    }

    /// <summary>The entire file as a zero-copy span. Valid until Dispose.</summary>
    private ReadOnlySpan<byte> Data => new(_ptr, (int)_length);

    /// <summary>
    /// Looks up <paramref name="key"/> in this SSTable. The bloom filter is
    /// consulted first; a definite miss returns <see langword="false"/> without
    /// any block access. Returns <see langword="true"/> for tombstones.
    /// <para>
    /// Thread-safe: the bloom filter and index are pure in-memory reads; the block
    /// is sliced from the mmap span and parsed entirely in memory.
    /// </para>
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        value = null;
        if (!_bloom.MightContain(key)) return false;

        var (blockOffset, blockEnd) = FindBlockBounds(key);
        if (blockOffset < 0) return false;

        int blockLen = (int)(blockEnd - blockOffset);
        var data = Data;

        // Cache hit — block was already decompressed and checksum-verified when first read.
        if (_blockCache is not null && _blockCache.TryGet(Path, blockOffset, out var cached))
            return ParseBlock(cached, key, out value);

        // Cache miss — slice from mmap, verify CRC, decompress, cache, then parse.
        var blockSpan = data.Slice((int)blockOffset, blockLen);
        VerifyChecksum(blockSpan, blockOffset);
        byte[] decompressed = Decompress(blockSpan);

        if (_blockCache is not null)
            _blockCache.Put(Path, blockOffset, decompressed);

        return ParseBlock(decompressed, key, out value);
    }

    private static bool ParseBlock(ReadOnlySpan<byte> block, byte[] key, out byte[]? value)
    {
        value = null;
        int pos = 0;

        int count = BinaryPrimitives.ReadInt32LittleEndian(block[pos..]);
        pos += 4;

        for (int i = 0; i < count; i++)
        {
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(block[pos..]);
            pos += 4;

            ReadOnlySpan<byte> entryKey = block.Slice(pos, keyLen);
            pos += keyLen;

            bool isTombstone = block[pos] != 0;
            pos += 1;

            int cmp = entryKey.SequenceCompareTo(key.AsSpan());

            if (!isTombstone)
            {
                int valLen = BinaryPrimitives.ReadInt32LittleEndian(block[pos..]);
                pos += 4;

                if (cmp == 0) { value = block.Slice(pos, valLen).ToArray(); return true; }
                if (cmp > 0) return false;
                pos += valLen;
            }
            else
            {
                if (cmp == 0) return true;
                if (cmp > 0) return false;
            }
        }
        return false;
    }

    // Block on-disk layout: [compression:1][payloadLen:4][payload:N][CRC32:4]
    // CRC covers [compression][payloadLen bytes][payload].

    private void VerifyChecksum(ReadOnlySpan<byte> block, long blockOffset)
    {
        int dataLen = block.Length - 4;
        uint stored   = BinaryPrimitives.ReadUInt32LittleEndian(block[dataLen..]);
        uint computed = Crc32.HashToUInt32(block[..dataLen]);
        if (computed != stored)
            throw new InvalidDataException(
                $"Block checksum mismatch in '{Path}' at offset {blockOffset}: " +
                $"expected 0x{stored:X8}, computed 0x{computed:X8}.");
    }

    private static byte[] Decompress(ReadOnlySpan<byte> block)
    {
        byte compressionByte = block[0];
        int payloadLen = BinaryPrimitives.ReadInt32LittleEndian(block[1..]);
        ReadOnlySpan<byte> payload = block.Slice(5, payloadLen);

        return compressionByte switch
        {
            SSTableWriter.CompressionNone => payload.ToArray(),
            SSTableWriter.CompressionLz4  => LZ4Pickler.Unpickle(payload),
            _ => throw new InvalidDataException($"Unknown compression type 0x{compressionByte:X2}.")
        };
    }

    /// <summary>
    /// Streams all entries in byte-lexicographic key order, including tombstones.
    /// Used by <see cref="Compaction.LeveledCompactor"/> during compaction.
    /// Reads directly from the mmap pointer — no FileStream, no per-block allocation.
    /// </summary>
    public IEnumerable<KeyValuePair<byte[], byte[]?>> ReadAllEntries()
    {
        if (_index.Count == 0) yield break;

        long pos = _index[0].offset;

        while (pos < _bloomOffset)
        {
            // Block layout: [compression:1][payloadLen:4][payload:N][CRC32:4]
            var data = Data;
            byte compressionByte = data[(int)pos];
            int payloadLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)pos + 1, 4));
            var payload = data.Slice((int)pos + 5, payloadLen);
            pos += 5 + payloadLen + 4; // skip compression + payloadLen + payload + CRC32

            byte[] block = compressionByte switch
            {
                SSTableWriter.CompressionNone => payload.ToArray(),
                SSTableWriter.CompressionLz4  => LZ4Pickler.Unpickle(payload),
                _ => throw new InvalidDataException($"Unknown compression type 0x{compressionByte:X2}.")
            };

            int bpos = 0;
            int count = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(bpos));
            bpos += 4;

            for (int i = 0; i < count; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(bpos));
                bpos += 4;
                var key = block[bpos..(bpos + keyLen)];
                bpos += keyLen;

                bool isTombstone = block[bpos] != 0;
                bpos += 1;

                byte[]? value = null;
                if (!isTombstone)
                {
                    int valLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(bpos));
                    bpos += 4;
                    value = block[bpos..(bpos + valLen)];
                    bpos += valLen;
                }

                yield return new KeyValuePair<byte[], byte[]?>(key, value);
            }
        }
    }

    /// <summary>
    /// Returns the number of SSTable blocks whose key range overlaps
    /// [<paramref name="from"/>, <paramref name="to"/>).
    /// </summary>
    public int ApproximateKeyCount(byte[]? from, byte[]? to)
    {
        var cmp = ByteArrayComparer.Instance;
        int count = 0;
        for (int i = 0; i < _index.Count; i++)
        {
            var blockStart = _index[i].firstKey;
            var blockEnd = i + 1 < _index.Count ? _index[i + 1].firstKey : null;

            bool beforeTo = to  == null || cmp.Compare(blockStart, to)  <= 0;
            bool afterFrom = from == null || blockEnd == null || cmp.Compare(blockEnd, from) > 0;

            if (beforeTo && afterFrom) count++;
        }
        return count;
    }

    /// <summary>
    /// Binary-searches the sparse index for the last block whose first key is
    /// ≤ <paramref name="key"/>.
    /// </summary>
    private (long start, long end) FindBlockBounds(byte[] key)
    {
        int lo = 0, hi = _index.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int cmp = ByteArrayComparer.Instance.Compare(_index[mid].firstKey, key);
            if (cmp <= 0) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (result < 0) return (-1L, -1L);
        long start = _index[result].offset;
        long end = result + 1 < _index.Count ? _index[result + 1].offset : _bloomOffset;
        return (start, end);
    }

    /// <summary>
    /// Reads the 16-byte footer to locate the bloom filter and index sections,
    /// then deserializes both from the mmap span.
    /// </summary>
    private static (List<(byte[] firstKey, long offset)> index, BloomFilter bloom, long bloomOffset) ReadMetadata(byte* ptr, long length)
    {
        var data = new ReadOnlySpan<byte>(ptr, (int)length);

        // Footer layout (last 16 bytes): [bloomOffset (8)] [indexOffset (8)]
        int footerPos = (int)length - 16;
        long bloomOffset = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(footerPos, 8));
        long indexOffset = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(footerPos + 8, 8));

        // Bloom filter
        int pos = (int)bloomOffset;
        int hashCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        pos += 4;
        int bitCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        pos += 4;
        var bits = new bool[bitCount];
        for (int i = 0; i < bitCount; i++)
        {
            bits[i] = data[pos] != 0;
            pos += 1;
        }
        var bloom = new BloomFilter(bits, hashCount);

        // Sparse index
        pos = (int)indexOffset;
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        pos += 4;
        var index = new List<(byte[], long)>(count);
        for (int i = 0; i < count; i++)
        {
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            var key = data.Slice(pos, keyLen).ToArray();
            pos += keyLen;
            long offset = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(pos, 8));
            pos += 8;
            index.Add((key, offset));
        }

        return (index, bloom, bloomOffset);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _handle.Free();
    }
}
