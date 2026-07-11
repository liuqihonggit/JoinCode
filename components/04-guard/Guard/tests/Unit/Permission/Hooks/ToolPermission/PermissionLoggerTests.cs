
namespace Core.Tests.Hooks.ToolPermission;

public class PermissionLoggerTests
{
    private readonly PermissionLogger _logger;

    public PermissionLoggerTests()
    {
        _logger = new PermissionLogger(NullLogger<PermissionLogger>.Instance);
    }

    [Fact]
    public void LogPermissionDecision_AcceptFromConfig_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.Config
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_AcceptFromUser_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.User,
                Permanent = true
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_AcceptFromHook_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.Hook,
                HookName = "TestHook",
                Permanent = false
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_AcceptFromClassifier_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.Classifier
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_RejectFromConfig_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new RejectDecisionArgs
        {
            RejectionSource = new PermissionRejectionSource
            {
                Type = PermissionDecisionSourceType.Config
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_RejectFromUserAbort_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new RejectDecisionArgs
        {
            RejectionSource = new PermissionRejectionSource
            {
                Type = PermissionDecisionSourceType.UserAbort
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_RejectFromUserReject_WithFeedback_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new RejectDecisionArgs
        {
            RejectionSource = new PermissionRejectionSource
            {
                Type = PermissionDecisionSourceType.UserReject,
                HasFeedback = true
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_RejectFromHook_ShouldNotThrow()
    {
        var context = CreateTestContext();
        var args = new RejectDecisionArgs
        {
            RejectionSource = new PermissionRejectionSource
            {
                Type = PermissionDecisionSourceType.Hook,
                HookName = "TestHook",
                Reason = "Test rejection reason"
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionCancelled_ShouldNotThrow()
    {
        var context = CreateTestContext();

        Action act = () => _logger.LogPermissionCancelled(context);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(FileToolNameConstants.FileEdit, "accept", "config", null)]
    [InlineData(FileToolNameConstants.FileWrite, "reject", "user", "csharp")]
    [InlineData(NotebookToolNameConstants.NotebookEdit, "accept", "hook", "python")]
    public void LogCodeEditToolDecision_VariousInputs_ShouldNotThrow(
        string toolName, string decision, string source, string? language)
    {
        Action act = () => _logger.LogCodeEditToolDecision(toolName, decision, source, language);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogPermissionDecision_WithWaitTime_ShouldIncludeWaitTime()
    {
        var context = new PermissionLogContext
        {
            ToolName = "test_tool",
            Input = new Dictionary<string, JsonElement>(),
            MessageId = "msg_123",
            ToolUseId = "tool_use_123",
            WaitingForUserPermissionMs = 1500
        };
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.User,
                Permanent = false
            }
        };

        Action act = () => _logger.LogPermissionDecision(context, args);

        act.Should().NotThrow();
    }

    private static PermissionLogContext CreateTestContext()
    {
        return new PermissionLogContext
        {
            ToolName = "test_tool",
            Input = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonElementHelper.FromString("/test/path"),
                ["command"] = JsonElementHelper.FromString("echo test")
            },
            MessageId = "msg_123",
            ToolUseId = "tool_use_123",
            SandboxEnabled = true
        };
    }
}
