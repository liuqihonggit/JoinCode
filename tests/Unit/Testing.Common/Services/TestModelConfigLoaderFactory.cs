namespace Testing.Common.Services;

/// <summary>
/// 测试用 IModelConfigLoader 工厂 — 构建含基础模型定价数据的 ModelConfigLoader 实例
/// 供 CostTracker/FallbackProviderDefinition 等需要默认定价/模型列表的测试使用
/// </summary>
public static class TestModelConfigLoaderFactory
{
    /// <summary>
    /// 创建含 openai/anthropic 基础模型定价的 loader
    /// </summary>
    public static IModelConfigLoader CreateWithDefaultPricing()
    {
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(BuildDefaultProviders());
        return loader;
    }

    private static Dictionary<string, ModelProviderConfig> BuildDefaultProviders()
    {
        return new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = new ModelProviderConfig
            {
                DefaultModelId = "gpt-4o",
                DefaultFastModelId = "gpt-4o-mini",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "gpt-4o",
                        DisplayName = "GPT-4o",
                        ContextWindow = 128000,
                        Aliases = ["4o", "default"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadPdf | ModelModalityKind.ToolUse },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.005m, CompletionCostPer1K = 0.015m }
                    },
                    new ModelItemConfig
                    {
                        Id = "gpt-4o-mini",
                        DisplayName = "GPT-4o Mini",
                        ContextWindow = 128000,
                        Aliases = ["mini", "fast"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadPdf | ModelModalityKind.ToolUse },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.00015m, CompletionCostPer1K = 0.0006m }
                    }
                ]
            },
            ["anthropic"] = new ModelProviderConfig
            {
                DefaultModelId = "claude-opus-4-7",
                DefaultFastModelId = "claude-haiku-4-5",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "claude-opus-4-7",
                        DisplayName = "Claude Opus 4.7",
                        ContextWindow = 1000000,
                        Aliases = ["opus", "default"],
                        Capabilities = new ModelCapabilitiesConfig { ThinkingMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadPdf | ModelModalityKind.Thinking | ModelModalityKind.ToolUse },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.005m, CompletionCostPer1K = 0.025m }
                    },
                    new ModelItemConfig
                    {
                        Id = "claude-haiku-4-5",
                        DisplayName = "Claude Haiku 4.5",
                        ContextWindow = 200000,
                        Aliases = ["haiku", "fast"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadPdf | ModelModalityKind.Thinking | ModelModalityKind.ToolUse },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.001m, CompletionCostPer1K = 0.005m }
                    }
                ]
            }
        };
    }
}
