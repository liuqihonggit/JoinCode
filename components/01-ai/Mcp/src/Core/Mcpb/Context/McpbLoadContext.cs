namespace McpClient.Mcpb;

/// <summary>
/// MCPB 加载管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class McpbLoadContext : IPipelineContext
{
    // === 输入 ===

    /// <summary>MCPB 源路径（本地文件路径或 URL）</summary>
    public required string Source { get; init; }

    /// <summary>解压目标基础路径</summary>
    public required string ExtractBasePath { get; init; }

    /// <summary>是否为 URL 源</summary>
    public bool IsUrlSource { get; init; }

    /// <summary>HTTP 客户端（URL 源时必需）</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === McpbValidationMiddleware 填充 ===

    /// <summary>本地文件路径（URL 下载后也为本地临时路径）</summary>
    public string LocalFilePath { get; set; } = string.Empty;

    /// <summary>URL 下载的临时文件路径（需要 finally 清理）</summary>
    public string? TempFilePath { get; set; }

    // === McpbHashMiddleware 填充 ===

    /// <summary>文件内容哈希（用于缓存键）</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>解压目标路径</summary>
    public string ExtractPath { get; set; } = string.Empty;

    // === McpbCacheCheckMiddleware 填充 ===

    /// <summary>缓存是否命中</summary>
    public bool IsCacheHit { get; set; }

    // === McpbManifestMiddleware 填充 ===

    /// <summary>解析后的清单</summary>
    public McpbManifest? Manifest { get; set; }

    // === 输出 ===

    /// <summary>最终加载结果</summary>
    public McpbLoadResult? Result { get; set; }

    // === IPipelineContext ===

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
