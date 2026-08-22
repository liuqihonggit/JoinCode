namespace Infrastructure.Configuration;

/// <summary>
/// 网络重试统一配置 — 所有出站网络重试参数的唯一数据源
/// <para>对应 settings.json 的 "NetworkRetry" 节点</para>
/// <para>请求-响应型：通过 ToRetryConfig() 转换为 RetryConfig，注入 ResilientHttpExecutor</para>
/// <para>长连接重连型：ReconnectBaseDelay/ReconnectMaxDelay 供 SSE/SSH/Bridge 等统一取用</para>
/// </summary>
public sealed class NetworkRetryOptions
{
    /// <summary>重试总预算，默认 24h，耗尽抛 NetworkRetryBudgetExhaustedException</summary>
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromHours(24);

    /// <summary>退避起始延迟，默认 2s</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>退避上限延迟，默认 5min，到达后稳态重试</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>退避策略，默认 ExponentialWithJitter</summary>
    public string Strategy { get; init; } = "ExponentialWithJitter";

    /// <summary>抖动比例 ±，默认 0.25（±25%），防止多客户端同步重试</summary>
    public double JitterFraction { get; init; } = 0.25;

    /// <summary>网络不可用时是否暂停预算计时，默认 true</summary>
    public bool PauseBudgetOnNetworkUnavailable { get; init; } = true;

    /// <summary>长连接重连起始延迟，默认 2s（供 SSE/SSH/Bridge/MCP 统一取用）</summary>
    public TimeSpan ReconnectBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>长连接重连上限延迟，默认 5min</summary>
    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 转换为 ResilientHttpExecutor 的 RetryConfig — 请求-响应型重试配置
    /// </summary>
    public RetryConfig ToRetryConfig() => new()
    {
        TotalBudget = TotalBudget,
        BaseDelay = BaseDelay,
        MaxDelay = MaxDelay,
        Strategy = ParseStrategy(Strategy),
        PauseBudgetOnNetworkUnavailable = PauseBudgetOnNetworkUnavailable,
    };

    /// <summary>
    /// 转换为长连接重连的 ExponentialBackoff 参数 — 供 SSE/SSH/Bridge/MCP 统一取用
    /// </summary>
    public (TimeSpan BaseDelay, TimeSpan MaxDelay) ToReconnectParams() => (ReconnectBaseDelay, ReconnectMaxDelay);

    private static BackoffStrategy ParseStrategy(string s) => s switch
    {
        "Fixed" => BackoffStrategy.Fixed,
        "Linear" => BackoffStrategy.Linear,
        "Exponential" => BackoffStrategy.Exponential,
        _ => BackoffStrategy.ExponentialWithJitter,
    };
}
