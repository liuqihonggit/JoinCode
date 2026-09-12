namespace JoinCode.Abstractions.LLM.Execution.Pricing;

/// <summary>
/// 模型定价表 — 从 IModelConfigLoader 读取定价数据
/// </summary>
public sealed class ModelPricingTable
{
    private readonly IModelConfigLoader _modelConfigLoader;

    public ModelPricingTable(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    public const decimal DefaultPromptCostPer1K = 0.01m;
    public const decimal DefaultCompletionCostPer1K = 0.03m;

    public decimal GetPromptCostPer1K(string modelId)
    {
        var pricing = FindPricing(modelId);
        return pricing?.PromptCostPer1K ?? DefaultPromptCostPer1K;
    }

    public decimal GetCompletionCostPer1K(string modelId)
    {
        var pricing = FindPricing(modelId);
        return pricing?.CompletionCostPer1K ?? DefaultCompletionCostPer1K;
    }

    public IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries()
    {
        var entries = new List<(string Keyword, decimal PromptCost, decimal CompletionCost)>();
        foreach (var model in _modelConfigLoader.Config.Providers.SelectMany(p => p.Value.Models))
        {
            if (model.Pricing is not null)
            {
                entries.Add((model.Id, model.Pricing.PromptCostPer1K, model.Pricing.CompletionCostPer1K));
            }
        }
        return entries;
    }

    private Configuration.Llm.ModelPricingConfig? FindPricing(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        foreach (var provider in _modelConfigLoader.Config.Providers)
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
