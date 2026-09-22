namespace JoinCode.BclBridge;

/// <summary>
/// BCL 文件操作实现 — 封装 System.IO 同步调用
/// <para>此项目设置 ExcludeJccAnalyzers=true，分析器不扫描</para>
/// <para>BCL 类型（FileStream/StreamReader 等）同步 Dispose 完全正确，无需 await using</para>
/// </summary>
public sealed class BclFileIO : IBclFileIO {
    /// <summary>单例实例</summary>
    public static readonly BclFileIO Instance = new();

    private BclFileIO() { }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public (string Content, System.Text.Encoding Encoding) DecodeBytes(byte[] bytes) {
        var encoding = DetectFromBOM(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(ms, encoding);
        return (reader.ReadToEnd(), encoding);
    }

    /// <inheritdoc />
    public string DecodeBytesWithEncoding(byte[] bytes, System.Text.Encoding encoding) {
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(ms, encoding);
        return reader.ReadToEnd();
    }

    private static System.Text.Encoding DetectFromBOM(byte[] bytes) {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode;
        return System.Text.Encoding.UTF8;
    }
}
