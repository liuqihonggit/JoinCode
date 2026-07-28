namespace Core.Agents;

public sealed class AgentTaskNotificationTests
{
    [Fact]
    public void ToXml_ContainsAllFields()
    {
        var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
        {
            TaskId = "agent-123",
            Status = "completed",
            Description = "Check README",
            ToolUseId = "call_abc",
            Output = "README found with 100 lines",
            ExecutionTimeMs = 5000,
            AgentType = "explore",
            ToolUseCount = 3,
            TokenCount = 1500,
            WorktreePath = "/tmp/worktree-agent-123",
            WorktreeBranch = "agent-123"
        };

        var xml = notification.ToXml();

        xml.Should().Contain("<task-id>agent-123</task-id>");
        xml.Should().Contain("<status>completed</status>");
        xml.Should().Contain("<summary>Agent \"Check README\" completed</summary>");
        xml.Should().Contain("<result>");
        xml.Should().Contain("README found with 100 lines");
        xml.Should().Contain("<tool-use-id>call_abc</tool-use-id>");
        xml.Should().Contain("<total_tokens>1500</total_tokens>");
        xml.Should().Contain("<tool_uses>3</tool_uses>");
        xml.Should().Contain("<duration_ms>5000</duration_ms>");
        xml.Should().Contain("<agent-type>explore</agent-type>");
        xml.Should().Contain("<worktreePath>/tmp/worktree-agent-123</worktreePath>");
        xml.Should().Contain("<worktreeBranch>agent-123</worktreeBranch>");
    }

    [Fact]
    public void ToXml_WithoutWorktree_NoWorktreeElement()
    {
        var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
        {
            TaskId = "agent-456",
            Status = "failed",
            Description = "Search code",
            Error = "Network timeout"
        };

        var xml = notification.ToXml();

        xml.Should().Contain("<status>failed</status>");
        xml.Should().Contain("<error>Network timeout</error>");
        xml.Should().NotContain("<worktree>");
        xml.Should().NotContain("<worktreePath>");
    }

    [Fact]
    public void ToXml_WithWorktreePathOnly_NoBranch()
    {
        var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
        {
            TaskId = "agent-789",
            Status = "completed",
            Description = "Build check",
            WorktreePath = "/tmp/wt-789"
        };

        var xml = notification.ToXml();

        xml.Should().Contain("<worktree>");
        xml.Should().Contain("<worktreePath>/tmp/wt-789</worktreePath>");
        xml.Should().NotContain("<worktreeBranch>");
    }

    [Fact]
    public void ToXml_FailedAgent_ContainsErrorNotResult()
    {
        var notification = new JoinCode.Abstractions.Interfaces.AgentTaskNotification
        {
            TaskId = "agent-fail",
            Status = "failed",
            Description = "Broken task",
            Error = "Something went wrong"
        };

        var xml = notification.ToXml();

        xml.Should().Contain("<error>Something went wrong</error>");
        xml.Should().NotContain("<result>");
    }
}
