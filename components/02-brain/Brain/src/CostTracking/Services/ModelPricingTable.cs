
namespace Core.CostTracking;

/// <summary>
/// 模型定价表 — 统一管理所有模型的 Prompt/Completion 每1K token 价格
/// 消除 ChatService.EstimatePromptCostPer1K 和 CostTracker.LoadDefaultModelCosts 的重复硬编码
/// </summary>
internal static class ModelPricingTable
{
    /// <summary>
    /// 默认 Prompt 每1K token 价格（未知模型回退值）
    /// </summary>
    public const decimal DefaultPromptCostPer1K = 0.01m;

    /// <summary>
    /// 默认 Completion 每1K token 价格（未知模型回退值）
    /// </summary>
    public const decimal DefaultCompletionCostPer1K = 0.03m;

    /// <summary>
    /// 模型定价条目 — Key 为模型关键字（用于 Contains 匹配），Value 为定价
    /// 注意: 更具体的关键字排在前面（如 "gpt-4o-mini" 在 "gpt-4o" 前面）
    /// </summary>
    private static readonly (string Keyword, decimal PromptCost, decimal CompletionCost)[] s_entries =
    [
        // OpenAI 系列 — 更具体版本优先
        ("gpt-4o-mini", 0.00015m, 0.0006m),
        ("gpt-4o", 0.005m, 0.015m),
        ("gpt-4-turbo-preview", 0.01m, 0.03m),
        ("gpt-4-turbo", 0.01m, 0.03m),
        ("gpt-4-0125-preview", 0.01m, 0.03m),
        ("gpt-4-1106-preview", 0.01m, 0.03m),
        ("gpt-4", 0.03m, 0.06m),
        ("gpt-3.5-turbo-0125", 0.0005m, 0.0015m),
        ("gpt-3.5-turbo-1106", 0.001m, 0.002m),
        ("gpt-3.5-turbo", 0.0005m, 0.0015m),
        ("gpt-3.5", 0.0005m, 0.0015m),

        // Anthropic 系列
        ("claude-3-opus", 0.015m, 0.075m),
        ("claude-3-sonnet", 0.003m, 0.015m),
        ("claude-3-haiku", 0.00025m, 0.00125m),
        ("claude-opus", 0.015m, 0.075m),
        ("claude-sonnet", 0.003m, 0.015m),
        ("claude-haiku", 0.00025m, 0.00125m),
        ("opus", 0.015m, 0.075m),
        ("sonnet", 0.003m, 0.015m),
        ("haiku", 0.00025m, 0.00125m),

        // DeepSeek 系列
        ("deepseek", 0.00014m, 0.00028m),

        // Azure 系列
        ("azure-gpt-4-turbo", 0.01m, 0.03m),
        ("azure-gpt-4", 0.03m, 0.06m),
        ("azure-gpt-35-turbo", 0.0005m, 0.0015m)
    ];

    /// <summary>
    /// 按 modelId 查找 Prompt 每1K token 价格（Contains 匹配，更具体优先）
    /// </summary>
    public static decimal GetPromptCostPer1K(string modelId)
    {
        foreach (var (keyword, promptCost, _) in s_entries)
        {
            if (modelId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return promptCost;
            }
        }

        return DefaultPromptCostPer1K;
    }

    /// <summary>
    /// 按 modelId 查找 Completion 每1K token 价格（Contains 匹配，更具体优先）
    /// </summary>
    public static decimal GetCompletionCostPer1K(string modelId)
    {
        foreach (var (keyword, _, completionCost) in s_entries)
        {
            if (modelId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return completionCost;
            }
        }

        return DefaultCompletionCostPer1K;
    }

    /// <summary>
    /// 获取所有定价条目（供 CostTracker 初始化用）
    /// </summary>
    public static IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries() => s_entries;
}
