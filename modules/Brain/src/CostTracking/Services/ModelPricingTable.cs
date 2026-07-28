namespace Core.CostTracking;

/// <summary>
/// 模型定价表 — 转发到 Abstractions 中的统一实现
/// </summary>
internal static class ModelPricingTable
{
    public const decimal DefaultPromptCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultPromptCostPer1K;
    public const decimal DefaultCompletionCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultCompletionCostPer1K;

    public static decimal GetPromptCostPer1K(string modelId)
        => JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.GetPromptCostPer1K(modelId);

    public static decimal GetCompletionCostPer1K(string modelId)
        => JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.GetCompletionCostPer1K(modelId);

    public static IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries()
        => JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.GetAllEntries();
}
