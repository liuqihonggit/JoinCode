
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>模型配置根节点。</summary>
public sealed class ModelConfigRoot {
    /// <summary>获取或设置供应商配置字典（按供应商名称索引）。</summary>
    public Dictionary<string, ModelProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>模型供应商配置。</summary>
public sealed class ModelProviderConfig {
    /// <summary>获取或设置默认模型 ID。</summary>
    public string DefaultModelId { get; set; } = string.Empty;
    /// <summary>获取或设置默认快速模型 ID。</summary>
    public string DefaultFastModelId { get; set; } = string.Empty;
    /// <summary>获取或设置该供应商下的模型列表。</summary>
    public List<ModelItemConfig> Models { get; set; } = [];
}

/// <summary>单个模型项配置。</summary>
public sealed class ModelItemConfig {
    /// <summary>获取或设置模型 ID。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>获取或设置规范 ID。</summary>
    public string CanonicalId { get; set; } = string.Empty;
    /// <summary>获取或设置显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>获取或设置上下文窗口大小。</summary>
    public int ContextWindow { get; set; }
    /// <summary>获取或设置模型描述。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置模型别名列表。</summary>
    public List<string> Aliases { get; set; } = [];
    /// <summary>获取或设置模型能力配置。</summary>
    public ModelCapabilitiesConfig Capabilities { get; set; } = new();
    /// <summary>获取或设置模型定价配置。</summary>
    public ModelPricingConfig? Pricing { get; set; }
    /// <summary>获取或设置知识截止时间。</summary>
    public string? KnowledgeCutoff { get; set; }
}

/// <summary>模型能力配置。</summary>
public sealed class ModelCapabilitiesConfig {
    /// <summary>获取或设置是否支持快速模式。</summary>
    public bool FastMode { get; set; } = true;
    /// <summary>获取或设置是否支持努力级别。</summary>
    public bool Effort { get; set; }
    /// <summary>获取或设置是否支持最大努力级别。</summary>
    public bool MaxEffort { get; set; }
    /// <summary>获取或设置是否支持思考模式。</summary>
    public bool ThinkingMode { get; set; }

    /// <summary>
    /// 模态能力标志 — [Flags] 位标志组合，如 Text | ReadImage | ToolUse
    /// JSON 中序列化为字符串数组，如 ["text","readImage","toolUse"]
    /// </summary>
    [JsonConverter(typeof(ModelModalityKindJsonConverter))]
    public ModelModalityKind Modalities { get; set; } = ModelModalityKind.Text;
}

/// <summary>模型定价配置。</summary>
public sealed class ModelPricingConfig {
    /// <summary>获取或设置每 1K 提示 token 的费用。</summary>
    public decimal PromptCostPer1K { get; set; }
    /// <summary>获取或设置每 1K 完成 token 的费用。</summary>
    public decimal CompletionCostPer1K { get; set; }
}
