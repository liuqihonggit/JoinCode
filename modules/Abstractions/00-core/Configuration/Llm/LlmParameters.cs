namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// LLM 参数配置 - 统一所有 LLM 调用参数
/// </summary>
public sealed record LlmParameters
{
    /// <summary>
    /// 温度参数 (0-2)
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// 最大 Token 数
    /// </summary>
    public int MaxTokens { get; init; } = 4000;

    /// <summary>
    /// Top P 采样 (0-1)
    /// </summary>
    public float TopP { get; init; } = 0.95f;

    /// <summary>
    /// 频率惩罚 (-2 到 2)
    /// </summary>
    public float FrequencyPenalty { get; init; } = 0f;

    /// <summary>
    /// 存在惩罚 (-2 到 2)
    /// </summary>
    public float PresencePenalty { get; init; } = 0f;

    /// <summary>
    /// 默认参数
    /// </summary>
    public static LlmParameters Default => new();

    /// <summary>
    /// 创意模式 - 高温度，适合头脑风暴
    /// </summary>
    public static LlmParameters Creative => new()
    {
        Temperature = 1.0f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 平衡模式 - 默认参数
    /// </summary>
    public static LlmParameters Balanced => new()
    {
        Temperature = 0.7f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 精确模式 - 低温度，适合代码生成、分析
    /// </summary>
    public static LlmParameters Precise => new()
    {
        Temperature = 0.3f,
        TopP = 0.5f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 计划 Agent 参数
    /// </summary>
    public static LlmParameters PlanAgent => new()
    {
        Temperature = 0.5f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 探索 Agent 参数
    /// </summary>
    public static LlmParameters ExploreAgent => new()
    {
        Temperature = 0.6f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 验证 Agent 参数
    /// </summary>
    public static LlmParameters VerificationAgent => new()
    {
        Temperature = 0.3f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 通用 Agent 参数
    /// </summary>
    public static LlmParameters GeneralPurposeAgent => new()
    {
        Temperature = 0.7f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// Claude Code 引导 Agent 参数
    /// </summary>
    public static LlmParameters ClaudeCodeGuideAgent => new()
    {
        Temperature = 0.4f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 上下文压缩 Agent 参数
    /// </summary>
    public static LlmParameters ContextCompressionAgent => new()
    {
        Temperature = 0.3f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 聊天服务参数
    /// </summary>
    public static LlmParameters Chat => new()
    {
        Temperature = 0.7f,
        TopP = 0.95f,
        MaxTokens = 2000
    };

    /// <summary>
    /// 查询引擎参数
    /// </summary>
    public static LlmParameters QueryEngine => new()
    {
        Temperature = 0.7f,
        TopP = 0.95f,
        MaxTokens = 4000
    };

    /// <summary>
    /// 创建副本并修改温度
    /// </summary>
    public LlmParameters WithTemperature(float temperature) =>
        this with { Temperature = temperature };

    /// <summary>
    /// 创建副本并修改最大 Token 数
    /// </summary>
    public LlmParameters WithMaxTokens(int maxTokens) =>
        this with { MaxTokens = maxTokens };

    /// <summary>
    /// 创建副本并修改 TopP
    /// </summary>
    public LlmParameters WithTopP(float topP) =>
        this with { TopP = topP };
}
