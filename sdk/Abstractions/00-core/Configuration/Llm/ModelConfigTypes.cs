
namespace JoinCode.Abstractions.Configuration.Llm;

public sealed class ModelConfigRoot
{
    public Dictionary<string, ModelProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelProviderConfig
{
    public string DefaultModelId { get; set; } = string.Empty;
    public string DefaultFastModelId { get; set; } = string.Empty;
    public List<ModelItemConfig> Models { get; set; } = [];
}

public sealed class ModelItemConfig
{
    public string Id { get; set; } = string.Empty;
    public string CanonicalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ContextWindow { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public ModelCapabilitiesConfig Capabilities { get; set; } = new();
    public ModelPricingConfig? Pricing { get; set; }
    public string? KnowledgeCutoff { get; set; }
}

public sealed class ModelCapabilitiesConfig
{
    public bool FastMode { get; set; } = true;
    public bool Effort { get; set; }
    public bool MaxEffort { get; set; }
}

public sealed class ModelPricingConfig
{
    public decimal PromptCostPer1K { get; set; }
    public decimal CompletionCostPer1K { get; set; }
}
