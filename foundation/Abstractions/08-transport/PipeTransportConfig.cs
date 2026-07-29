namespace JoinCode.Abstractions.Transport;

/// <summary>
/// 命名管道传输配置 — 替代旧 PipeEndpointConfig，统一到 Transport 契约层
/// </summary>
public sealed record PipeTransportConfig
{
    /// <summary>
    /// 管道名称
    /// </summary>
    public required string PipeName { get; init; }

    /// <summary>
    /// 连接超时时间（毫秒，默认 30000）
    /// </summary>
    public int ConnectionTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// 请求超时时间（毫秒，默认 120000）
    /// </summary>
    public int RequestTimeoutMs { get; init; } = 120000;
}
