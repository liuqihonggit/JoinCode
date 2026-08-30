namespace Mcp.Tests;

public sealed class ModelSearchEngineTests
{
    private static ModelSearchEntry Model(string vendor, string modelId, string displayName, ModelModalityKind modalities) =>
        new(vendor, modelId, displayName, modalities);

    private static ModelSearchEngine Sut(params ModelSearchEntry[] models) => new(models);

    [Fact]
    public void Search_ThrowsOnNullQuery()
    {
        var engine = Sut();
        Assert.Throws<ArgumentNullException>(() => engine.Search(null!));
    }

    [Fact]
    public void Search_ThrowsOnEmptyQuery()
    {
        var engine = Sut();
        Assert.Throws<ArgumentException>(() => engine.Search(""));
    }

    [Fact]
    public void Search_ListGroups_ReturnsSupportedFunctionalities()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ToolUse),
            Model("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.Text | ModelModalityKind.GenerateImage));

        var result = engine.Search("list_groups");

        result.IsGroupList.Should().BeTrue();
        result.Lines.Should().Contain("readImage:图片识别");
        result.Lines.Should().Contain("generateImage:图片生成");
        result.Lines.Should().Contain("toolUse:工具使用");
        result.Lines.Should().NotContain("readVideo:视频识别", "无模型支持视频识别时不应列出");
    }

    [Fact]
    public void Search_ListGroups_EmptyModels_ReturnsEmpty()
    {
        var engine = Sut();
        var result = engine.Search("list_groups");

        result.IsGroupList.Should().BeTrue();
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Search_MapByModality_ReturnsAllModelsSupportingIt()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text | ModelModalityKind.ReadImage),
            Model("anthropic", "claude-3-opus", "Claude 3 Opus", ModelModalityKind.Text | ModelModalityKind.ReadImage | ModelModalityKind.ReadPdf),
            Model("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.GenerateImage));

        var result = engine.Search("map[readImage]");

        result.IsModelList.Should().BeTrue();
        result.Lines.Should().HaveCount(2);
        result.Lines.Should().Contain("anthropic/claude-3-opus (Claude 3 Opus)");
        result.Lines.Should().Contain("openai/gpt-4o (GPT-4o)");
        result.Lines.Should().NotContain(l => l.Contains("dall-e-3"), "dall-e-3 不支持 readImage");
    }

    [Fact]
    public void Search_MapByModalityAndVendor_FiltersByVendor()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.ReadImage),
            Model("anthropic", "claude-3-opus", "Claude 3 Opus", ModelModalityKind.ReadImage));

        var result = engine.Search("map[readImage][openai]");

        result.IsModelList.Should().BeTrue();
        result.Lines.Should().ContainSingle();
        result.Lines[0].Should().Be("openai/gpt-4o (GPT-4o)");
    }

    [Fact]
    public void Search_MapByModality_NoModels_ReturnsEmpty()
    {
        var engine = Sut(Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text));

        var result = engine.Search("map[generateImage]");

        result.IsModelList.Should().BeTrue();
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Search_MapInvalidModalityKey_ReturnsEmptyModelList()
    {
        var engine = Sut(Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.ReadImage));

        var result = engine.Search("map[nonexistent]");

        result.IsModelList.Should().BeTrue();
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Search_Keyword_MatchesModelId()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text),
            Model("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.GenerateImage));

        var result = engine.Search("dall");

        result.Lines.Should().ContainSingle();
        result.Lines[0].Should().Contain("dall-e-3");
    }

    [Fact]
    public void Search_Keyword_MatchesDisplayName()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text),
            Model("anthropic", "claude-3-opus", "Claude 3 Opus", ModelModalityKind.Text));

        var result = engine.Search("Claude");

        result.Lines.Should().ContainSingle();
        result.Lines[0].Should().Contain("claude-3-opus");
    }

    [Fact]
    public void Search_Keyword_MatchesVendor()
    {
        var engine = Sut(
            Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text),
            Model("anthropic", "claude-3-opus", "Claude 3 Opus", ModelModalityKind.Text));

        var result = engine.Search("anthropic");

        result.Lines.Should().ContainSingle();
        result.Lines[0].Should().Contain("claude-3-opus");
    }

    [Fact]
    public void Search_MaxResults_LimitsOutput()
    {
        var models = Enumerable.Range(0, 30)
            .Select(i => Model("openai", $"model{i}", $"Model {i}", ModelModalityKind.Text))
            .ToArray();
        var engine = Sut(models);

        var result = engine.Search("model", maxResults: 5);

        result.Lines.Should().HaveCount(5);
    }

    [Fact]
    public void Search_MapByModalityKey_IsCaseInsensitive()
    {
        var engine = Sut(Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.ReadImage));

        var result = engine.Search("map[READIMAGE]");

        result.IsModelList.Should().BeTrue();
        result.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Search_ListGroups_OnlyListsFunctionalitiesWithModels()
    {
        var engine = Sut(Model("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text | ModelModalityKind.ToolUse));

        var result = engine.Search("list_groups");

        result.Lines.Should().ContainSingle();
        result.Lines[0].Should().Be("toolUse:工具使用");
    }
}
