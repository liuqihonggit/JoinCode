namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件编码检测器 — 对齐 TS: fileRead.ts detectFileEncoding + FileEditTool.ts L207-213
/// <para>只做转发：BOM 判定与编码往返的唯一实现在 <see cref="BclFileIO"/>（最低层、无上层依赖）。
/// 这里曾另存一份「无 BOM 时返回 Encoding.UTF8」的判定，导致每条 读-改-写 路径都给文件注入 BOM。</para>
/// </summary>
public static class FileEncodingDetector {
    /// <summary>无 BOM 的 UTF-8 —— 无 BOM 文件的默认编码，也是「写入不新增 BOM」的保证</summary>
    private static Encoding Utf8NoBom => BclFileIO.Utf8NoBom;

    /// <summary>
    /// 从字节数组前几个字节（BOM）检测编码。
    /// <para>实现只有一处（<see cref="BclFileIO.DetectFromBOM"/>）：有签名返回带 preamble 的编码，
    /// 没有就返回无 BOM 的 UTF-8，读写往返不会给文件新增或丢掉 BOM。</para>
    /// </summary>
    public static Encoding DetectFromBOM(ReadOnlySpan<byte> buffer) => BclFileIO.DetectFromBOM(buffer);

    /// <summary>
    /// 从文件路径检测编码（读取前几个字节检查 BOM）
    /// 对齐 TS: FileEditTool.ts L207-213
    /// 检测失败时记录警告而非静默吞异常，降级返回 UTF-8。
    /// </summary>
    public static async Task<Encoding> DetectFromFileAsync(
        string filePath,
        IFileSystem fs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null) {
        if (!fs.FileExists(filePath))
            return Utf8NoBom;

        try {
            // 读取前 4 字节足够检测所有 BOM
            var buffer = new byte[4];
            await using var stream = fs.CreateStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            return DetectFromBOM(buffer.AsSpan(0, bytesRead));
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            // 检测失败时记录警告，而非静默吞异常
            // 下游用 UTF-8 解码可能产生乱码，用户需知晓检测失败
            logger?.LogWarning(ex, "文件编码检测失败，降级为 UTF-8: {FilePath}", filePath);
            return Utf8NoBom;
        }
    }

    /// <summary>
    /// 从字节数组解码为字符串 — 自动检测 BOM 编码，StreamReader 自动跳过 BOM。
    /// </summary>
    public static (string Content, Encoding Encoding) DecodeBytes(byte[] bytes) => BclFileIO.Instance.DecodeBytes(bytes);

    /// <summary>
    /// 将字符串编码为字节数组 — 该编码有 BOM 就带上，没有就不带（原文件有无签名原样保持）。
    /// </summary>
    public static byte[] EncodeString(string content, Encoding encoding) => BclFileIO.EncodeString(content, encoding);
}