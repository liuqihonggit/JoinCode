using System.Buffers.Binary;
using System.IO.Hashing;
using K4os.Compression.LZ4;
using PithosDB.Core.Core;

namespace PithosDB.Core.Storage;

/// <summary>
/// Writes an immutable, sorted SSTable file from a sequence of key-value entries.
/// <para>
/// File layout:
/// <code>
/// [ data blocks (≤4 KB each) ]  ← each block: [compression:1][payloadLen:4][payload:N][CRC32:4]
/// [ bloom filter section     ]  ← hashCount (int32), bitCount (int32), bits (bool[])
/// [ sparse index section     ]  ← entry count (int32), (keyLen, key, blockOffset)…
/// [ footer (16 bytes)        ]  ← bloomOffset (int64), indexOffset (int64)
/// </code>
/// </para>
/// </summary>
public sealed class SSTableWriter
{
    private const int BlockSize = 4096;

    // Compression byte values stored in block headers.
    internal const byte CompressionNone = 0;
    internal const byte CompressionLz4  = 1;

    /// <summary>
    /// Writes <paramref name="entries"/> to a new SSTable file at <paramref name="path"/>.
    /// Entries must be supplied in byte-lexicographic key order. The file must not
    /// already exist.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="entries">
    /// Sorted key-value pairs to write. A <see langword="null"/> value encodes a tombstone.
    /// </param>
    /// <param name="bloomFalsePositiveRate">
    /// Target false positive rate for the bloom filter. Defaults to 1%.
    /// </param>
    /// <param name="compression">Block compression algorithm. Defaults to <see cref="CompressionKind.None"/>.</param>
    public static void Write(string path, IEnumerable<KeyValuePair<byte[], byte[]?>> entries,
        double bloomFalsePositiveRate = 0.01, CompressionKind compression = CompressionKind.None)
    {
        var entryList = entries.ToList();

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        var bloom = new BloomFilter(Math.Max(1, entryList.Count), bloomFalsePositiveRate);
        foreach (var (key, _) in entryList)
            bloom.Add(key);

        var index = new List<(byte[] firstKey, long offset)>();
        var blockBuffer = new List<KeyValuePair<byte[], byte[]?>>();
        long blockStart = 0;

        foreach (var entry in entryList)
        {
            blockBuffer.Add(entry);
            if (EstimateBlockSize(blockBuffer) >= BlockSize)
                FlushBlock(writer, blockBuffer, index, ref blockStart, compression);
        }

        if (blockBuffer.Count > 0)
            FlushBlock(writer, blockBuffer, index, ref blockStart, compression);

        // Bloom filter section
        long bloomOffset = stream.Position;
        var (bits, hashCount) = bloom.Serialize();
        writer.Write(hashCount);
        writer.Write(bits.Length);
        foreach (var bit in bits)
            writer.Write(bit);

        // Index section
        long indexOffset = stream.Position;
        writer.Write(index.Count);
        foreach (var (firstKey, offset) in index)
        {
            writer.Write(firstKey.Length);
            writer.Write(firstKey);
            writer.Write(offset);
        }

        // Footer: bloomOffset then indexOffset (16 bytes total)
        writer.Write(bloomOffset);
        writer.Write(indexOffset);
    }

    private static void FlushBlock(
        BinaryWriter writer,
        List<KeyValuePair<byte[], byte[]?>> entries,
        List<(byte[] firstKey, long offset)> index,
        ref long blockStart,
        CompressionKind compression)
    {
        index.Add((entries[0].Key, blockStart));

        // Serialize entries into a MemoryStream.
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(entries.Count);
            foreach (var (key, value) in entries)
            {
                bw.Write(key.Length);
                bw.Write(key);
                bool isTombstone = value is null;
                bw.Write(isTombstone);
                if (!isTombstone)
                {
                    bw.Write(value!.Length);
                    bw.Write(value);
                }
            }
        }

        ReadOnlySpan<byte> raw = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        // Optionally compress.
        byte compressionByte;
        byte[] payload;
        if (compression == CompressionKind.Lz4)
        {
            compressionByte = CompressionLz4;
            payload = LZ4Pickler.Pickle(raw);
        }
        else
        {
            compressionByte = CompressionNone;
            payload = raw.ToArray();
        }

        // Block on-disk: [compression:1][payloadLen:4][payload:N][CRC32:4]
        // CRC covers the compression byte + payloadLen bytes + payload.
        Span<byte> payloadLenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payloadLenBytes, payload.Length);

        var crc = new Crc32();
        crc.Append(stackalloc byte[1] { compressionByte });
        crc.Append(payloadLenBytes);
        crc.Append(payload);

        writer.Write(compressionByte);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Write(crc.GetCurrentHashAsUInt32());

        blockStart = writer.BaseStream.Position;
        entries.Clear();
    }

    private static int EstimateBlockSize(List<KeyValuePair<byte[], byte[]?>> entries)
    {
        int size = 0;
        foreach (var (key, value) in entries)
            size += key.Length + (value?.Length ?? 0) + 9;
        return size;
    }
}
