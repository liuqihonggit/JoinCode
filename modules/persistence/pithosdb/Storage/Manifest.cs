using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace PithosDB.Core.Storage;

/// <summary>
/// Durably records the current SSTable level structure so that recovery does not
/// depend on file-system metadata (creation timestamps, filename conventions, etc.).
///
/// <para>
/// File format (little-endian):
/// <code>
/// [magic     : 4 bytes]  0x50544853 ("PTHS")
/// [version   : 4 bytes]  1
/// [levels    : 4 bytes]  number of levels
/// For each level:
///   [files   : 4 bytes]  number of files in this level
///   For each file:
///     [pathLen : 4 bytes]
///     [path    : pathLen bytes, UTF-8]
/// [crc32     : 4 bytes]  CRC32 over all preceding bytes
/// </code>
/// </para>
///
/// <para>
/// Written atomically: the new manifest is first written to <c>MANIFEST.tmp</c>,
/// then renamed over <c>MANIFEST</c>. A crash mid-write leaves the previous
/// manifest intact.
/// </para>
/// </summary>
public sealed class Manifest
{
    private const uint Magic   = 0x50544853; // "PTHS"
    private const int  Version = 1;

    private readonly string _path;
    private readonly string _tmpPath;

    public Manifest(string directory)
    {
        _path    = Path.Combine(directory, "MANIFEST");
        _tmpPath = Path.Combine(directory, "MANIFEST.tmp");
    }

    /// <summary>
    /// Tries to read the manifest. Returns <see langword="false"/> if the file does
    /// not exist, the magic/version does not match, or the CRC32 check fails.
    /// </summary>
    public bool TryRead(out List<List<string>> levels)
    {
        levels = [];
        if (!File.Exists(_path)) return false;

        try
        {
            byte[] data = File.ReadAllBytes(_path);
            if (data.Length < 16) return false;

            int dataLen = data.Length - 4;
            uint stored   = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dataLen));
            uint computed = Crc32.HashToUInt32(data.AsSpan(0, dataLen));
            if (stored != computed) return false;

            var span = data.AsSpan(0, dataLen);
            int pos = 0;

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4;
            if (magic != Magic) return false;

            int version = BinaryPrimitives.ReadInt32LittleEndian(span[pos..]); pos += 4;
            if (version != Version) return false;

            int levelCount = BinaryPrimitives.ReadInt32LittleEndian(span[pos..]); pos += 4;
            levels = new List<List<string>>(levelCount);

            for (int l = 0; l < levelCount; l++)
            {
                int fileCount = BinaryPrimitives.ReadInt32LittleEndian(span[pos..]); pos += 4;
                var files = new List<string>(fileCount);
                for (int f = 0; f < fileCount; f++)
                {
                    int pathLen = BinaryPrimitives.ReadInt32LittleEndian(span[pos..]); pos += 4;
                    files.Add(Encoding.UTF8.GetString(span.Slice(pos, pathLen))); pos += pathLen;
                }
                levels.Add(files);
            }

            return true;
        }
        catch
        {
            levels = [];
            return false;
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="levels"/> to the manifest file.
    /// </summary>
    public void Write(List<List<string>> levels)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(Magic);
            bw.Write(Version);
            bw.Write(levels.Count);
            foreach (var level in levels)
            {
                bw.Write(level.Count);
                foreach (var path in level)
                {
                    byte[] pathBytes = Encoding.UTF8.GetBytes(path);
                    bw.Write(pathBytes.Length);
                    bw.Write(pathBytes);
                }
            }
        }

        byte[] data = ms.ToArray();
        uint crc = Crc32.HashToUInt32(data);

        // Write to temp file then rename for atomicity.
        using (var fs = new FileStream(_tmpPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(data);
            bw.Write(crc);
        }

        File.Move(_tmpPath, _path, overwrite: true);
    }
}
