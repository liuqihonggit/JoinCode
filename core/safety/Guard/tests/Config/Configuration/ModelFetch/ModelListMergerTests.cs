namespace Core.Configuration.ModelFetch.Tests;

/// <summary>
/// 模型列表智能合并器单元测试
/// </summary>
public class ModelListMergerTests
{
    [Fact]
    public void Merge_RemoteHasNewModel_AddsWithGeneratedDisplayName()
    {
        var local = new List<ModelItemConfig>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o", ContextWindow = 128000 }
        };
        var remote = new List<string> { "gpt-4o", "gpt-5" };

        var result = ModelListMerger.Merge(local, remote);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("gpt-4o");
        result[0].DisplayName.Should().Be("GPT-4o");
        result[0].ContextWindow.Should().Be(128000);
        result[1].Id.Should().Be("gpt-5");
        result[1].DisplayName.Should().Be("Gpt 5");
    }

    [Fact]
    public void Merge_RemoteMissingLocalModel_RemovesIt()
    {
        var local = new List<ModelItemConfig>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o" },
            new() { Id = "old-model", DisplayName = "Old" }
        };
        var remote = new List<string> { "gpt-4o" };

        var result = ModelListMerger.Merge(local, remote);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("gpt-4o");
    }

    [Fact]
    public void Merge_BothHave_PreservesLocalMetadata()
    {
        var local = new List<ModelItemConfig>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o", ContextWindow = 128000, Description = "多模态模型" }
        };
        var remote = new List<string> { "gpt-4o" };

        var result = ModelListMerger.Merge(local, remote);

        result.Should().HaveCount(1);
        result[0].DisplayName.Should().Be("GPT-4o");
        result[0].ContextWindow.Should().Be(128000);
        result[0].Description.Should().Be("多模态模型");
    }

    [Fact]
    public void Merge_EmptyRemote_ReturnsLocal()
    {
        var local = new List<ModelItemConfig>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o" }
        };

        var result = ModelListMerger.Merge(local, Array.Empty<string>());

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("gpt-4o");
    }

    [Fact]
    public void Merge_NullLocal_AllNewWithGeneratedNames()
    {
        var remote = new List<string> { "gpt-4o", "gpt-5" };

        var result = ModelListMerger.Merge(null, remote);

        result.Should().HaveCount(2);
        result[0].DisplayName.Should().Be("Gpt 4o");
        result[1].DisplayName.Should().Be("Gpt 5");
    }

    [Fact]
    public void Merge_CaseInsensitiveMatch_PreservesLocal()
    {
        var local = new List<ModelItemConfig>
        {
            new() { Id = "GPT-4O", DisplayName = "GPT-4o" }
        };
        var remote = new List<string> { "gpt-4o" };

        var result = ModelListMerger.Merge(local, remote);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("GPT-4O");
        result[0].DisplayName.Should().Be("GPT-4o");
    }

    /// <summary>
    /// 远程新模型不从 ID 推断模态 — 配置大于代码，模态能力由用户在 settings.json 显式配置
    /// </summary>
    [Theory]
    [InlineData("deepseek-v4-flash-vision-exp", "含 vision 不应推断 ReadImage")]
    [InlineData("deepseek-v4-pro", "含 pro 不应推断 Thinking")]
    [InlineData("deepseek-reasoner", "含 reasoner 不应推断 Thinking")]
    public void Merge_NewModel_DoesNotInferModalitiesFromId(string remoteId, string reason)
    {
        var remote = new List<string> { remoteId };

        var result = ModelListMerger.Merge(null, remote);

        result.Should().HaveCount(1);
        result[0].Capabilities.Modalities.Should().NotHaveFlag(ModelModalityKind.ReadImage, reason);
        result[0].Capabilities.Modalities.Should().NotHaveFlag(ModelModalityKind.ReadGif, reason);
        result[0].Capabilities.Modalities.Should().NotHaveFlag(ModelModalityKind.Thinking, reason);
    }

    [Fact]
    public void Merge_ExistingModelWithManualModalities_PreservesLocalModalities()
    {
        var local = new List<ModelItemConfig>
        {
            new()
            {
                Id = "deepseek-v4-flash-vision-exp",
                DisplayName = "DeepSeek V4 Flash Vision",
                Capabilities = new ModelCapabilitiesConfig
                {
                    Modalities = ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse,
                    FastMode = true,
                }
            }
        };
        var remote = new List<string> { "deepseek-v4-flash-vision-exp" };

        var result = ModelListMerger.Merge(local, remote);

        result.Should().HaveCount(1);
        result[0].Capabilities.Modalities.Should().Be(ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse, "本地已有模态应保留，不被推断覆盖");
    }
}
