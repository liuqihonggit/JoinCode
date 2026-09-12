namespace Core.CostTracking;

/// <summary>
/// 模型定价表 — 转发到 Abstractions 中的统一实现
/// </summary>
internal sealed class ModelPricingTable
{
    private readonly JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable _inner;

    public ModelPricingTable(IModelConfigLoader modelConfigLoader)
    {
        _inner = new(modelConfigLoader);
    }

    public const decimal DefaultPromptCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultPromptCostPer1K;
    public const decimal DefaultCompletionCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultCompletionCostPer1K;

    public decimal GetPromptCostPer1K(string modelId) => _inner.GetPromptCostPer1K(modelId);
    public decimal GetCompletionCostPer1K(string modelId) => _inner.GetCompletionCostPer1K(modelId);
    public IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries() => _inner.GetAllEntries();
}
