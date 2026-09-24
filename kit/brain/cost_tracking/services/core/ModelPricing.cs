namespace Core.CostTracking;

/// <summary>
/// 模型定价 — 封装自定义模型成本字典与默认定价表
/// 从 CostTracker 提取,统一管理模型定价的设置、查询、成本计算
/// </summary>
internal sealed class ModelPricing {
    private ImmutableDictionary<string, ModelCostInfo> _modelCosts = ImmutableDictionary<string, ModelCostInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private readonly ModelPricingTable _pricingTable;
    private readonly ILogger? _logger;

    /// <summary>构造 ModelPricing — 加载默认定价表</summary>
    public ModelPricing(ILogger? logger = null, IModelConfigLoader? modelConfigLoader = null) {
        _logger = logger;
        _pricingTable = new ModelPricingTable(modelConfigLoader ?? new ModelConfigLoader());
        LoadDefaults();
    }

    /// <summary>设置模型定价 — 覆盖默认定价表</summary>
    public void Set(string model, decimal promptCostPer1K, decimal completionCostPer1K) {
        var entry = new ModelCostInfo {
            Model = model,
            PromptCostPer1KTokens = promptCostPer1K,
            CompletionCostPer1KTokens = completionCostPer1K
        };
        while (true) {
            var current = _modelCosts;
            var updated = current.SetItem(model, entry);
            if (Interlocked.CompareExchange(ref _modelCosts, updated, current) == current) break;
        }

        _logger?.LogInformation("[CostTracker] 设置模型定价 - {Model}: Prompt ${PromptCost}/1K, Completion ${CompletionCost}/1K",
            model, promptCostPer1K, completionCostPer1K);
    }

    /// <summary>获取指定模型的定价信息;若未配置则返回 null</summary>
    public ModelCostInfo? Get(string model) => _modelCosts.GetValueOrDefault(model);

    /// <summary>尝试获取模型定价;存在则返回 true</summary>
    public bool TryGetCost(string model, out ModelCostInfo costInfo) => _modelCosts.TryGetValue(model, out costInfo!);

    /// <summary>是否包含指定模型的自定义定价</summary>
    public bool Contains(string model) => _modelCosts.ContainsKey(model);

    /// <summary>获取所有已配置模型定价的只读字典快照</summary>
    public IReadOnlyDictionary<string, ModelCostInfo> GetAll() => _modelCosts.ToFrozenDictionary();

    /// <summary>计算单次调用的成本 — 含 prompt/completion/cacheCreation/cacheRead</summary>
    public decimal CalculateCost(string model, int promptTokens, int completionTokens, int cacheCreationTokens = 0, int cacheReadTokens = 0) {
        if (!_modelCosts.TryGetValue(model, out var costInfo)) {
            costInfo = GetDefaultCostInfo(model);
        }

        var promptCost = (promptTokens / 1000m) * costInfo.PromptCostPer1KTokens;
        var completionCost = (completionTokens / 1000m) * costInfo.CompletionCostPer1KTokens;
        var cacheCreationCost = (cacheCreationTokens / 1000m) * costInfo.PromptCostPer1KTokens * 1.25m;
        var cacheReadCost = (cacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens * 0.1m;

        return promptCost + completionCost + cacheCreationCost + cacheReadCost;
    }

    /// <summary>加载默认模型定价 — 从定价表注入到自定义字典</summary>
    private void LoadDefaults() {
        foreach (var (keyword, promptCost, completionCost) in _pricingTable.GetAllEntries()) {
            Set(keyword, promptCost, completionCost);
        }
    }

    /// <summary>获取默认成本信息 — 按关键字模糊匹配,未命中则用全局默认</summary>
    public ModelCostInfo GetDefaultCostInfo(string model) {
        foreach (var kvp in _modelCosts) {
            if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) {
                return kvp.Value;
            }
        }

        return new ModelCostInfo {
            Model = model,
            PromptCostPer1KTokens = ModelPricingTable.DefaultPromptCostPer1K,
            CompletionCostPer1KTokens = ModelPricingTable.DefaultCompletionCostPer1K
        };
    }
}