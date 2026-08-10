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
    /// 支持 UTF-16LE / UTF-16BE / UTF-8 / UTF-32LE / UTF-32BE BOM 检测
    /// </summary>
    public static Encoding DetectFromBOM(ReadOnlySpan<byte> buffer)
    {
        // TS: bytesRead === 0 → 'utf8'
        if (buffer.Length == 0)
            return Encoding.UTF8;

        // UTF-32 LE BOM: FF FE 00 00
        if (buffer.Length >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
            return Encoding.UTF32; // UTF-32LE in .NET

        // UTF-32 BE BOM: 00 00 FE FF
        if (buffer.Length >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);

        // TS: bytesRead >= 2 && buffer[0] === 0xff && buffer[1] === 0xfe → 'utf16le'
        // UTF-16LE BOM: FF FE
        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            return Encoding.Unicode; // Unicode = UTF-16LE in .NET

        // UTF-16BE BOM: FE FF
        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16BE in .NET

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
    /// 检测失败时记录警告而非静默吞异常，降级返回 UTF-8。
    /// </summary>
    public static async Task<Encoding> DetectFromFileAsync(
        string filePath,
        IFileSystem fs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 检测失败时记录警告，而非静默吞异常
            // 下游用 UTF-8 解码可能产生乱码，用户需知晓检测失败
            logger?.LogWarning(ex, "文件编码检测失败，降级为 UTF-8: {FilePath}", filePath);
            return Encoding.UTF8;
        }
    }
}
