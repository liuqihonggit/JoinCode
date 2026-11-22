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
    /// <returns>定价条目列表(关键词 + Prompt 成本 + Completion 成本)</returns>
    public IReadOnlyList<ModelPricingEntry> GetAllEntries() {
        var entries = new List<ModelPricingEntry>();
        foreach (var model in _modelConfigLoader.Config.Providers.SelectMany(p => p.Value.Models)) {
            if (model.Pricing is not null) {
                entries.Add(new ModelPricingEntry(model.Id, model.Pricing.PromptCostPer1K, model.Pricing.CompletionCostPer1K));
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

/// <summary>
/// 模型定价条目 — 关键词(模型 ID)与每 1K Token 成本
/// </summary>
/// <param name="Keyword">匹配关键词(模型 ID)</param>
/// <param name="PromptCost">每 1K Prompt Token 成本(USD)</param>
/// <param name="CompletionCost">每 1K Completion Token 成本(USD)</param>
public sealed record ModelPricingEntry(string Keyword, decimal PromptCost, decimal CompletionCost);