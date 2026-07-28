namespace Core.Tests.LLM;

using global::Services.Api;

public sealed class RateLimitTrackerTests
{
    [Fact]
    public void GetLatestSnapshot_Initial_ShouldReturnNull()
    {
        var tracker = new RateLimitTracker();

        tracker.GetLatestSnapshot().Should().BeNull();
    }

    [Fact]
    public void Update_ShouldStoreSnapshot()
    {
        var tracker = new RateLimitTracker();
        var snapshot = new RateLimitSnapshot
        {
            RequestLimit = 1000,
            RequestRemaining = 800,
            TokenLimit = 500000,
            TokenRemaining = 400000
        };

        tracker.Update(snapshot);

        var latest = tracker.GetLatestSnapshot();
        latest.Should().NotBeNull();
        latest!.RequestLimit.Should().Be(1000);
        latest.RequestRemaining.Should().Be(800);
        latest.TokenLimit.Should().Be(500000);
        latest.TokenRemaining.Should().Be(400000);
    }

    [Fact]
    public void Update_MultipleTimes_ShouldKeepLatest()
    {
        var tracker = new RateLimitTracker();

        tracker.Update(new RateLimitSnapshot { RequestLimit = 1000, RequestRemaining = 800 });
        tracker.Update(new RateLimitSnapshot { RequestLimit = 1000, RequestRemaining = 500 });

        var latest = tracker.GetLatestSnapshot();
        latest!.RequestRemaining.Should().Be(500);
    }

    [Fact]
    public void Clear_ShouldResetToNull()
    {
        var tracker = new RateLimitTracker();
        tracker.Update(new RateLimitSnapshot { RequestLimit = 1000 });

        tracker.Clear();

        tracker.GetLatestSnapshot().Should().BeNull();
    }

    [Fact]
    public void Update_NullSnapshot_ShouldThrow()
    {
        var tracker = new RateLimitTracker();

        var act = () => tracker.Update(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Snapshot_WithResetsAt_ShouldPreserveDateTime()
    {
        var tracker = new RateLimitTracker();
        var resetTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new RateLimitSnapshot
        {
            RequestLimit = 1000,
            RequestResetsAt = resetTime
        };

        tracker.Update(snapshot);

        var latest = tracker.GetLatestSnapshot();
        latest!.RequestResetsAt.Should().Be(resetTime);
    }

    [Fact]
    public void Snapshot_WithPartialData_ShouldStoreOnlyProvidedFields()
    {
        var tracker = new RateLimitTracker();
        var snapshot = new RateLimitSnapshot
        {
            RequestLimit = 500,
            RequestRemaining = 300
        };

        tracker.Update(snapshot);

        var latest = tracker.GetLatestSnapshot();
        latest!.RequestLimit.Should().Be(500);
        latest.RequestRemaining.Should().Be(300);
        latest.TokenLimit.Should().BeNull();
        latest.TokenRemaining.Should().BeNull();
    }
}
