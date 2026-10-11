namespace McpClient;

/// <summary>
/// MCP 二进制内容辅助方法 — 对齐 TS mcpOutputStorage.ts 的静态方法
/// </summary>
public static class McpBinaryHelper {
    /// <summary>
    /// 判断 MIME 类型是否为二进制 — 委托单数据源 BinaryContentTypeCatalog.IsBinaryContentType
    /// </summary>
    public static bool IsBinaryContentType(string? contentType) => BinaryContentTypeCatalog.IsBinaryContentType(contentType);

    /// <summary>
    /// 判断 MIME 类型是否为图片 — 图片走 base64 内联路径，不写盘
    /// </summary>
    public static bool IsImageMimeType(string? mimeType) {
        if (string.IsNullOrEmpty(mimeType))
            return false;

        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成持久化 ID — 对齐 TS persistId 格式 mcp-{serverName}-blob-{timestamp}-{random}
    /// </summary>
    public static string GeneratePersistId(string serverName) {
        var normalized = NameNormalizer.NormalizeForMcp(serverName, '-');
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = Random.Shared.Next(0, 0xFFFFFF).ToString("x6");
        return $"mcp-{normalized}-blob-{timestamp}-{random}";
    }

    /// <summary>
    /// 生成二进制内容保存后的消息 — 对齐 TS getBinaryBlobSavedMessage
    /// </summary>
    public static string GetBinaryBlobSavedMessage(string filepath, string? mimeType, int size, string sourceDescription) {
        var typeInfo = string.IsNullOrEmpty(mimeType) ? "unknown type" : mimeType;
        return $"{sourceDescription}Binary content ({typeInfo}, {size} bytes) saved to {filepath}";
    }
}