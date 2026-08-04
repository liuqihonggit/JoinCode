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
    /// 是否启用流式工具并发执行 — 对齐 TS tengu_streaming_tool_execution2 特性门控
    /// 启用后，LLM 流式响应期间收到 tool_use block 就立即执行，而非等待流式结束
    /// 并发安全工具可并行执行，非并发安全工具独占执行
    /// </summary>
    public bool UseStreamingToolExecution { get; set; } = false;

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

    /// <summary>
    /// 工具健康评分配置 — 控制奖惩幅度、熔断阈值、时间衰减率
    /// </summary>
    public ToolScoreSettings ToolScore { get; set; } = new();

    /// <summary>
    /// 工具黑名单 — 完全禁止调用的工具（用户主动禁用）
    /// </summary>
    public List<string> BlacklistedTools { get; set; } = [];

    /// <summary>
    /// 工具降权配置 — 键为工具名，值为额外扣分（负数降低排序优先级）
    /// </summary>
    public Dictionary<string, int> ToolPenalties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 自定义工具链超边 — 用户可覆盖或扩展预设超边
    /// </summary>
    public List<HyperedgeSettings> CustomHyperedges { get; set; } = [];
}

/// <summary>
/// 超边配置 — 定义一组语义关联的工具共享评分空间
/// </summary>
public class HyperedgeSettings
{
    /// <summary>
    /// 超边标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 成员工具名称列表
    /// </summary>
    public List<string> ToolNames { get; set; } = [];

    /// <summary>
    /// 超边权重（0-1），影响共享评分在最终评分中的占比
    /// </summary>
    public double Weight { get; set; } = 0.5;

    /// <summary>
    /// 链路顺序 — LLM使用工具A后推荐的后续工具序列
    /// </summary>
    public List<string>? ChainOrder { get; set; }

    /// <summary>
    /// 转换为 ToolHyperedge
    /// </summary>
    public ToolHyperedge ToHyperedge() => new()
    {
        Id = Id,
        ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, [.. ToolNames]),
        Weight = Weight,
        ChainOrder = ChainOrder?.ToArray()
    };
}

/// <summary>
/// 工具健康评分配置 — 控制奖惩幅度、熔断阈值、时间衰减率
/// </summary>
public class ToolScoreSettings
{
    /// <summary>
    /// 成功执行评分增量
    /// </summary>
    public int SuccessDelta { get; set; } = 1;

    /// <summary>
    /// 失败执行评分增量（负数）
    /// </summary>
    public int FailDelta { get; set; } = -5;

    /// <summary>
    /// 熔断阈值 — 连续失败次数达到此值时自动禁用工具
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// 评分下限
    /// </summary>
    public int ScoreMin { get; set; } = -100;

    /// <summary>
    /// 评分上限
    /// </summary>
    public int ScoreMax { get; set; } = 100;

    /// <summary>
    /// 每小时时间衰减率 — 闲置工具的负分逐渐恢复
    /// </summary>
    public double DecayRatePerHour { get; set; } = 0.1;

    /// <summary>
    /// 每次衰减恢复的分数
    /// </summary>
    public int DecayRecoveryScore { get; set; } = 1;

    /// <summary>
    /// 转换为 ToolScoreConfig
    /// </summary>
    public ToolScoreConfig ToToolScoreConfig() => new()
    {
        SuccessDelta = SuccessDelta,
        FailDelta = FailDelta,
        CircuitBreakerThreshold = CircuitBreakerThreshold,
        ScoreMin = ScoreMin,
        ScoreMax = ScoreMax,
        DecayRatePerHour = DecayRatePerHour,
        DecayRecoveryScore = DecayRecoveryScore
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
