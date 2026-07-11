namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件编码检测器 — 对齐 TS: fileRead.ts detectFileEncoding + FileEditTool.ts L207-213
/// TS 逻辑：检查 BOM（0xFF 0xFE → UTF-16LE），否则默认 UTF-8
/// 空文件默认 UTF-8（不是 ASCII），修复写入 emoji/CJK 时损坏的 bug
/// </summary>
public static class FileEncodingDetector
{
    /// <summary>
    /// 从字节数组前几个字节（BOM）检测编码
    /// 对齐 TS: fileRead.ts L33-44
    /// </summary>
    public static Encoding DetectFromBOM(ReadOnlySpan<byte> buffer)
    {
        // TS: bytesRead === 0 → 'utf8'
        if (buffer.Length == 0)
            return Encoding.UTF8;

        // TS: bytesRead >= 2 && buffer[0] === 0xff && buffer[1] === 0xfe → 'utf16le'
        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            return Encoding.Unicode; // Unicode = UTF-16LE in .NET

        // TS: bytesRead >= 3 && buffer[0] === 0xef && buffer[1] === 0xbb && buffer[2] === 0xbf → 'utf8'
        // UTF-8 BOM 也返回 UTF-8（.NET 的 Encoding.UTF8 会自动处理 BOM）
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return Encoding.UTF8;

        // TS: 默认 utf8
        return Encoding.UTF8;
    }

    /// <summary>
    /// 从文件路径检测编码（读取前几个字节检查 BOM）
    /// 对齐 TS: FileEditTool.ts L207-213
    /// </summary>
    public static async Task<Encoding> DetectFromFileAsync(string filePath, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        if (!fs.FileExists(filePath))
            return Encoding.UTF8;

        try
        {
            // 读取前 4 字节足够检测所有 BOM
            var buffer = new byte[4];
            await using var stream = fs.CreateStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            return DetectFromBOM(buffer.AsSpan(0, bytesRead));
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
