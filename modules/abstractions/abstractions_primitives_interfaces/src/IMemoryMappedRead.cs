namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 内存映射只读视图 — 零拷贝文件访问。
/// <para>生产: MemoryMappedFile 包装，OS 按需分页加载；测试: byte[] 包装。</para>
/// <para>AsSpan 返回的 span 在 Dispose 前有效，禁止跨 await 使用。</para>
/// </summary>
public interface IMemoryMappedRead : IDisposable {
    /// <summary>文件总长度（字节）。</summary>
    long Length { get; }

    /// <summary>
    /// 零拷贝映射整个文件为 ReadOnlySpan&lt;byte&gt;。
    /// <para>span 在 Dispose 前有效，禁止跨 await 边界。</para>
    /// </summary>
    ReadOnlySpan<byte> AsSpan();
}
