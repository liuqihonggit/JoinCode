namespace Core.Tests.Prompts;

public sealed class ToolIdleReminderServiceTests
{
    private static ToolIdleReminderConfig CreateConfig(
        string toolName = "test_tool",
        int turnsSinceUse = 3,
        int turnsBetweenReminders = 3,
        string reminderMessage = "Test reminder",
        Func<CancellationToken, ValueTask<string>>? stateProvider = null)
    {
        return new ToolIdleReminderConfig(toolName, turnsSinceUse, turnsBetweenReminders, reminderMessage, stateProvider);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_NoTurns_ReturnsEmpty()
    {
        var service = new ToolIdleReminderService([CreateConfig()]);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_BelowThreshold_ReturnsEmpty()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 3)]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_ReachesThreshold_ReturnsReminder()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 3, turnsBetweenReminders: 1)]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Single(results);
        Assert.Equal("test_tool", results[0].ToolName);
        Assert.Equal("Test reminder", results[0].Message);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_ToolUsed_ResetsCounter()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 3, turnsBetweenReminders: 1)]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn("test_tool");

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_ReminderThrottle_PreventsFrequentReminders()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 2, turnsBetweenReminders: 5)]);

        for (int i = 0; i < 3; i++) service.RecordAssistantTurn(null);

        var first = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);
        Assert.Single(first);

        service.RecordAssistantTurn(null);

        var second = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);
        Assert.Empty(second);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_AfterThrottlePeriod_GeneratesAgain()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 2, turnsBetweenReminders: 3)]);

        for (int i = 0; i < 3; i++) service.RecordAssistantTurn(null);

        var first = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);
        Assert.Single(first);

        for (int i = 0; i < 4; i++) service.RecordAssistantTurn(null);

        var second = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);
        Assert.Single(second);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_MultipleTools_IndependentTracking()
    {
        var configs = new[]
        {
            CreateConfig(toolName: "tool_a", turnsSinceUse: 2, turnsBetweenReminders: 1),
            CreateConfig(toolName: "tool_b", turnsSinceUse: 4, turnsBetweenReminders: 1),
        };
        var service = new ToolIdleReminderService(configs);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Single(results);
        Assert.Equal("tool_a", results[0].ToolName);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_WithStateProvider_AppendsState()
    {
        var config = CreateConfig(
            turnsSinceUse: 2,
            turnsBetweenReminders: 1,
            stateProvider: _ => new ValueTask<string>("Current todos:\n1. [in_progress] Task A"));

        var service = new ToolIdleReminderService([config]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Single(results);
        Assert.Contains("Test reminder", results[0].Message);
        Assert.Contains("Current todos:", results[0].Message);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_StateProviderThrows_SkipsState()
    {
        var config = CreateConfig(
            turnsSinceUse: 2,
            turnsBetweenReminders: 1,
            stateProvider: _ => throw new InvalidOperationException("State unavailable"));

        var service = new ToolIdleReminderService([config]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Single(results);
        Assert.Equal("Test reminder", results[0].Message);
    }

    [Fact]
    public async Task Reset_ClearsAllCounters()
    {
        var service = new ToolIdleReminderService([CreateConfig(turnsSinceUse: 2, turnsBetweenReminders: 1)]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);
        service.Reset();

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);
        Assert.Empty(results);
    }

    [Fact]
    public async Task CheckAndGenerateRemindersAsync_ToolUsedInDifferentCase_ResetsCounter()
    {
        var service = new ToolIdleReminderService([CreateConfig(toolName: TodoToolName.TodoWrite.ToValue(), turnsSinceUse: 3, turnsBetweenReminders: 1)]);

        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(null);
        service.RecordAssistantTurn(TodoToolName.TodoWrite.ToValue());

        var results = await service.CheckAndGenerateRemindersAsync().ConfigureAwait(true);

        Assert.Empty(results);
    }
}
