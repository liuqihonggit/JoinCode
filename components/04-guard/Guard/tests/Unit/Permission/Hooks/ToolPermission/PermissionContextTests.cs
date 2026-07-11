
namespace Core.Tests.Hooks.ToolPermission;

public class PermissionContextTests
{
    private readonly TestPermissionLogger _logger;

    public PermissionContextTests()
    {
        _logger = new TestPermissionLogger();
    }

    [Fact]
    public void Constructor_ShouldSetProperties()
    {
        var toolName = "test_tool";
        var input = new Dictionary<string, JsonElement> { ["key"] = JsonElementHelper.FromString("value") };
        var messageId = "msg_123";
        var toolUseId = "tool_use_123";

        var ctx = new PermissionContext(toolName, input, messageId, toolUseId, _logger);

        ctx.ToolName.Should().Be(toolName);
        ctx.Input.Should().BeEquivalentTo(input);
        ctx.MessageId.Should().Be(messageId);
        ctx.ToolUseId.Should().Be(toolUseId);
    }

    [Fact]
    public void LogDecision_Accept_ShouldLogToLogger()
    {
        var ctx = CreateTestContext();
        var args = new AcceptDecisionArgs
        {
            ApprovalSource = new PermissionApprovalSource
            {
                Type = PermissionDecisionSourceType.User,
                Permanent = true
            }
        };

        ctx.LogDecision(args);

        _logger.LoggedDecisions.Should().HaveCount(1);
        _logger.LoggedDecisions[0].Args.Should().Be(args);
    }

    [Fact]
    public void LogDecision_Reject_ShouldLogToLogger()
    {
        var ctx = CreateTestContext();
        var args = new RejectDecisionArgs
        {
            RejectionSource = new PermissionRejectionSource
            {
                Type = PermissionDecisionSourceType.UserReject,
                HasFeedback = true
            }
        };

        ctx.LogDecision(args);

        _logger.LoggedDecisions.Should().HaveCount(1);
    }

    [Fact]
    public void LogCancelled_ShouldLogToLogger()
    {
        var ctx = CreateTestContext();

        ctx.LogCancelled();

        _logger.LoggedCancellations.Should().HaveCount(1);
    }

    [Fact]
    public void ResolveIfAborted_NotCancelled_ShouldReturnFalse()
    {
        var ctx = CreateTestContext();
        var resolved = false;

        var result = ctx.ResolveIfAborted(_ => resolved = true);

        result.Should().BeFalse();
        resolved.Should().BeFalse();
    }

    [Fact]
    public void ResolveIfAborted_Cancelled_ShouldReturnTrueAndResolve()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = CreateTestContext(cts.Token);
        PermissionDecision? resolvedDecision = null;

        var result = ctx.ResolveIfAborted(d => resolvedDecision = d);

