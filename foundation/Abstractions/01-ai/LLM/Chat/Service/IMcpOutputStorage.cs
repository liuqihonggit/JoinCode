namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// MCP 二进制内容持久化接口 — 对齐 TS mcpOutputStorage.ts
/// </summary>
public interface IMcpOutputStorage
{
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
public sealed class PersistBinaryResult
{
    public required string Filepath { get; init; }
    public required int Size { get; init; }
    public required string Ext { get; init; }
}
