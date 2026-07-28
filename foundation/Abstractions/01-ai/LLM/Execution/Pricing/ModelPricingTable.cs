namespace JoinCode.Abstractions.LLM.Execution.Pricing;

/// <summary>
/// 模型定价表 — 从 models.json 读取定价数据，消除硬编码
/// </summary>
public static class ModelPricingTable
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
    /// 按 modelId 查找 Prompt 每1K token 价格
    /// </summary>
    public static decimal GetPromptCostPer1K(string modelId)
    {
        var pricing = FindPricing(modelId);
        return pricing?.PromptCostPer1K ?? DefaultPromptCostPer1K;
    }

    /// <summary>
    /// 按 modelId 查找 Completion 每1K token 价格
    /// </summary>
    public static decimal GetCompletionCostPer1K(string modelId)
    {
        var pricing = FindPricing(modelId);
        return pricing?.CompletionCostPer1K ?? DefaultCompletionCostPer1K;
    }

    /// <summary>
    /// 获取所有定价条目（供 CostTracker 初始化用）
    /// </summary>
    public static IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries()
    {
        var entries = new List<(string Keyword, decimal PromptCost, decimal CompletionCost)>();
        foreach (var model in Configuration.Llm.ModelConfigLoader.Config.Providers.SelectMany(p => p.Value.Models))
        {
            if (model.Pricing is not null)
            {
                entries.Add((model.Id, model.Pricing.PromptCostPer1K, model.Pricing.CompletionCostPer1K));
            }
        }
        return entries;
    }

    private static Configuration.Llm.ModelPricingConfig? FindPricing(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        foreach (var provider in Configuration.Llm.ModelConfigLoader.Config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                if (model.Pricing is not null && lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    return model.Pricing;
                }
            }
        }
        return null;
    }
}
