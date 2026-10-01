using System.Buffers;
using System.Buffers.Binary;
using System.IO.Hashing;
using K4os.Compression.LZ4;
using Microsoft.Win32.SafeHandles;
using PithosDB.Core.Core;

namespace PithosDB.Core.Storage;

/// <summary>
/// Reads an immutable SSTable file written by <see cref="SSTableWriter"/>.
/// On open, the sparse index and bloom filter are loaded into memory; data
/// blocks remain on disk and are read on demand. Each instance holds an open
/// file handle — dispose when done.
/// </summary>
public sealed class SSTableReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly List<(byte[] firstKey, long offset)> _index;
    private readonly BloomFilter _bloom;
    private readonly long _bloomOffset;
    private readonly IBlockCache? _blockCache;

    /// <summary>Absolute path to the SSTable file.</summary>
    public string Path { get; }

    /// <summary>
    /// Opens the SSTable at <paramref name="path"/> and loads its index and
    /// bloom filter into memory.
    /// </summary>
    /// <param name="path">Absolute path to the SSTable file.</param>
    /// <param name="blockCache">
    /// Optional shared block cache. When provided, block reads are served from
    /// cache on subsequent accesses to the same block. The cache stores decompressed
    /// block bytes so decompression only happens once per block per cache warm-up.
    /// </param>
    public SSTableReader(string path, IBlockCache? blockCache = null)
    {
        Path = path;
        _blockCache = blockCache;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream);
        (_index, _bloom, _bloomOffset) = ReadMetadata();
    }

    /// <summary>
    /// Looks up <paramref name="key"/> in this SSTable. The bloom filter is
    /// consulted first; a definite miss returns <see langword="false"/> without
    /// any block I/O. Returns <see langword="true"/> for tombstones (with a
    /// <see langword="null"/> <paramref name="value"/>), allowing callers to
    /// distinguish "found a tombstone" from "not present".
    /// <para>
    /// Thread-safe: the bloom filter and index are pure in-memory reads; the block
    /// is fetched with a single positional <see cref="RandomAccess.Read"/> call
    /// (does not advance <c>_stream.Position</c>) and parsed entirely in memory,
    /// so concurrent callers never share mutable state.
    /// </para>
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">
    /// The stored value on success, <see langword="null"/> for a tombstone,
    /// or <see langword="null"/> when the method returns <see langword="false"/>.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the block's CRC32 checksum does not match the stored checksum,
    /// indicating data corruption.
    /// </exception>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        value = null;
        if (!_bloom.MightContain(key)) return false;

        var (blockOffset, blockEnd) = FindBlockBounds(key);
        if (blockOffset < 0) return false;

        int blockLen = (int)(blockEnd - blockOffset);

        // Cache hit — block was already decompressed and checksum-verified when first read.
        if (_blockCache is not null && _blockCache.TryGet(Path, blockOffset, out var cached))
            return ParseBlock(cached, key, out value);

        // Cache miss — read raw block, verify CRC, decompress, cache, then parse.
        byte[] buf = ArrayPool<byte>.Shared.Rent(blockLen);
        try
        {
            ReadAt(_stream.SafeFileHandle, buf.AsSpan(0, blockLen), blockOffset);
            VerifyChecksum(buf.AsSpan(0, blockLen), blockOffset);
            byte[] decompressed = Decompress(buf.AsSpan(0, blockLen));

            if (_blockCache is not null)
                _blockCache.Put(Path, blockOffset, decompressed);

            return ParseBlock(decompressed, key, out value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
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

    // Positional read that retries until the buffer is full. RandomAccess.Read
    // does not advance _stream.Position, so concurrent callers share the handle safely.
    private static void ReadAt(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            int n = RandomAccess.Read(handle, buffer, offset);
            if (n == 0) throw new EndOfStreamException();
            buffer = buffer[n..];
            offset += n;
        }
    }

    /// <summary>
    /// Streams all entries in byte-lexicographic key order, including tombstones.
    /// Used by <see cref="Compaction.LeveledCompactor"/> during compaction.
    /// </summary>
    public IEnumerable<KeyValuePair<byte[], byte[]?>> ReadAllEntries()
    {
        if (_index.Count == 0) yield break;

        _stream.Seek(_index[0].offset, SeekOrigin.Begin);

        while (_stream.Position < _bloomOffset)
        {
            // Block layout: [compression:1][payloadLen:4][payload:N][CRC32:4]
            byte compressionByte = _reader.ReadByte();
            int payloadLen = _reader.ReadInt32();
            byte[] payload = _reader.ReadBytes(payloadLen);
            _reader.ReadUInt32(); // consume CRC32 (verified on TryGet path; skip here for compaction speed)

            byte[] block = compressionByte switch
            {
                SSTableWriter.CompressionNone => payload,
                SSTableWriter.CompressionLz4  => LZ4Pickler.Unpickle(payload),
                _ => throw new InvalidDataException($"Unknown compression type 0x{compressionByte:X2}.")
            };

            int pos = 0;
            int count = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(pos));
            pos += 4;

            for (int i = 0; i < count; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(pos));
                pos += 4;
                var key = block[pos..(pos + keyLen)];
                pos += keyLen;

                bool isTombstone = block[pos] != 0;
                pos += 1;

                byte[]? value = null;
                if (!isTombstone)
                {
                    int valLen = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(pos));
                    pos += 4;
                    value = block[pos..(pos + valLen)];
                    pos += valLen;
                }

                yield return new KeyValuePair<byte[], byte[]?>(key, value);
            }
        }
    }

    /// <summary>
    /// Returns the number of SSTable blocks whose key range overlaps
    /// [<paramref name="from"/>, <paramref name="to"/>]. Each block holds
    /// approximately one block's worth of keys; callers should treat this as a
    /// rough order-of-magnitude estimate rather than an exact count. Omit either
    /// bound for an open-ended range.
    /// </summary>
    public int ApproximateKeyCount(byte[]? from, byte[]? to)
    {
        var cmp = ByteArrayComparer.Instance;
        int count = 0;
        for (int i = 0; i < _index.Count; i++)
        {
            var blockStart = _index[i].firstKey;
            // Next block's first key is the exclusive upper bound for block i.
            var blockEnd = i + 1 < _index.Count ? _index[i + 1].firstKey : null;

            // Block overlaps [from, to] when blockStart <= to AND blockEnd > from.
            bool beforeTo = to  == null || cmp.Compare(blockStart, to)  <= 0;
            bool afterFrom = from == null || blockEnd == null || cmp.Compare(blockEnd, from) > 0;

            if (beforeTo && afterFrom) count++;
        }
        return count;
    }

    /// <summary>
    /// Binary-searches the sparse index for the last block whose first key is
    /// ≤ <paramref name="key"/>. Returns <c>(-1, -1)</c> if the key precedes the
    /// first block. The <c>end</c> value is the exclusive byte offset of the block's
    /// last byte — either the next block's start or <c>_bloomOffset</c> for the
    /// last block.
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
    /// then deserializes both into memory.
    /// </summary>
    private (List<(byte[] firstKey, long offset)> index, BloomFilter bloom, long bloomOffset) ReadMetadata()
    {
        // Footer layout (last 16 bytes): [bloomOffset (8)] [indexOffset (8)]
        _stream.Seek(-16, SeekOrigin.End);
        long bloomOffset = _reader.ReadInt64();
        long indexOffset = _reader.ReadInt64();

        _stream.Seek(bloomOffset, SeekOrigin.Begin);
        int hashCount = _reader.ReadInt32();
        int bitCount = _reader.ReadInt32();
        var bits = new bool[bitCount];
        for (int i = 0; i < bitCount; i++)
            bits[i] = _reader.ReadBoolean();
        var bloom = new BloomFilter(bits, hashCount);

        _stream.Seek(indexOffset, SeekOrigin.Begin);
        int count = _reader.ReadInt32();
        var index = new List<(byte[], long)>(count);
        for (int i = 0; i < count; i++)
        {
            var keyLen = _reader.ReadInt32();
            var key = _reader.ReadBytes(keyLen);
            var offset = _reader.ReadInt64();
            index.Add((key, offset));
        }

        return (index, bloom, bloomOffset);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
