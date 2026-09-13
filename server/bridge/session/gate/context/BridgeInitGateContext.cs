namespace Core.Bridge.Gate;


/// <summary>
/// Bridge 初始化门控上下文 — 承载 Bridge 启动所需的配置选项、运行时句柄与可变状态
/// </summary>
public sealed class BridgeInitGateContext : PipelineContextBase
{
    /// <summary>Bridge 初始化选项</summary>
    public required BridgeInitOptions Options { get; init; }
    /// <summary>Bridge 是否启用</summary>
    public required bool BridgeEnabled { get; init; }
    /// <summary>获取访问令牌的委托</summary>
    public required Func<string?> GetAccessToken { get; init; }
    /// <summary>获取组织 UUID 的委托</summary>
    public required Func<string?> GetOrgUUID { get; init; }
    /// <summary>获取基础 URL 的委托</summary>
    public required Func<string> GetBaseUrl { get; init; }
    /// <summary>文件系统抽象</summary>
    public required IFileSystem FileSystem { get; init; }
    /// <summary>HTTP 客户端（可选）</summary>
    public HttpClient? HttpClient { get; init; }
    /// <summary>传输工厂（可选）</summary>
    public IReplBridgeTransportFactory? TransportFactory { get; init; }
    /// <summary>日志器（可选）</summary>
    public ILogger? Logger { get; init; }
    /// <summary>V1 初始化管道（可选）</summary>
    public MiddlewarePipeline<V1BridgeInitContext>? V1Pipeline { get; init; }
    /// <summary>V2 初始化管道（可选）</summary>
    public MiddlewarePipeline<V2BridgeInitContext>? V2Pipeline { get; init; }
    /// <summary>时钟服务（可选）</summary>
    public IClockService? Clock { get; init; }
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>解析后的访问令牌</summary>
    public string? AccessToken { get; set; }
    /// <summary>解析后的组织 UUID</summary>
    public string? OrgUUID { get; set; }
    /// <summary>会话标题</summary>
    public string? Title { get; set; }
    /// <summary>解析后的基础 URL</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Bridge 句柄</summary>
    public IReplBridgeHandle? Handle { get; set; }
}
