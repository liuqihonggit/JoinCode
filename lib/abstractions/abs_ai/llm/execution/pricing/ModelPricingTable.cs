namespace JoinCode.Abstractions.LLM.Execution.Pricing;

/// <summary>
/// 模型定价表 — 从 IModelConfigLoader 读取定价数据
/// </summary>
public sealed class ModelPricingTable {
    private readonly IModelConfigLoader _modelConfigLoader;

    /// <summary>构造模型定价表。</summary>
    /// <param name="modelConfigLoader">模型配置加载器。</param>
    public ModelPricingTable(IModelConfigLoader modelConfigLoader) {
        _modelConfigLoader = modelConfigLoader;
    }

    public const decimal DefaultPromptCostPer1K = 0.01m;
    public const decimal DefaultCompletionCostPer1K = 0.03m;

    /// <summary>获取指定模型的提示词每 1K 令牌成本。</summary>
    /// <param name="modelId">模型标识。</param>
    public decimal GetPromptCostPer1K(string modelId) {
        var pricing = FindPricing(modelId);
        return pricing?.PromptCostPer1K ?? DefaultPromptCostPer1K;
    }

    /// <summary>获取指定模型的补全每 1K 令牌成本。</summary>
    /// <param name="modelId">模型标识。</param>
    public decimal GetCompletionCostPer1K(string modelId) {
        var pricing = FindPricing(modelId);
        return pricing?.CompletionCostPer1K ?? DefaultCompletionCostPer1K;
    }

    /// <summary>获取所有定价条目。</summary>
    public IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries() {
        var entries = new List<(string Keyword, decimal PromptCost, decimal CompletionCost)>();
        foreach (var model in _modelConfigLoader.Config.Providers.SelectMany(p => p.Value.Models)) {
            if (model.Pricing is not null) {
                entries.Add((model.Id, model.Pricing.PromptCostPer1K, model.Pricing.CompletionCostPer1K));
            }
        }
        return entries;
    }

    private Configuration.Llm.ModelPricingConfig? FindPricing(string modelId) {
        var lower = modelId.ToLowerInvariant();
        foreach (var provider in _modelConfigLoader.Config.Providers) {
            foreach (var model in provider.Value.Models) {
                if (model.Pricing is not null && lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal)) {
                    return model.Pricing;
                }
            }
        }
        return null;
    }
}