namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 缓存配置设置
/// </summary>
public class CacheSettings
{
    /// <summary>
    /// 缓存过期时间（分钟）
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 最大缓存项数
    /// </summary>
    public int MaxCacheItems { get; set; } = WorkflowConstants.Cache.MaxCacheItems;

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// 压缩阈值（字节）
    /// </summary>
    public int CompressionThresholdBytes { get; set; } = 1024;
}

/// <summary>
/// 工具执行配置设置
/// </summary>
public class ToolExecutionSettings
{
    /// <summary>
    /// 工具执行超时时间（秒）
    /// </summary>
    public int ExecutionTimeoutSeconds { get; set; } = WorkflowConstants.Timeouts.ToolDefaultTimeoutSeconds;

    /// <summary>
    /// 是否启用工具缓存
    /// </summary>
    public bool EnableToolCache { get; set; } = true;

    /// <summary>
    /// 工具缓存过期时间（分钟）
    /// </summary>
    public int ToolCacheExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 最大工具并行执行数
    /// </summary>
    public int MaxParallelToolExecution { get; set; } = 5;

    /// <summary>
    /// 是否启用工具结果验证
    /// </summary>
    public bool EnableResultValidation { get; set; } = true;

    /// <summary>
    /// 危险工具列表
    /// </summary>
    public List<string> DangerousTools { get; set; } = new()
    {
        "shell",
        FileToolNameConstants.FileWrite,
        "file_delete"
    };
}

/// <summary>
/// LLM 执行配置设置
/// </summary>
public class LlmExecutionSettings
{
    /// <summary>
    /// 温度参数 (0-2)
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 最大令牌数
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Top P 采样参数
    /// </summary>
    public double TopP { get; set; } = 1.0;

    /// <summary>
    /// 频率惩罚
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0.0;

    /// <summary>
    /// 存在惩罚
    /// </summary>
    public double PresencePenalty { get; set; } = 0.0;

    /// <summary>
    /// 停止序列
    /// </summary>
    public List<string>? StopSequences { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    public int RetryDelayMs { get; set; } = WorkflowConstants.Retry.DefaultRetryDelayMs;
}
