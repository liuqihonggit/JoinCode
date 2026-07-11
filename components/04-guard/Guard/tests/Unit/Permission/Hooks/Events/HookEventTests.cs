
namespace Core.Tests.Hooks.Events;

/// <summary>
/// HookEvent 枚举测试
/// </summary>
public class HookEventTests
{
    [Theory]
    [InlineData(HookEvent.PreToolUse, "tool_name")]
    [InlineData(HookEvent.PostToolUse, "tool_name")]
    [InlineData(HookEvent.PostToolUseFailure, "tool_name")]
    [InlineData(HookEvent.PermissionDenied, "tool_name")]
    [InlineData(HookEvent.PermissionRequest, "tool_name")]
    [InlineData(HookEvent.Notification, "notification_type")]
    [InlineData(HookEvent.SessionStart, "source")]
    [InlineData(HookEvent.StopFailure, "error")]
    [InlineData(HookEvent.SubagentStart, "agent_type")]
    [InlineData(HookEvent.SubagentStop, "agent_type")]
    [InlineData(HookEvent.PreCompact, "trigger")]
    [InlineData(HookEvent.PostCompact, "trigger")]
    [InlineData(HookEvent.SessionEnd, "reason")]
    [InlineData(HookEvent.Setup, "trigger")]
    [InlineData(HookEvent.Elicitation, "mcp_server_name")]
    [InlineData(HookEvent.ElicitationResult, "mcp_server_name")]
    [InlineData(HookEvent.ConfigChange, "source")]
    [InlineData(HookEvent.InstructionsLoaded, "load_reason")]
    public void GetMatcherField_ShouldReturnCorrectField(HookEvent hookEvent, string expectedField)
    {
        // Act
        var result = hookEvent.GetMatcherField();

        // Assert
        result.Should().Be(expectedField);
    }

    [Theory]
    [InlineData(HookEvent.TaskCreated)]
    [InlineData(HookEvent.TaskCompleted)]
    [InlineData(HookEvent.TeammateIdle)]
    public void GetMatcherField_EventsWithoutMatcher_ShouldReturnNull(HookEvent hookEvent)
    {
        // Act
        var result = hookEvent.GetMatcherField();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(HookEvent.PreToolUse, true)]
    [InlineData(HookEvent.PostToolUse, true)]
    [InlineData(HookEvent.PermissionRequest, true)]
    [InlineData(HookEvent.TaskCreated, false)]
    [InlineData(HookEvent.TaskCompleted, false)]
    public void RequiresMatcher_ShouldReturnCorrectValue(HookEvent hookEvent, bool expected)
    {
        // Act
        var result = hookEvent.RequiresMatcher();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(HookEvent.PreToolUse, true)]
    [InlineData(HookEvent.UserPromptSubmit, true)]
    [InlineData(HookEvent.PreCompact, true)]
    [InlineData(HookEvent.PermissionRequest, true)]
    [InlineData(HookEvent.SessionStart, false)]
    [InlineData(HookEvent.PostCompact, false)]
    public void SupportsBlocking_ShouldReturnCorrectValue(HookEvent hookEvent, bool expected)
    {
        // Act
        var result = hookEvent.SupportsBlocking();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HookEvent_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var values = Enum.GetValues<HookEvent>();

        // Assert - 验证至少有核心事件值
        values.Should().Contain(HookEvent.PreToolUse);
        values.Should().Contain(HookEvent.PostToolUse);
        values.Should().Contain(HookEvent.PermissionRequest);
        values.Should().Contain(HookEvent.SessionStart);
        values.Should().Contain(HookEvent.SessionEnd);
    }
}
