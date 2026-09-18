namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 流文本读写扩展 — 消除 <c>new StreamReader(stream, Encoding.UTF8)</c> 样板代码
/// </summary>
public static class StreamTextScope
{
    /// <summary>
    /// 以 UTF-8 编码创建 StreamReader
    /// </summary>
    public static StreamReader AsUtf8Reader(this Stream stream, bool leaveOpen = false)
        => new(stream, System.Text.Encoding.UTF8, leaveOpen: leaveOpen);

    /// <summary>
    /// 以 UTF-8 编码创建 StreamWriter
    /// </summary>
    public static StreamWriter AsUtf8Writer(this Stream stream, bool leaveOpen = false)
        => new(stream, System.Text.Encoding.UTF8, leaveOpen: leaveOpen);
}
