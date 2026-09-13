
namespace Core.Configuration;

/// <summary>
/// QueryEngine配置
/// </summary>
public class QueryEngineConfig
{
    /// <summary>
    /// 温度参数
    /// </summary>
    [Range(0, 2)]
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 最大Token数
    /// </summary>
    [Range(1, 128000)]
    public int MaxTokens { get; set; } = 4000;

    /// <summary>
    /// Top P采样
    /// </summary>
    [Range(0, 1)]
    public float TopP { get; set; } = 0.95f;

    /// <summary>
    /// 最大工具调用迭代次数
    /// </summary>
    [Range(1, 1000)]
    public int MaxToolCallIterations { get; set; } = 128;

    /// <summary>
    /// 是否启用思考模式
    /// </summary>
    public bool EnableThinkingMode { get; set; } = false;

    /// <summary>
    /// 思考模式开始标记
    /// </summary>
    public string ThinkingStartTag { get; set; } = "<thinking>";

    /// <summary>
    /// 思考模式结束标记
    /// </summary>
    public string ThinkingEndTag { get; set; } = "</thinking>";

    /// <summary>
    /// 重试配置
    /// </summary>
    public RetryConfig Retry { get; set; } = new RetryConfig();

    /// <summary>
    /// 成本追踪配置
    /// </summary>
    public CostTrackingConfig CostTracking { get; set; } = new CostTrackingConfig();

    /// <summary>
    /// USD预算上限
    /// </summary>
    public decimal? MaxUsdBudget { get; set; }

    /// <summary>
    /// USD告警阈值（0.0-1.0）
    /// </summary>
    public double UsdAlertThreshold { get; set; } = 0.8;

    /// <summary>
    /// 内容替换配置 — 对齐 TS tengu_hawthorn_steeple feature flag
    /// </summary>
    public ContentReplacementConfig ContentReplacement { get; set; } = new();
}

/// <summary>
/// 内容替换配置 — 对齐 TS toolResultStorage.ts feature flags
/// </summary>
public class ContentReplacementConfig
{
    /// <summary>
    /// 是否启用内容替换 — 对齐 TS getFeatureValue_CACHED_MAY_BE_STALE('tengu_hawthorn_steeple', false)
    /// 默认启用（C# 暂无 GrowthBook 功能开关系统）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 每 user message 的工具结果总大小上限 — 对齐 TS MAX_TOOL_RESULTS_PER_MESSAGE_CHARS
    /// </summary>
    [Range(10000, 1000000)]
    public int MaxToolResultsPerMessageChars { get; set; } = 200000;
}

/// <summary>
/// 重试配置
/// </summary>
public class RetryConfig
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    [Range(0, 60000)]
    public int RetryDelayMs { get; set; } = WorkflowConstants.Retry.DefaultRetryDelayMs;

    /// <summary>
    /// 是否启用指数退避
    /// </summary>
    public bool EnableExponentialBackoff { get; set; } = true;
}

/// <summary>
/// 成本追踪配置
/// </summary>
public class CostTrackingConfig
{
    /// <summary>
    /// 是否启用成本追踪
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 每1000个输入Token的成本（美元）
    /// </summary>
    public decimal InputTokenCostPer1K { get; set; } = 0.0015m;

    /// <summary>
    /// 每1000个输出Token的成本（美元）
    /// </summary>
    public decimal OutputTokenCostPer1K { get; set; } = 0.002m;
}

#region Builders

/// <summary>
/// QueryEngineConfig 构建器 - 支持链式配置
/// </summary>
public sealed class QueryEngineConfigBuilder
{
    private readonly QueryEngineConfig _config;

