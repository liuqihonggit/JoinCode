
using JoinCode.Abstractions.Configuration.Llm;

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
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.005m, CompletionCostPer1K = 0.015m }
                    },
                    new ModelItemConfig
                    {
                        Id = "gpt-4o-mini",
                        DisplayName = "GPT-4o Mini",
                        ContextWindow = 128000,
                        Aliases = ["mini", "fast"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.00015m, CompletionCostPer1K = 0.0006m }
                    }
                ]
            },
            ["anthropic"] = new ModelProviderConfig
            {
                DefaultModelId = "claude-3-5-sonnet",
                DefaultFastModelId = "claude-3-5-haiku",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "claude-3-5-sonnet",
                        DisplayName = "Claude 3.5 Sonnet",
                        ContextWindow = 200000,
                        Aliases = ["sonnet", "default"],
                        Capabilities = new ModelCapabilitiesConfig { ThinkingMode = true },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.003m, CompletionCostPer1K = 0.015m }
                    },
                    new ModelItemConfig
                    {
                        Id = "claude-3-5-haiku",
                        DisplayName = "Claude 3.5 Haiku",
                        ContextWindow = 200000,
                        Aliases = ["haiku", "fast"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true },
                        Pricing = new ModelPricingConfig { PromptCostPer1K = 0.0008m, CompletionCostPer1K = 0.004m }
                    }
                ]
            }
        };
    }
}
