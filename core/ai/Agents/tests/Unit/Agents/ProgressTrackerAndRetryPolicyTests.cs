namespace Agents.Tests;

public class ProgressTrackerTests
{
    [Fact]
    public void RecordToolUse_IncrementsToolUseCount()
    {
        var tracker = new ProgressTracker();
        tracker.RecordToolUse("bash");
        tracker.ToolUseCount.Should().Be(1);
    }

    [Fact]
    public void RecordToolUse_MultipleCalls_IncrementsCorrectly()
    {
        var tracker = new ProgressTracker();
        tracker.RecordToolUse("bash");
        tracker.RecordToolUse("read");
        tracker.RecordToolUse("search");
        tracker.ToolUseCount.Should().Be(3);
    }

    [Fact]
    public void RecordTokenUsage_AddsTokenCount()
    {
        var tracker = new ProgressTracker();
        tracker.RecordTokenUsage(100);
        tracker.TokenCount.Should().Be(100);
    }

    [Fact]
    public void RecordTokenUsage_MultipleCalls_Accumulates()
    {
        var tracker = new ProgressTracker();
        tracker.RecordTokenUsage(100);
        tracker.RecordTokenUsage(50);
        tracker.TokenCount.Should().Be(150);
    }

    [Fact]
    public void UpdateSummary_SetsSummary()
    {
        var tracker = new ProgressTracker();
        tracker.UpdateSummary("working on it");
        tracker.Summary.Should().Be("working on it");
    }

    [Fact]
    public void MarkNotified_FirstCall_ReturnsTrue()
    {
        var tracker = new ProgressTracker();
        tracker.MarkNotified().Should().BeTrue();
    }

    [Fact]
    public void MarkNotified_SecondCall_ReturnsFalse()
    {
        var tracker = new ProgressTracker();
        tracker.MarkNotified();
        tracker.MarkNotified().Should().BeFalse();
    }

    [Fact]
    public void Notified_AfterMarkNotified_ReturnsTrue()
    {
        var tracker = new ProgressTracker();
        tracker.MarkNotified();
        tracker.Notified.Should().BeTrue();
    }

    [Fact]
    public void ToProgress_WithActivities_ReturnsProgress()
    {
        var tracker = new ProgressTracker();
        tracker.RecordToolUse("bash", "running command");
        tracker.RecordTokenUsage(50);
        tracker.UpdateSummary("test summary");

        var progress = tracker.ToProgress();
        progress.ToolUseCount.Should().Be(1);
        progress.TokenCount.Should().Be(50);
        progress.Summary.Should().Be("test summary");
        progress.LastActivity.Should().NotBeNull();
        progress.LastActivity!.ToolName.Should().Be("bash");
    }

    [Fact]
    public void ToProgress_NoActivities_LastActivityIsNull()
    {
        var tracker = new ProgressTracker();
        var progress = tracker.ToProgress();
        progress.LastActivity.Should().BeNull();
    }

    [Fact]
    public void RecordToolUse_KeepsOnly5RecentActivities()
    {
        var tracker = new ProgressTracker();
        for (var i = 0; i < 7; i++)
        {
            tracker.RecordToolUse($"tool{i}");
        }

        var progress = tracker.ToProgress();
        progress.RecentActivities.Should().HaveCount(5);
    }

    [Fact]
    public void RecordToolUse_SearchTool_SetsIsSearchFlag()
    {
        var tracker = new ProgressTracker();
        tracker.RecordToolUse("code_search");

        var progress = tracker.ToProgress();
        progress.LastActivity!.IsSearch.Should().BeTrue();
    }

    [Fact]
    public void RecordToolUse_ReadTool_SetsIsReadFlag()
    {
        var tracker = new ProgressTracker();
        tracker.RecordToolUse("file_read");

        var progress = tracker.ToProgress();
        progress.LastActivity!.IsRead.Should().BeTrue();
    }
}

public class RetryPolicyTests
{
    [Fact]
    public void Default_HasMaxRetries3()
    {
        RetryPolicy.Default.MaxRetries.Should().Be(3);
    }

    [Fact]
    public void NoRetry_HasMaxRetries0()
    {
        RetryPolicy.NoRetry.MaxRetries.Should().Be(0);
    }

    [Fact]
    public void GetDelay_ZeroRetry_ReturnsZero()
    {
        var policy = RetryPolicy.Default;
        policy.GetDelay(0).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetDelay_NegativeRetry_ReturnsZero()
    {
        var policy = RetryPolicy.Default;
        policy.GetDelay(-1).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetDelay_FirstRetry_ReturnsInitialDelay()
    {
        var policy = RetryPolicy.ExponentialBackoff(3, 1000);
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void GetDelay_SecondRetry_MultipliesByBackoff()
    {
        var policy = RetryPolicy.ExponentialBackoff(3, 1000, 2.0);
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(2000));
    }

    [Fact]
    public void GetDelay_ThirdRetry_MultipliesAgain()
    {
        var policy = RetryPolicy.ExponentialBackoff(3, 1000, 2.0);
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(4000));
    }

    [Fact]
    public void GetDelay_ClampsToMaxDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelayMs = 1000,
            BackoffMultiplier = 10.0,
            MaxDelayMs = 5000
        };

        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(5000));
    }

    [Fact]
    public void FixedDelay_CreatesPolicyWithMultiplier1()
    {
        var policy = RetryPolicy.FixedDelay(5, 500);
        policy.MaxRetries.Should().Be(5);
        policy.InitialDelayMs.Should().Be(500);
        policy.BackoffMultiplier.Should().Be(1.0);
    }

    [Fact]
    public void FixedDelay_SameDelayEveryRetry()
    {
        var policy = RetryPolicy.FixedDelay(3, 1000);
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(1000));
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(1000));
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void ExponentialBackoff_CreatesPolicyWithCorrectValues()
    {
        var policy = RetryPolicy.ExponentialBackoff(5, 500, 3.0);
        policy.MaxRetries.Should().Be(5);
        policy.InitialDelayMs.Should().Be(500);
        policy.BackoffMultiplier.Should().Be(3.0);
    }
}
