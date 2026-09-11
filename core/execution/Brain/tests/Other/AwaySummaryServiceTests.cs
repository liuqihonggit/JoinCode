using Core.Summary;

namespace Brain.Other.Tests;

public sealed class AwaySummaryServiceTests
{
    [Fact]
    public async Task MarkAway_SetsIsAwayTrue()
    {
        using var sut = new AwaySummaryService();
        sut.IsAway.Should().BeFalse();

        await sut.MarkAwayAsync();

        sut.IsAway.Should().BeTrue();
        sut.AwaySince.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateSummary_WhenNotAway_ReturnsFailure()
    {
        using var sut = new AwaySummaryService();

        var result = await sut.GenerateSummaryAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task TrackEvent_WhenAway_RecordsEvent()
    {
        using var sut = new AwaySummaryService();
        await sut.MarkAwayAsync();

        await sut.TrackEventAsync(new AwayEvent
        {
            Type = AwayEventType.ToolCall,
            Description = "test event",
            Timestamp = DateTime.UtcNow,
        });

        var result = await sut.GenerateSummaryAsync();
        result.Success.Should().BeTrue();
        result.TotalEvents.Should().Be(1);
        result.ToolCallCount.Should().Be(1);
    }
}
