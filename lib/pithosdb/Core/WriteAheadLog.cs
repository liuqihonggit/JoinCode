using System.Buffers.Binary;
using System.IO.Hashing;

namespace PithosDB.Core.Core;

/// <summary>Entry type discriminator stored as the first byte of each WAL record.</summary>
public enum WalEntryType : byte { Put = 1, Delete = 2, Batch = 3 }

/// <summary>
/// Append-only binary log that provides crash durability for unflushed MemTable writes.
/// Every <see cref="AppendPut"/>, <see cref="AppendDelete"/>, and <see cref="AppendBatch"/>
/// is written before the corresponding MemTable mutation is applied. Flush behavior is
/// controlled by <see cref="WalSyncMode"/>. On startup, <see cref="Replay"/> re-applies
/// all records to rebuild the MemTable. The log file is deleted and recreated after each
/// successful MemTable flush to SSTable.
/// <para>
/// Record formats (all multi-byte integers are little-endian):
/// <list type="bullet">
/// <item>Put:    <c>[Put(1)][keyLen(4)][key][valLen(4)][value][CRC32(4)]</c></item>
/// <item>Delete: <c>[Delete(1)][keyLen(4)][key][CRC32(4)]</c></item>
/// <item>Batch:  <c>[Batch(1)][payloadLen(4)][payload][CRC32(4)]</c> — CRC covers payload only</item>
/// </list>
/// For Put and Delete the CRC covers all bytes from the type discriminator through the last
/// data byte. A truncated record or CRC mismatch causes <see cref="Replay"/> to stop at
/// that point; earlier records in the same log are unaffected.
/// </para>
/// </summary>
public sealed class WriteAheadLog : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly WalSyncMode _syncMode;
    private readonly Timer? _syncTimer;

    /// <summary>
    /// Opens or creates the WAL file at <paramref name="path"/> in append mode.
    /// </summary>
    /// <param name="path">Path to the WAL file.</param>
    /// <param name="syncMode">
    /// Controls when data is flushed to the OS write buffer.
    /// <see cref="WalSyncMode.Full"/> (default) flushes after every record.
    /// <see cref="WalSyncMode.Periodic"/> flushes on a background timer.
    /// <see cref="WalSyncMode.None"/> never flushes explicitly.
    /// </param>
    /// <param name="syncIntervalMs">
    /// Background flush interval in milliseconds. Only used when
    /// <paramref name="syncMode"/> is <see cref="WalSyncMode.Periodic"/>.
    /// </param>
    public WriteAheadLog(string path, WalSyncMode syncMode = WalSyncMode.Full, int syncIntervalMs = 200)
    {
        _syncMode = syncMode;
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_stream);

        if (syncMode == WalSyncMode.Periodic)
            _syncTimer = new Timer(_ => { try { _stream.Flush(); } catch { } },
                null, syncIntervalMs, syncIntervalMs);
    }

    /// <summary>
    /// Appends a CRC32-guarded PUT record and flushes to disk.
    /// Format: <c>[Put(1)][keyLen(4)][key][valLen(4)][value][CRC32(4)]</c>
    /// </summary>
    public void AppendPut(byte[] key, byte[] value)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((byte)WalEntryType.Put);
            bw.Write(key.Length);
            bw.Write(key);
            bw.Write(value.Length);
            bw.Write(value);
        }
        WriteRecordWithCrc(ms);
    }

    /// <summary>
    /// Appends a CRC32-guarded DELETE record and flushes to disk.
    /// Format: <c>[Delete(1)][keyLen(4)][key][CRC32(4)]</c>
    /// </summary>
    public void AppendDelete(byte[] key)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((byte)WalEntryType.Delete);
            bw.Write(key.Length);
            bw.Write(key);
        }
        WriteRecordWithCrc(ms);
    }

    /// <summary>
    /// Appends a BATCH record containing all <paramref name="operations"/> in a single
    /// CRC32-guarded write, then flushes to disk.
    /// Format: <c>[Batch(1)][payloadLen(4)][payload][CRC32(4)]</c> — CRC covers payload only.
    /// On replay, a missing or mismatched CRC causes the record (and all subsequent records)
    /// to be silently dropped, preserving atomicity.
    /// </summary>
    internal void AppendBatch(List<(WalEntryType Type, byte[] Key, byte[]? Value)> operations)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (type, key, value) in operations)
            {
                bw.Write((byte)type);
                bw.Write(key.Length);
                bw.Write(key);
                if (type == WalEntryType.Put)
                {
                    bw.Write(value!.Length);
                    bw.Write(value);
                }
            }
        }

        var payload = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        _writer.Write((byte)WalEntryType.Batch);
        _writer.Write((int)ms.Length);
        _writer.Write(payload);
        _writer.Write(Crc32.HashToUInt32(payload));
        if (_syncMode == WalSyncMode.Full) _stream.Flush();
    }

    private void WriteRecordWithCrc(MemoryStream ms)
    {
        var record = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        _writer.Write(record);
        _writer.Write(Crc32.HashToUInt32(record));
        if (_syncMode == WalSyncMode.Full) _stream.Flush();
    }

    /// <summary>
    /// Replays all records from the WAL file at <paramref name="path"/>.
    /// Returns an empty sequence if the file does not exist.
    /// <para>
    /// Partial trailing records and CRC mismatches cause replay to stop silently at that
    /// point — earlier records are unaffected. This guarantees that a crash mid-write
    /// never produces a partially-applied record on recovery.
    /// </para>
    /// </summary>
    public static IEnumerable<(WalEntryType type, byte[] key, byte[]? value)> Replay(string path)
    {
        if (!File.Exists(path)) yield break;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);

        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 1) yield break;
            byte typeByte = reader.ReadByte();
            var type = (WalEntryType)typeByte;

            if (type == WalEntryType.Batch)
            {
                // CRC covers payload bytes only (excludes type byte and length field).
                if (stream.Length - stream.Position < 4) yield break;
                int payloadLen = reader.ReadInt32();
                if (stream.Length - stream.Position < payloadLen + 4) yield break;

                var payload = reader.ReadBytes(payloadLen);
                uint storedCrc = reader.ReadUInt32();
                if (Crc32.HashToUInt32(payload) != storedCrc) yield break;

                using var ms = new MemoryStream(payload);
                using var batchReader = new BinaryReader(ms);
                while (ms.Position < ms.Length)
                {
                    var opType = (WalEntryType)batchReader.ReadByte();
                    int bKeyLen = batchReader.ReadInt32();
                    var bKey = batchReader.ReadBytes(bKeyLen);
                    if (opType == WalEntryType.Put)
                    {
                        int bValLen = batchReader.ReadInt32();
                        yield return (opType, bKey, batchReader.ReadBytes(bValLen));
                    }
                    else
                    {
                        yield return (opType, bKey, null);
                    }
                }
            }
            else
            {
                // Put / Delete: CRC covers type byte through last data byte.
                // Accumulate bytes incrementally as we read each field.
                var crc = new Crc32();
                Span<byte> typeBuf = stackalloc byte[1] { typeByte };
                crc.Append(typeBuf);

                if (stream.Length - stream.Position < 4) yield break;
                var keyLenBytes = reader.ReadBytes(4);
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(keyLenBytes);
                crc.Append(keyLenBytes);

                if (stream.Length - stream.Position < keyLen) yield break;
                var key = reader.ReadBytes(keyLen);
                crc.Append(key);

                byte[]? value = null;
                if (type == WalEntryType.Put)
                {
                    if (stream.Length - stream.Position < 4) yield break;
                    var valLenBytes = reader.ReadBytes(4);
                    int valLen = BinaryPrimitives.ReadInt32LittleEndian(valLenBytes);
                    crc.Append(valLenBytes);

                    if (stream.Length - stream.Position < valLen) yield break;
                    value = reader.ReadBytes(valLen);
                    crc.Append(value);
                }

                if (stream.Length - stream.Position < 4) yield break;
                uint storedCrc = reader.ReadUInt32();
                if (crc.GetCurrentHashAsUInt32() != storedCrc) yield break;

                yield return (type, key, value);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Stop the periodic timer before flushing so it doesn't race with Dispose.
        _syncTimer?.Dispose();
        // Final flush ensures any buffered writes reach the OS before we close.
        _stream.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }
}
