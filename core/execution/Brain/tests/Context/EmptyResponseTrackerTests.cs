namespace Core.Context;

public sealed class EmptyResponseTrackerTests
{
    [Fact]
    public void RecordEmptyResponse_IncrementsCount()
    {
        var tracker = new EmptyResponseTracker();
        tracker.ConsecutiveEmptyCount.Should().Be(0);

        tracker.RecordEmptyResponse();
        tracker.ConsecutiveEmptyCount.Should().Be(1);

        tracker.RecordEmptyResponse();
        tracker.ConsecutiveEmptyCount.Should().Be(2);
    }

    [Fact]
    public void RecordEmptyResponse_ReturnsFalseBelowThreshold()
    {
        var tracker = new EmptyResponseTracker();
        for (var i = 0; i < tracker.MaxConsecutiveEmpty; i++)
        {
            tracker.RecordEmptyResponse().Should().BeFalse();
        }
        tracker.ConsecutiveEmptyCount.Should().Be(tracker.MaxConsecutiveEmpty);
    }

    [Fact]
    public void RecordEmptyResponse_ReturnsTrueAboveThreshold()
    {
        var tracker = new EmptyResponseTracker();
        for (var i = 0; i < tracker.MaxConsecutiveEmpty; i++)
            tracker.RecordEmptyResponse();

        var exceeded = tracker.RecordEmptyResponse();
        exceeded.Should().BeTrue();
        tracker.ConsecutiveEmptyCount.Should().Be(tracker.MaxConsecutiveEmpty + 1);
    }

    [Fact]
    public void Reset_SetsCountToZero()
    {
        var tracker = new EmptyResponseTracker();
        tracker.RecordEmptyResponse();
        tracker.RecordEmptyResponse();
        tracker.ConsecutiveEmptyCount.Should().Be(2);

        tracker.Reset();
        tracker.ConsecutiveEmptyCount.Should().Be(0);
    }

    [Fact]
    public void Reset_WhenZero_IsNoOp()
    {
        var tracker = new EmptyResponseTracker();
        tracker.Reset();
        tracker.ConsecutiveEmptyCount.Should().Be(0);
    }

    [Fact]
    public void BuildInterventionPrompt_ContainsCurrentCount()
    {
        var tracker = new EmptyResponseTracker();
        tracker.RecordEmptyResponse();
        tracker.RecordEmptyResponse();

        var prompt = tracker.BuildInterventionPrompt();
        prompt.Should().Contain("第2次");
        prompt.Should().Contain($"最多{tracker.MaxConsecutiveEmpty}次");
        prompt.Should().Contain("<system-reminder>");
        prompt.Should().Contain("</system-reminder>");
    }

    [Fact]
    public void MaxConsecutiveEmpty_DefaultIs5()
    {
        var tracker = new EmptyResponseTracker();
        tracker.MaxConsecutiveEmpty.Should().Be(5);
    }

    [Fact]
    public void Reset_AfterExceeded_AllowsFreshStart()
    {
        var tracker = new EmptyResponseTracker();
        for (var i = 0; i <= tracker.MaxConsecutiveEmpty; i++)
            tracker.RecordEmptyResponse();

        tracker.Reset();
        tracker.RecordEmptyResponse().Should().BeFalse();
        tracker.ConsecutiveEmptyCount.Should().Be(1);
    }

    [Fact]
    public void CustomMaxConsecutiveEmpty_FromOptions()
    {
        var opts = CreateOptions(new LoopInterventionOptions { MaxConsecutiveEmptyResponse = 3 });
        var tracker = new EmptyResponseTracker(opts);

        tracker.MaxConsecutiveEmpty.Should().Be(3);

        tracker.RecordEmptyResponse().Should().BeFalse();
        tracker.RecordEmptyResponse().Should().BeFalse();
        tracker.RecordEmptyResponse().Should().BeFalse();
        tracker.RecordEmptyResponse().Should().BeTrue();
    }

    [Fact]
    public void BuildInterventionPrompt_UsesCustomMax()
    {
        var opts = CreateOptions(new LoopInterventionOptions { MaxConsecutiveEmptyResponse = 3 });
        var tracker = new EmptyResponseTracker(opts);
        tracker.RecordEmptyResponse();

        var prompt = tracker.BuildInterventionPrompt();
        prompt.Should().Contain("最多3次");
    }

    private static Microsoft.Extensions.Options.IOptions<LoopInterventionOptions> CreateOptions(LoopInterventionOptions value)
        => Microsoft.Extensions.Options.Options.Create(value);
}
