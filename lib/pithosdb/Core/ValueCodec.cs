using System.Buffers.Binary;

namespace PithosDB.Core.Core;

/// <summary>
/// Encodes and decodes values stored on disk when TTL is enabled.
/// Format:
///   Plain  → [0x00][user-value...]
///   With TTL → [0x01][expiry-unix-ms: 8 bytes LE][user-value...]
/// </summary>
internal static class ValueCodec
{
    internal const byte FlagPlain = 0x00;
    internal const byte FlagTtl   = 0x01;

    /// <summary>Wraps <paramref name="value"/> with the plain flag byte.</summary>
    public static byte[] Encode(byte[] value)
    {
        var buf = new byte[1 + value.Length];
        buf[0] = FlagPlain;
        value.CopyTo(buf, 1);
        return buf;
    }

    /// <summary>
    /// Wraps <paramref name="value"/> with the TTL flag byte and the expiry timestamp.
    /// </summary>
    public static byte[] EncodeWithExpiry(byte[] value, DateTimeOffset expiry)
    {
        var buf = new byte[9 + value.Length];
        buf[0] = FlagTtl;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(1), expiry.ToUnixTimeMilliseconds());
        value.CopyTo(buf, 9);
        return buf;
    }

    /// <summary>
    /// Decodes a stored value at read time.
    /// Returns <see langword="null"/> when the entry has expired.
    /// </summary>
    public static byte[]? Decode(byte[] stored)
    {
        if (stored.Length == 0) return stored;

        if (stored[0] == FlagTtl)
        {
            if (stored.Length < 9) return null; // malformed
            long expiryMs = BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(1, 8));
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiryMs) return null;
            return stored[9..];
        }

        return stored[1..]; // FlagPlain (or unknown flag treated as plain)
    }

    /// <summary>
    /// Decodes a stored value during compaction.
    /// Returns <c>(decoded, dropped: false)</c> for live entries, or
    /// <c>(null, dropped: true)</c> for expired / malformed entries.
    /// Tombstones (null input) are returned as <c>(null, false)</c> so they pass through.
    /// </summary>
    public static (byte[]? value, bool dropped) DecodeForCompaction(byte[]? stored)
    {
        if (stored is null) return (null, false); // tombstone — pass through

        if (stored.Length == 0 || stored[0] == FlagPlain)
            return (stored.Length == 0 ? stored : stored[1..], false);

        if (stored[0] == FlagTtl)
        {
            if (stored.Length < 9) return (null, true); // malformed
            long expiryMs = BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(1, 8));
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiryMs) return (null, true);
            return (stored[9..], false);
        }

        return (stored[1..], false); // unknown flag, treat as plain
    }
}
