namespace McpProtocol;

/// <summary>
/// MCP 资源处理器接口 — 实现 resources/read 请求的资源内容读取
/// </summary>
public interface IResourceHandler
{
    /// <summary>资源 Uri(唯一标识,作为资源字典键)</summary>
    string Uri { get; }
    /// <summary>资源名称(人类可读)</summary>
    string Name { get; }
    /// <summary>资源描述,可为 null</summary>
    string? Description { get; }
    /// <summary>资源 MIME 类型,可为 null</summary>
    string? MimeType { get; }
    /// <summary>
    /// 异步读取资源内容
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>资源内容</returns>
    Task<McpResourceContent> ReadAsync(CancellationToken cancellationToken = default);
}
