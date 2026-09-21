namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// MCP 二进制内容持久化接口 — 对齐 TS mcpOutputStorage.ts
/// </summary>
public interface IMcpOutputStorage {
    /// <summary>
    /// 将二进制内容写入磁盘
    /// </summary>
    /// <param name="bytes">原始字节</param>
    /// <param name="mimeType">MIME 类型</param>
    /// <param name="persistId">持久化 ID（由 McpOutputStorage.GeneratePersistId 生成）</param>
    /// <returns>持久化结果，失败返回 null</returns>
    PersistBinaryResult? PersistBinaryContent(ReadOnlySpan<byte> bytes, string? mimeType, string persistId);
}

/// <summary>
/// MCP 二进制内容持久化结果 — 对齐 TS mcpOutputStorage PersistBinaryResult
/// </summary>
public sealed class PersistBinaryResult {
    /// <summary>获取持久化文件路径。</summary>
    public required string Filepath { get; init; }
    /// <summary>获取内容大小（字节）。</summary>
    public required int Size { get; init; }
    /// <summary>获取文件扩展名。</summary>
    public required string Ext { get; init; }
}