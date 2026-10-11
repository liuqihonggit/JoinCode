namespace Core.CostTracking;

/// <summary>
/// 模型定价表 — 转发到 Abstractions 中的统一实现
/// </summary>
internal sealed class ModelPricingTable {
    private readonly JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable _inner;

    /// <summary>
    /// 构造模型定价表实例
    /// </summary>
    /// <param name="modelConfigLoader">模型配置加载器</param>
    public ModelPricingTable(IModelConfigLoader modelConfigLoader) {
        _inner = new(modelConfigLoader);
    }

    /// <summary>
    /// 默认每 1K Prompt Token 成本 (USD)
    /// </summary>
    public const decimal DefaultPromptCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultPromptCostPer1K;

    /// <summary>
    /// 默认每 1K Completion Token 成本 (USD)
    /// </summary>
    public const decimal DefaultCompletionCostPer1K = JoinCode.Abstractions.LLM.Execution.Pricing.ModelPricingTable.DefaultCompletionCostPer1K;

    /// <summary>
    /// 获取指定模型的每 1K Prompt Token 成本
    /// </summary>
    /// <param name="modelId">模型标识</param>
    /// <returns>每 1K Prompt Token 成本 (USD)</returns>
    public decimal GetPromptCostPer1K(string modelId) => _inner.GetPromptCostPer1K(modelId);

    /// <summary>
    /// 获取指定模型的每 1K Completion Token 成本
    /// </summary>
    /// <param name="modelId">模型标识</param>
    /// <returns>每 1K Completion Token 成本 (USD)</returns>
    public decimal GetCompletionCostPer1K(string modelId) => _inner.GetCompletionCostPer1K(modelId);

    /// <summary>
    /// 获取所有定价表条目
    /// </summary>
    /// <returns>定价条目列表，每项包含关键词、Prompt 成本和 Completion 成本</returns>
    public IReadOnlyList<(string Keyword, decimal PromptCost, decimal CompletionCost)> GetAllEntries() => _inner.GetAllEntries();
}