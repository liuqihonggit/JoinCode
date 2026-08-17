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
}
