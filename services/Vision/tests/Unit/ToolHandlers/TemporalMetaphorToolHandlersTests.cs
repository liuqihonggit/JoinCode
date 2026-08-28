namespace Vision.Tests.ToolHandlers;

/// <summary>
/// TemporalMetaphorToolHandlers 单元测试 — 验证 M3 的 2 个 MCP 工具
/// </summary>
public sealed class TemporalMetaphorToolHandlersTests
{
    private static string CreateTestImageBase64(int width = 4, int height = 4, byte r = 100, byte g = 150, byte b = 200)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(r, g, b));
        using var ms = new MemoryStream();
        image.Save(ms, PngFormat.Instance);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string CreateFramesJson(params string[] frames)
        => JsonSerializer.Serialize(frames.ToList());

    private static Mock<IQueryService> CreateQueryServiceMock(string responseContent)
    {
        var mock = new Mock<IQueryService>();
        mock
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, responseContent) });
        return mock;
    }

    [Fact]
    public async Task TemporalAggregate_ValidFrames_ShouldReturnAnalysis()
    {
        var frame1 = CreateTestImageBase64(r: 100);
        var frame2 = CreateTestImageBase64(r: 200);
        var framesJson = CreateFramesJson(frame1, frame2);
        var mock = CreateQueryServiceMock("物体从左侧移动到右侧");
        var handlers = new TemporalMetaphorToolHandlers(mock.Object);

        var result = await handlers.TemporalAggregateAsync(framesJson);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("2 帧");
        result.Content[0].Text.Should().Contain("移动到右侧");
    }

    [Fact]
    public async Task TemporalAggregate_EmptyFramesJson_ShouldReturnError()
    {
        var mock = new Mock<IQueryService>();
        var handlers = new TemporalMetaphorToolHandlers(mock.Object);

        var result = await handlers.TemporalAggregateAsync("");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS300]");
    }

    [Fact]
    public async Task TemporalAggregate_TooManyFrames_ShouldReturnError()
    {
        var frame = CreateTestImageBase64();
        var frames = new string[11].Select(_ => frame).ToList();
        var framesJson = JsonSerializer.Serialize(frames);
        var mock = new Mock<IQueryService>();
        var handlers = new TemporalMetaphorToolHandlers(mock.Object);

        var result = await handlers.TemporalAggregateAsync(framesJson);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS302]");
    }

    [Fact]
    public async Task TemporalStableContour_ValidFrames_ShouldReturnMaskImage()
    {
        var frame1 = CreateTestImageBase64(r: 100);
        var frame2 = CreateTestImageBase64(r: 100);
        var framesJson = CreateFramesJson(frame1, frame2);
        var handlers = new TemporalMetaphorToolHandlers(new Mock<IQueryService>().Object);

        var result = await handlers.TemporalStableContourAsync(framesJson);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(2);
        result.Content[1].Type.Should().Be(ToolContentType.Image);
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TemporalStableContour_SingleFrame_ShouldReturnError()
    {
        var frame = CreateTestImageBase64();
        var framesJson = CreateFramesJson(frame);
        var handlers = new TemporalMetaphorToolHandlers(new Mock<IQueryService>().Object);

        var result = await handlers.TemporalStableContourAsync(framesJson);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS311]");
    }

    [Fact]
    public async Task TemporalStableContour_InvalidThreshold_ShouldReturnError()
    {
        var frame1 = CreateTestImageBase64();
        var frame2 = CreateTestImageBase64();
        var framesJson = CreateFramesJson(frame1, frame2);
        var handlers = new TemporalMetaphorToolHandlers(new Mock<IQueryService>().Object);

        var result = await handlers.TemporalStableContourAsync(framesJson, threshold: 300);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS313]");
    }

    [Fact]
    public async Task TemporalStableContour_DifferentFrames_ShouldReturnMaskWithUnstableRegions()
    {
        var frame1 = CreateTestImageBase64(r: 0);
        var frame2 = CreateTestImageBase64(r: 255);
        var framesJson = CreateFramesJson(frame1, frame2);
        var handlers = new TemporalMetaphorToolHandlers(new Mock<IQueryService>().Object);

        var result = await handlers.TemporalStableContourAsync(framesJson, threshold: 30);

        result.IsError.Should().BeFalse();
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }
}