        result.Should().BeTrue();
        resolvedDecision.Should().NotBeNull();
        resolvedDecision.Should().BeOfType<PermissionDenyDecision>();
    }

    [Fact]
    public void CancelAndAbort_NoFeedback_ShouldReturnDenyDecision()
    {
        var ctx = CreateTestContext();

        var result = ctx.CancelAndAbort();

        result.Should().BeOfType<PermissionDenyDecision>();
        ((PermissionDenyDecision)result).Message.Should().Contain("cancelled");
    }

    [Fact]
    public void CancelAndAbort_WithFeedback_ShouldIncludeFeedback()
    {
        var ctx = CreateTestContext();
        var feedback = "User provided reason";

        var result = ctx.CancelAndAbort(feedback);

        result.Should().BeOfType<PermissionDenyDecision>();
        ((PermissionDenyDecision)result).Message.Should().Contain(feedback);
    }

    [Fact]
    public void BuildAllow_ShouldReturnAllowDecision()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement> { ["key"] = JsonElementHelper.FromString("updated") };

        var result = ctx.BuildAllow(updatedInput);

        result.Should().BeOfType<PermissionAllowDecision>();
        ((PermissionAllowDecision)result).UpdatedInput.Should().ContainKey("key");
    }

    [Fact]
    public void BuildAllow_WithDecisionReason_ShouldIncludeReason()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement>();
        var reason = new HookDecisionReason { HookName = "TestHook", Reason = "Test" };

        var result = ctx.BuildAllow(updatedInput, reason);

        ((PermissionAllowDecision)result).DecisionReason.Should().Be(reason);
    }

    [Fact]
    public void BuildDeny_ShouldReturnDenyDecision()
    {
        var ctx = CreateTestContext();
        var message = "Access denied";
        var reason = new HookDecisionReason { HookName = "TestHook" };

        var result = ctx.BuildDeny(message, reason);

        result.Should().BeOfType<PermissionDenyDecision>();
        ((PermissionDenyDecision)result).Message.Should().Be(message);
        ((PermissionDenyDecision)result).DecisionReason.Should().Be(reason);
    }

    [Fact]
    public async Task HandleUserAllow_ShouldReturnAllowDecision()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement> { ["key"] = JsonElementHelper.FromString("value") };
        var permissionUpdates = new List<PermissionUpdate>();

        var result = await ctx.HandleUserAllow(updatedInput, permissionUpdates).ConfigureAwait(true);

        result.Should().BeOfType<PermissionAllowDecision>();
    }

    [Fact]
    public async Task HandleUserAllow_WithFeedback_ShouldIncludeFeedback()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement>();
        var permissionUpdates = new List<PermissionUpdate>();
        var feedback = "User feedback";

        var result = await ctx.HandleUserAllow(updatedInput, permissionUpdates, feedback).ConfigureAwait(true);

        result.AcceptFeedback.Should().Be(feedback);
    }

    [Fact]
    public async Task HandleHookAllow_ShouldReturnAllowDecision()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement> { ["key"] = JsonElementHelper.FromString("value") };
        var permissionUpdates = new List<PermissionUpdate>();

        var result = await ctx.HandleHookAllow(updatedInput, permissionUpdates).ConfigureAwait(true);

        result.Should().BeOfType<PermissionAllowDecision>();
        result.DecisionReason.Should().BeOfType<HookDecisionReason>();
    }

    [Fact]
    public void PushToQueue_WithQueueOps_ShouldCallPush()
    {
        var queueOps = new TestPermissionQueueOperations();
        var ctx = CreateTestContext(queueOps: queueOps);
        var item = new PermissionQueueItem
        {
            ToolUseId = "test_id",
            ToolName = "test_tool",
            Description = "Test",
            PermissionPromptStartTime = DateTimeOffset.UtcNow
        };

        ctx.PushToQueue(item);

        queueOps.PushedItems.Should().Contain(item);
    }

    [Fact]
    public void RemoveFromQueue_WithQueueOps_ShouldCallRemove()
    {
        var queueOps = new TestPermissionQueueOperations();
        var ctx = CreateTestContext(queueOps: queueOps);

        ctx.RemoveFromQueue();

        queueOps.RemovedToolUseIds.Should().Contain(ctx.ToolUseId);
    }

    private PermissionContext CreateTestContext(
        CancellationToken cancellationToken = default,
        IPermissionQueueOperations? queueOps = null)
    {
        return new PermissionContext(
            "test_tool",
            new Dictionary<string, JsonElement> { ["key"] = JsonElementHelper.FromString("value") },
            "msg_123",
            "tool_use_123",
            _logger,
            queueOps,
            cancellationToken);
    }

    #region Test Helpers

    private class TestPermissionLogger : IPermissionLogger
    {
        public List<(PermissionLogContext Context, PermissionDecisionArgs Args)> LoggedDecisions { get; } = new();
        public List<PermissionLogContext> LoggedCancellations { get; } = new();

        public void LogPermissionDecision(PermissionLogContext context, PermissionDecisionArgs args)
        {
            LoggedDecisions.Add((context, args));
        }

        public void LogPermissionCancelled(PermissionLogContext context)
        {
            LoggedCancellations.Add(context);
        }

        public void LogCodeEditToolDecision(string toolName, string decision, string source, string? language = null)
        {
        }
    }

    private class TestPermissionQueueOperations : IPermissionQueueOperations
    {
        public List<PermissionQueueItem> PushedItems { get; } = new();
        public List<string> RemovedToolUseIds { get; } = new();
        public List<(string ToolUseId, Action<PermissionQueueItem> Patch)> Updates { get; } = new();

        public void Push(PermissionQueueItem item)
        {
            PushedItems.Add(item);
        }

        public void Remove(string toolUseId)
        {
            RemovedToolUseIds.Add(toolUseId);
        }

        public void Update(string toolUseId, Action<PermissionQueueItem> patch)
        {
            Updates.Add((toolUseId, patch));
        }
    }

    #endregion
}
