namespace Abs.Tests.LLM;

/// <summary>
/// ModelConfigLoader 查询方法单元测试 — 验证 FindModel/ResolveAlias/FindProviderByModelId/FindModelByModelId/GetCanonicalName
/// 的精确查找、模糊查找、provider 隔离与 null 回退行为
/// </summary>
public class ModelConfigLoaderQueryTests {
    private static ModelConfigLoader CreateLoader() {
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase) {
            ["openai"] = new ModelProviderConfig {
                DefaultModelId = "gpt-4o",
                Models = [
                    new ModelItemConfig {
                        Id = "gpt-4o",
                        DisplayName = "GPT-4o",
                        ContextWindow = 128000,
                        Aliases = ["4o", "gpt4o"],
                        CanonicalId = "gpt-4o-canonical"
                    },
                    new ModelItemConfig {
                        Id = "gpt-4o-mini",
                        DisplayName = "GPT-4o Mini",
                        ContextWindow = 128000,
                        Aliases = ["mini"]
                    }
                ]
            },
            ["anthropic"] = new ModelProviderConfig {
                DefaultModelId = "claude-3-opus",
                Models = [
                    new ModelItemConfig {
                        Id = "claude-3-opus",
                        DisplayName = "Claude 3 Opus",
                        ContextWindow = 200000,
                        Aliases = ["opus"]
                    }
                ]
            }
        });
        return loader;
    }

    #region FindModel

    [Fact]
    public void FindModel_ExactMatch_ReturnsConfig() {
        var loader = CreateLoader();
        var model = loader.FindModel("openai", "gpt-4o");
        model.Should().NotBeNull();
        model!.Id.Should().Be("gpt-4o");
        model.DisplayName.Should().Be("GPT-4o");
    }

    [Fact]
    public void FindModel_CaseInsensitive_ReturnsConfig() {
        var loader = CreateLoader();
        loader.FindModel("OpenAI", "GPT-4O").Should().NotBeNull();
    }

    [Fact]
    public void FindModel_UnknownProvider_ReturnsNull() {
        var loader = CreateLoader();
        loader.FindModel("unknown", "gpt-4o").Should().BeNull();
    }

    [Fact]
    public void FindModel_UnknownModel_ReturnsNull() {
        var loader = CreateLoader();
        loader.FindModel("openai", "unknown-model").Should().BeNull();
    }

    [Fact]
    public void FindModel_DoesNotCrossProvider() {
        var loader = CreateLoader();
        loader.FindModel("anthropic", "gpt-4o").Should().BeNull();
    }

    #endregion

    #region ResolveAlias

    [Fact]
    public void ResolveAlias_KnownAlias_ReturnsModelId() {
        var loader = CreateLoader();
        loader.ResolveAlias("openai", "4o").Should().Be("gpt-4o");
        loader.ResolveAlias("openai", "gpt4o").Should().Be("gpt-4o");
        loader.ResolveAlias("openai", "mini").Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void ResolveAlias_UnknownAlias_ReturnsNull() {
        var loader = CreateLoader();
        loader.ResolveAlias("openai", "nonexistent").Should().BeNull();
    }

    [Fact]
    public void ResolveAlias_DoesNotCrossProvider() {
        var loader = CreateLoader();
        loader.ResolveAlias("anthropic", "4o").Should().BeNull();
        loader.ResolveAlias("anthropic", "opus").Should().Be("claude-3-opus");
    }

    [Fact]
    public void ResolveAlias_UnknownProvider_ReturnsNull() {
        var loader = CreateLoader();
        loader.ResolveAlias("unknown", "4o").Should().BeNull();
    }

    #endregion

    #region FindProviderByModelId

    [Fact]
    public void FindProviderByModelId_KnownModel_ReturnsProvider() {
        var loader = CreateLoader();
        loader.FindProviderByModelId("gpt-4o").Should().Be("openai");
        loader.FindProviderByModelId("claude-3-opus").Should().Be("anthropic");
    }

    [Fact]
    public void FindProviderByModelId_UnknownModel_ReturnsNull() {
        var loader = CreateLoader();
        loader.FindProviderByModelId("unknown-model").Should().BeNull();
    }

    #endregion

    #region FindModelByModelId

    [Fact]
    public void FindModelByModelId_ExactMatch_ReturnsConfig() {
        var loader = CreateLoader();
        var model = loader.FindModelByModelId("gpt-4o");
        model.Should().NotBeNull();
        model!.Id.Should().Be("gpt-4o");
    }

    [Fact]
    public void FindModelByModelId_FuzzyMatch_ReturnsConfig() {
        var loader = CreateLoader();
        var model = loader.FindModelByModelId("prefix-gpt-4o-suffix");
        model.Should().NotBeNull();
        model!.Id.Should().Be("gpt-4o");
    }

    [Fact]
    public void FindModelByModelId_Unknown_ReturnsNull() {
        var loader = CreateLoader();
        loader.FindModelByModelId("totally-unknown").Should().BeNull();
    }

    #endregion

    #region GetCanonicalName

    [Fact]
    public void GetCanonicalName_ExactMatch_ReturnsCanonicalId() {
        var loader = CreateLoader();
        loader.GetCanonicalName("gpt-4o").Should().Be("gpt-4o-canonical");
    }

    [Fact]
    public void GetCanonicalName_FuzzyMatch_ReturnsId() {
        var loader = CreateLoader();
        var result = loader.GetCanonicalName("prefix-claude-3-opus-suffix");
        result.Should().Be("claude-3-opus");
    }

    [Fact]
    public void GetCanonicalName_Unknown_ReturnsInput() {
        var loader = CreateLoader();
        loader.GetCanonicalName("totally-unknown").Should().Be("totally-unknown");
    }

    #endregion
}
