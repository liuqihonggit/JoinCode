namespace Core.Tests.Security.PermissionPipeline;

/// <summary>
/// 权限管道工具集作用域测试 — 子代理白名单/拒绝集合必须进入 <c>PermissionCheckContext</c>，
/// 并在 <c>AgentRestrictionMiddleware</c> 中先于 auto-approved 快捷路径生效。
/// </summary>
public sealed class AgentRestrictionContextTests {
    private static PermissionCheckContext CreateContext(
        string toolName,
        IReadOnlySet<string>? allowed = null,
        IReadOnlySet<string>? denied = null) {
        return new PermissionCheckContext {
            ToolName = toolName,
            Arguments = [],
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedTools = allowed,
            DeniedTools = denied,
        };
    }

    [Fact]
    public async Task Invoke_AskNotInAllowedTools_DeniedEvenIfAutoApproved() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new Core.Permission.AgentRestrictionMiddleware(
            restrictions,
            new Core.Utils.ToolFilterPolicy());
        var context = CreateContext(
            UserInteractionToolNameEnumConstants.AskUserQuestion,
            allowed: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileToolNameEnumConstants.FileRead });
        context.AutoApprovedTools.Add(UserInteractionToolNameEnumConstants.AskUserQuestion);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse("模式/白名单限制必须优先于 auto-approved 快捷路径");
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result.Reason.Should().Contain("白名单");
    }

    [Fact]
    public async Task Invoke_AskInDeniedTools_Denied() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new Core.Permission.AgentRestrictionMiddleware(
            restrictions,
            new Core.Utils.ToolFilterPolicy());
        var context = CreateContext(
            UserInteractionToolNameEnumConstants.AskUserQuestion,
            denied: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserInteractionToolNameEnumConstants.AskUserQuestion });
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse("显式拒绝集合必须生效");
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ReadAllowed_Allows() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var middleware = new Core.Permission.AgentRestrictionMiddleware(
            restrictions,
            new Core.Utils.ToolFilterPolicy());
        var context = CreateContext(
            FileToolNameEnumConstants.FileRead,
            allowed: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileToolNameEnumConstants.FileRead });
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }
}
