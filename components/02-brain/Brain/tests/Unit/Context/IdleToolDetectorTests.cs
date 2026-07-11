namespace Core.Context.Tests;

public sealed class IdleToolDetectorTests
{
    [Fact]
    public void OnLlmResponse_WithTool_Should_Reset_Counter()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 3);

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: true);

        Assert.Equal(0, detector.ConsecutiveNoToolRounds);
        Assert.False(detector.ShouldInjectReminder());
    }

    [Fact]
    public void OnLlmResponse_WithoutTool_Should_Increment_Counter()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 3);

        detector.OnLlmResponse(usedTool: false);

        Assert.Equal(1, detector.ConsecutiveNoToolRounds);
    }

    [Fact]
    public void ShouldInjectReminder_Should_Return_True_When_Threshold_Reached()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 2);

        detector.OnLlmResponse(usedTool: false);
        Assert.False(detector.ShouldInjectReminder());

        detector.OnLlmResponse(usedTool: false);
        Assert.True(detector.ShouldInjectReminder());
    }

    [Fact]
    public void ShouldInjectReminder_Should_Return_False_When_Below_Threshold()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 5);

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);

        Assert.False(detector.ShouldInjectReminder());
    }

    [Fact]
    public void Reset_Should_Clear_Counter()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 2);

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);
        Assert.True(detector.ShouldInjectReminder());

        detector.Reset();

        Assert.Equal(0, detector.ConsecutiveNoToolRounds);
        Assert.False(detector.ShouldInjectReminder());
    }

    [Fact]
    public void GetReminderMessage_Should_Contain_Round_Count()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 2);

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);

        var message = detector.GetReminderMessage();
        Assert.Contains("2", message);
        Assert.Contains("工具", message);
    }

    [Fact]
    public void GetReminderMessage_WithCustomContent_Should_Use_Template()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 2, reminderContent: "Custom: {0} rounds idle");

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);

        var message = detector.GetReminderMessage();
        Assert.Equal("Custom: 2 rounds idle", message);
    }

    [Fact]
    public void Constructor_Should_Throw_When_MaxIdleRounds_Less_Than_One()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleToolDetector(maxIdleRounds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleToolDetector(maxIdleRounds: -1));
    }

    [Fact]
    public void Tool_Usage_Should_Break_Consecutive_Streak()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 3);

        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: false);
        detector.OnLlmResponse(usedTool: true);
        detector.OnLlmResponse(usedTool: false);

        Assert.Equal(1, detector.ConsecutiveNoToolRounds);
        Assert.False(detector.ShouldInjectReminder());
    }

    [Fact]
    public void MaxIdleRounds_Should_Return_Configured_Value()
    {
        var detector = new IdleToolDetector(maxIdleRounds: 5);
        Assert.Equal(5, detector.MaxIdleRounds);
    }
}
