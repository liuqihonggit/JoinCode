namespace JoinCode.BclBridge;

/// <summary>
/// BCL 文件操作实现 — 封装 System.IO 同步调用
/// <para>此项目设置 ExcludeJccAnalyzers=true，分析器不扫描</para>
/// <para>BCL 类型（FileStream/StreamReader 等）同步 Dispose 完全正确，无需 await using</para>
/// <para>编码判定与编码往返只在这里实现一次，上层 FileEncodingDetector 仅转发 —— 两份 BOM 判定必然分叉</para>
/// </summary>
public sealed class BclFileIO : IBclFileIO {
    /// <summary>单例实例</summary>
    public static readonly BclFileIO Instance = new();

    // 无 BOM 的 UTF-8。Encoding.UTF8 自带 preamble(EF BB BF)，把它当默认编码会让
    // 无 BOM 文件在「读-改-写」后凭空多出 BOM。
    private static readonly System.Text.UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // UTF-32BE 带 BOM。.NET 没有现成静态实例（Encoding.UTF32 是小端），缓存一个避免每次检测都 new。
    private static readonly System.Text.UTF32Encoding s_utf32BeWithBom = new(bigEndian: true, byteOrderMark: true);

    private BclFileIO() { }

    /// <summary>无 BOM 的 UTF-8 —— 无 BOM 文件的默认编码，也是「写入不新增 BOM」的保证</summary>
    public static System.Text.Encoding Utf8NoBom => s_utf8NoBom;

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public (string Content, System.Text.Encoding Encoding) DecodeBytes(byte[] bytes) {
        var encoding = DetectFromBOM(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        // detectEncodingFromByteOrderMarks 保持默认 true：StreamReader 只会跳过与 encoding 自身
        // preamble 一致的那段 BOM，判定结论仍来自 DetectFromBOM，不会出现第二套规则。
        using var reader = new StreamReader(ms, encoding);
        return (reader.ReadToEnd(), encoding);
    }

    /// <inheritdoc />
    public string DecodeBytesWithEncoding(byte[] bytes, System.Text.Encoding encoding) {
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(ms, encoding);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 从字节数组开头检测 BOM 编码。
    /// <para>契约：原文件有签名就返回带 preamble 的编码，没有就返回无 BOM 的 UTF-8 ——
    /// 上层用「解码返回的 encoding」再编码回去时，BOM 有无被原样保持。</para>
    /// </summary>
    /// <param name="buffer">文件开头若干字节（至少 4 字节可覆盖全部 BOM）</param>
    public static System.Text.Encoding DetectFromBOM(ReadOnlySpan<byte> buffer) {
        // 4 字节 BOM 必须先判，否则 UTF-32LE(FF FE 00 00) 会被当成 UTF-16LE(FF FE)
        if (buffer.Length >= 4) {
            if (buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                return System.Text.Encoding.UTF32; // UTF-32LE，preamble FF FE 00 00
            if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return s_utf32BeWithBom;
        }

        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return System.Text.Encoding.UTF8; // 带 BOM 的 UTF-8

        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            return System.Text.Encoding.Unicode; // UTF-16LE

        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode; // UTF-16BE

        // 无 BOM：编码按 UTF-8 解读，但写回时不得新增 BOM
        return s_utf8NoBom;
    }

    /// <summary>
    /// 把字符串按给定编码写回字节 —— 该编码有 preamble 就带上，没有就不带。
    /// <para>与 <see cref="DetectFromBOM"/> 配对构成往返：BOM 的有无跟着原文件走</para>
    /// </summary>
    /// <param name="content">文本内容（不含 BOM 字符，解码时已剥离）</param>
    /// <param name="encoding">要使用的编码，通常来自 <see cref="DecodeBytes"/></param>
    public static byte[] EncodeString(string content, System.Text.Encoding encoding) {
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(content);
        if (preamble.Length == 0)
            return contentBytes;

        var bytes = new byte[preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, bytes, preamble.Length, contentBytes.Length);
        return bytes;
    }
}