    private QueryEngineConfigBuilder()
    {
        _config = new QueryEngineConfig();
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static QueryEngineConfigBuilder Create() => new();

    /// <summary>
    /// 设置温度参数
    /// </summary>
    public QueryEngineConfigBuilder WithTemperature(float temperature)
    {
        _config.Temperature = temperature;
        return this;
    }

    /// <summary>
    /// 设置最大Token数
    /// </summary>
    public QueryEngineConfigBuilder WithMaxTokens(int maxTokens)
    {
        _config.MaxTokens = maxTokens;
        return this;
    }

    /// <summary>
    /// 设置Top P采样
    /// </summary>
    public QueryEngineConfigBuilder WithTopP(float topP)
    {
        _config.TopP = topP;
        return this;
    }

    /// <summary>
    /// 设置最大工具调用迭代次数
    /// </summary>
    public QueryEngineConfigBuilder WithMaxToolCallIterations(int iterations)
    {
        _config.MaxToolCallIterations = iterations;
        return this;
    }

    /// <summary>
    /// 启用思考模式
    /// </summary>
    public QueryEngineConfigBuilder EnableThinkingMode()
    {
        _config.EnableThinkingMode = true;
        return this;
    }

    /// <summary>
    /// 禁用思考模式
    /// </summary>
    public QueryEngineConfigBuilder DisableThinkingMode()
    {
        _config.EnableThinkingMode = false;
        return this;
    }

    /// <summary>
    /// 设置思考模式标记
    /// </summary>
    public QueryEngineConfigBuilder WithThinkingTags(string startTag, string endTag)
    {
        _config.ThinkingStartTag = startTag;
        _config.ThinkingEndTag = endTag;
        return this;
    }

    /// <summary>
    /// 配置重试选项
    /// </summary>
    public QueryEngineConfigBuilder WithRetry(Action<RetryConfigBuilder> configure)
    {
        var builder = new RetryConfigBuilder(_config.Retry);
        configure(builder);
        _config.Retry = builder.Build();
        return this;
    }

    /// <summary>
    /// 配置成本追踪选项
    /// </summary>
    public QueryEngineConfigBuilder WithCostTracking(Action<CostTrackingConfigBuilder> configure)
    {
        var builder = new CostTrackingConfigBuilder(_config.CostTracking);
        configure(builder);
        _config.CostTracking = builder.Build();
        return this;
    }

    /// <summary>
    /// 使用创意模式（高温度）
    /// </summary>
    public QueryEngineConfigBuilder UseCreativeMode()
    {
        _config.Temperature = 1.0f;
        _config.TopP = 0.95f;
        return this;
    }

    /// <summary>
    /// 使用精确模式（低温度）
    /// </summary>
    public QueryEngineConfigBuilder UsePreciseMode()
    {
        _config.Temperature = 0.3f;
        _config.TopP = 0.5f;
        return this;
    }

    /// <summary>
    /// 使用平衡模式
    /// </summary>
    public QueryEngineConfigBuilder UseBalancedMode()
    {
        _config.Temperature = 0.7f;
        _config.TopP = 0.95f;
        return this;
    }

    /// <summary>
    /// 设置USD预算上限
    /// </summary>
    public QueryEngineConfigBuilder WithMaxUsdBudget(decimal maxUsd)
    {
        _config.MaxUsdBudget = maxUsd;
        return this;
    }

    /// <summary>
    /// 设置USD告警阈值
    /// </summary>
    public QueryEngineConfigBuilder WithUsdAlertThreshold(double threshold)
    {
        _config.UsdAlertThreshold = threshold;
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    public QueryEngineConfig Build() => _config;
}

/// <summary>
/// RetryConfig 构建器
/// </summary>
public sealed class RetryConfigBuilder
{
    private readonly RetryConfig _config;

    public RetryConfigBuilder(RetryConfig initial)
    {
        _config = new RetryConfig
        {
            MaxRetries = initial.MaxRetries,
            RetryDelayMs = initial.RetryDelayMs,
            EnableExponentialBackoff = initial.EnableExponentialBackoff
        };
    }

    /// <summary>
    /// 设置最大重试次数
    /// </summary>
    public RetryConfigBuilder WithMaxRetries(int maxRetries)
    {
        _config.MaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// 设置重试延迟
    /// </summary>
    public RetryConfigBuilder WithRetryDelay(int delayMs)
    {
        _config.RetryDelayMs = delayMs;
        return this;
    }

    /// <summary>
    /// 启用指数退避
    /// </summary>
    public RetryConfigBuilder EnableExponentialBackoff()
    {
        _config.EnableExponentialBackoff = true;
        return this;
    }

    /// <summary>
    /// 禁用指数退避
    /// </summary>
    public RetryConfigBuilder DisableExponentialBackoff()
    {
        _config.EnableExponentialBackoff = false;
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    public RetryConfig Build() => _config;
}

/// <summary>
/// CostTrackingConfig 构建器
/// </summary>
public sealed class CostTrackingConfigBuilder
{
    private readonly CostTrackingConfig _config;

    public CostTrackingConfigBuilder(CostTrackingConfig initial)
    {
        _config = new CostTrackingConfig
        {
            Enabled = initial.Enabled,
            InputTokenCostPer1K = initial.InputTokenCostPer1K,
            OutputTokenCostPer1K = initial.OutputTokenCostPer1K
        };
    }

    /// <summary>
    /// 设置是否启用成本追踪
    /// </summary>
    public CostTrackingConfigBuilder WithEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        return this;
    }

    /// <summary>
    /// 启用成本追踪
    /// </summary>
    public CostTrackingConfigBuilder Enable()
    {
        _config.Enabled = true;
        return this;
    }

    /// <summary>
    /// 禁用成本追踪
    /// </summary>
    public CostTrackingConfigBuilder Disable()
    {
        _config.Enabled = false;
        return this;
    }

    /// <summary>
    /// 设置输入Token成本
    /// </summary>
    public CostTrackingConfigBuilder WithInputTokenCost(decimal costPer1K)
    {
        _config.InputTokenCostPer1K = costPer1K;
        return this;
    }

    /// <summary>
    /// 设置输出Token成本
    /// </summary>
    public CostTrackingConfigBuilder WithOutputTokenCost(decimal costPer1K)
    {
        _config.OutputTokenCostPer1K = costPer1K;
        return this;
    }

    /// <summary>
    /// 设置Token成本
    /// </summary>
    public CostTrackingConfigBuilder WithTokenCosts(decimal inputCostPer1K, decimal outputCostPer1K)
    {
        _config.InputTokenCostPer1K = inputCostPer1K;
        _config.OutputTokenCostPer1K = outputCostPer1K;
        return this;
    }

    /// <summary>
    /// 使用 GPT-4 价格
    /// </summary>
    public CostTrackingConfigBuilder UseGpt4Pricing()
    {
        _config.InputTokenCostPer1K = 0.03m;
        _config.OutputTokenCostPer1K = 0.06m;
        return this;
    }

    /// <summary>
    /// 使用 GPT-4o 价格
    /// </summary>
    public CostTrackingConfigBuilder UseGpt4oPricing()
    {
        _config.InputTokenCostPer1K = 0.005m;
        _config.OutputTokenCostPer1K = 0.015m;
        return this;
    }

    /// <summary>
    /// 使用 GPT-3.5 价格
    /// </summary>
    public CostTrackingConfigBuilder UseGpt35Pricing()
    {
        _config.InputTokenCostPer1K = 0.0015m;
        _config.OutputTokenCostPer1K = 0.002m;
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    public CostTrackingConfig Build() => _config;
}

#endregion
