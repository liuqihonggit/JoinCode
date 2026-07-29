namespace JoinCode.Transport.Bridge;

/// <summary>
/// BridgeTokenRefreshScheduler 配置选项
/// </summary>
public sealed record TokenRefreshOptions
{
    /// <summary>获取当前访问令牌的委托</summary>
    public required Func<string?> GetAccessToken { get; init; }

    /// <summary>令牌刷新成功后的回调（sessionId, newToken）</summary>
    public required Action<string, string> OnRefresh { get; init; }

    /// <summary>调度器标签，用于日志标识</summary>
    public required string Label { get; init; }

    /// <summary>提前刷新缓冲时间（毫秒），默认 5 分钟</summary>
    public int RefreshBufferMs { get; init; } = 300_000;

    /// <summary>可选日志记录器</summary>
    public ILogger? Logger { get; init; }
}
