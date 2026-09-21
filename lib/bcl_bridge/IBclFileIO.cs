namespace JoinCode.BclBridge;

/// <summary>
/// BCL 文件操作接口 — 隔离区对外暴露的同步文件 API
/// <para>隔离区不引用分析器，BCL 同步调用（File.Exists/File.ReadAllText 等）在此自由使用</para>
/// <para>主项目通过此接口调用，不直接接触 BCL 类型，不触发 JCC9104</para>
/// </summary>
public interface IBclFileIO {
    /// <summary>检查文件是否存在</summary>
    bool FileExists(string path);

    /// <summary>同步读取全部文本</summary>
    string ReadAllText(string path);

    /// <summary>从字节数组解码为字符串 — 自动检测 BOM 编码，StreamReader 自动跳过 BOM</summary>
    (string Content, System.Text.Encoding Encoding) DecodeBytes(byte[] bytes);

    /// <summary>从字节数组用指定编码解码为字符串</summary>
    string DecodeBytesWithEncoding(byte[] bytes, System.Text.Encoding encoding);
}
