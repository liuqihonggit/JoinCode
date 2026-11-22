namespace McpToolDispatch.Tests.Middleware;

/// <summary>
/// 子代理工具集作用域测试 — <c>ToolExecutionContext.AllowedTools/DeniedTools</c> 必须在执行层生效，
/// 否则后台 fork/resume 即使设置了白名单，模型仍可调用 ask_user_question 并阻塞。
/// </summary>
public sealed class AgentContextToolSetTests {
    [Fact]
    public async Task Invoke_AllowedToolsExcludesAsk_DeniesAskUserQuestion() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new McpToolRegistry.AgentRestrictionMiddleware(
            restrictions,
            NullLogger<McpToolRegistry.AgentRestrictionMiddleware>.Instance,
            new Core.Utils.ToolFilterPolicy());

        var context = new McpToolRegistry.ToolExecutionContext {
            ToolName = UserInteractionToolNameEnumConstants.AskUserQuestion,
            Arguments = [],
            AgentMode = PermissionMode.Auto,
            AllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileToolNameEnumConstants.FileRead },
        };
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse("ask_user_question 不在子代理白名单中，必须在执行前拒绝");
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        context.PermissionDenyReason.Should().Contain("白名单");
    }

    [Fact]
    public async Task Invoke_DeniedToolsContainsAsk_DeniesAskUserQuestion() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new McpToolRegistry.AgentRestrictionMiddleware(
            restrictions,
            NullLogger<McpToolRegistry.AgentRestrictionMiddleware>.Instance,
            new Core.Utils.ToolFilterPolicy());

        var context = new McpToolRegistry.ToolExecutionContext {
            ToolName = UserInteractionToolNameEnumConstants.AskUserQuestion,
            Arguments = [],
            AgentMode = PermissionMode.Auto,
            DeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserInteractionToolNameEnumConstants.AskUserQuestion },
        };
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse("显式拒绝集合中的工具必须在执行前拒绝");
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
    }

    [Fact]
    public async Task Invoke_AllowedToolsContainsRead_AllowsRead() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new McpToolRegistry.AgentRestrictionMiddleware(
            restrictions,
            NullLogger<McpToolRegistry.AgentRestrictionMiddleware>.Instance,
            new Core.Utils.ToolFilterPolicy());

        var context = new McpToolRegistry.ToolExecutionContext {
            ToolName = FileToolNameEnumConstants.FileRead,
            Arguments = [],
            AgentMode = PermissionMode.Unattended,
            AllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileToolNameEnumConstants.FileRead },
        };
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.PermissionDecision.Should().Be(PermissionDecision.Allowed);
    }
}
