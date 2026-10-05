namespace Services.Api;

/// <summary>
/// API 客户端配置设置
/// </summary>
public sealed record ApiSettings {
    /// <summary>
    /// 基础 URL
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// 默认超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// 初始退避延迟（毫秒）
    /// </summary>
    public int InitialDelayMs { get; init; } = 1000;

    /// <summary>
    /// 最大退避延迟（毫秒）
    /// </summary>
    public int MaxDelayMs { get; init; } = 30000;

    /// <summary>
    /// 退避乘数
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// 是否启用抖动
    /// </summary>
    public bool EnableJitter { get; init; } = true;

    /// <summary>
    /// 抖动因子 (0-1)
    /// </summary>
    public double JitterFactor { get; init; } = 0.1;

    /// <summary>
    /// 用户代理字符串
    /// </summary>
    public string UserAgent { get; init; } = "JoinCode/1.0";

    /// <summary>
    /// 是否启用请求/响应日志
    /// </summary>
    public bool EnableLogging { get; init; } = true;

    /// <summary>
    /// 日志详细程度
    /// </summary>
    public ApiLoggingLevel LoggingLevel { get; init; } = ApiLoggingLevel.Basic;

    /// <summary>
    /// 认证令牌（可选，通常通过 OAuth 动态设置）
    /// </summary>
    public string? AuthToken { get; init; }

    /// <summary>
    /// 认证方案
    /// </summary>
    public string AuthScheme { get; init; } = "Bearer";

    /// <summary>
    /// 默认请求头
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; init; } = new();

    /// <summary>
    /// 转换为重试策略选项
    /// </summary>
    /// <returns>重试策略选项</returns>
    public RetryPolicyOptions ToRetryPolicyOptions() => new() {
        MaxRetryCount = MaxRetryCount,
        InitialDelay = TimeSpan.FromMilliseconds(InitialDelayMs),
        MaxDelay = TimeSpan.FromMilliseconds(MaxDelayMs),
        BackoffMultiplier = BackoffMultiplier,
        EnableJitter = EnableJitter,
        JitterFactor = JitterFactor
    };

    /// <summary>
    /// 转换为 API 客户端选项
    /// </summary>
    /// <returns>API 客户端选项</returns>
    public ApiClientOptions ToApiClientOptions() => new() {
        BaseUrl = BaseUrl,
        Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        RetryOptions = ToRetryPolicyOptions(),
        DefaultHeaders = DefaultHeaders,
        UserAgent = UserAgent
    };

    /// <summary>
    /// 转换为日志选项
    /// </summary>
    /// <returns>日志选项</returns>
    public ApiLoggingOptions ToLoggingOptions() => LoggingLevel switch {
        ApiLoggingLevel.None => ApiLoggingOptions.ErrorsOnly,
        ApiLoggingLevel.Basic => ApiLoggingOptions.Default,
        ApiLoggingLevel.Verbose => ApiLoggingOptions.Verbose,
        _ => ApiLoggingOptions.Default
    };
}

/// <summary>
/// API 日志详细程度级别
/// </summary>
public enum ApiLoggingLevel {
    /// <summary>
    /// 不记录
    /// </summary>
    [EnumValue("none")] None,

    /// <summary>
    /// 基本记录（仅请求方法、URL、状态码、耗时）
    /// </summary>
    [EnumValue("basic")] Basic,

    /// <summary>
    /// 详细记录（包含请求/响应体）
    /// </summary>
    [EnumValue("verbose")] Verbose
}