
namespace Guard.Tests.Permission.Permission;

/// <summary>
/// AgentRestrictionMiddleware 单元测试 — 验证用户显式 allow 列表优先于硬编码 Agent 限制
/// </summary>
public sealed class AgentRestrictionMiddlewareTests
{
    /// <summary>
    /// 🔴 红测试: AutoApprovedTools 包含 Bash 时，AgentRestrictionMiddleware 应放行
    /// 根因: Bash 在 AutoDeniedTools 中，IsToolAllowedForMode 返回 false，短路拒绝
    /// 修复后: AutoApprovedTools 优先，绕过硬编码 Agent 限制
    /// </summary>
    [Fact]
    public async Task InvokeAsync_BashInAutoApprovedTools_ShouldBypassAgentRestriction()
    {
        // 安排 — 模拟生产环境: DI 注入 AgentToolRestrictions，用户配置 permissions.allow 包含 Bash
        var sut = new AgentRestrictionMiddleware(new AgentToolRestrictions());
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new PermissionCheckContext
        {
            ToolName = ShellToolNameConstants.ShellExecute, // "Bash"
            Arguments = null,
            CurrentMode = PermissionMode.Default,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ShellToolNameConstants.ShellExecute },
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        // 行动
        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        // 断言 — next 应被调用（放行），Result 不应是 Rejected
        nextCalled.Should().BeTrue("用户显式 allow 列表中的工具应绕过 Agent 硬编码限制");
        context.Result.Should().BeNull("未设置结果表示交由下游中间件决策");
    }

    /// <summary>
    /// 🟢 验证: AutoApprovedTools 不包含 Bash 时，AgentRestrictionMiddleware 应拒绝（保持现有行为）
    /// </summary>
    [Fact]
    public async Task InvokeAsync_BashNotInAutoApprovedTools_ShouldReject()
    {
        var sut = new AgentRestrictionMiddleware(new AgentToolRestrictions());
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new PermissionCheckContext
        {
            ToolName = ShellToolNameConstants.ShellExecute, // "Bash"
            Arguments = null,
            CurrentMode = PermissionMode.Default,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeFalse("Bash 不在 AutoApprovedTools 中，应被 Agent 限制拒绝");
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// 🟢 验证: Read 工具在 AutoAllowedTools 中，即使不在 AutoApprovedTools 中也应放行
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReadToolNotInAutoApproved_ShouldStillAllow()
    {
        var sut = new AgentRestrictionMiddleware(new AgentToolRestrictions());
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new PermissionCheckContext
        {
            ToolName = FileToolNameConstants.FileRead,
            Arguments = null,
            CurrentMode = PermissionMode.Default,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("Read 在 AutoAllowedTools 中，应放行");
        context.Result.Should().BeNull();
    }
}
