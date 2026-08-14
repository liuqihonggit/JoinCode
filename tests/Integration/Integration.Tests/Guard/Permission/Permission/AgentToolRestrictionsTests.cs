namespace Integration.Tests.Guard.Permission;

/// <summary>
/// AgentToolRestrictions 单元测试 — 验证工具在各权限模式下的允许/拒绝
/// </summary>
public sealed class AgentToolRestrictionsTests
{
    private readonly AgentToolRestrictions _restrictions = new();

    [Theory]
    [InlineData(SystemToolNameConstants.TaskOutput, PermissionMode.Auto)]
    [InlineData(SystemToolNameConstants.TaskOutput, PermissionMode.Plan)]
    [InlineData(SystemToolNameConstants.TaskOutput, PermissionMode.Ask)]
    public void IsToolAllowedForMode_TaskOutput_ShouldBeAllowed(string toolName, PermissionMode mode)
    {
        // Act
        var isAllowed = _restrictions.IsToolAllowedForMode(toolName, mode);

        // Assert — TaskOutput 是只读工具（获取后台任务输出），应在所有标准模式下被允许
        Assert.True(isAllowed, $"工具 '{toolName}' 应在 {mode} 模式下被允许，但被拒绝");
    }

    [Theory]
    [InlineData(TaskToolNameConstants.TaskList, PermissionMode.Auto)]
    [InlineData(TaskToolNameConstants.TaskGet, PermissionMode.Auto)]
    public void IsToolAllowedForMode_KnownTaskTools_ShouldBeAllowed(string toolName, PermissionMode mode)
    {
        // Act
        var isAllowed = _restrictions.IsToolAllowedForMode(toolName, mode);

        // Assert — 已知的任务工具应在 Auto 模式下被允许（回归测试）
        Assert.True(isAllowed, $"工具 '{toolName}' 应在 {mode} 模式下被允许");
    }

    [Theory]
    [InlineData(ShellToolNameConstants.Bash, PermissionMode.Auto)]
    [InlineData(ShellToolNameConstants.Powershell, PermissionMode.Auto)]
    public void IsToolAllowedForMode_SensitiveTools_ShouldBeAllowedInAuto(string toolName, PermissionMode mode)
    {
        // Act
        var isAllowed = _restrictions.IsToolAllowedForMode(toolName, mode);

        // Assert — 敏感工具在 Auto 模式下不再被 AgentRestrictions 直接拒绝，
        // 而是放行到 AutoClassifierMiddleware 走交互确认路径
        Assert.True(isAllowed, $"敏感工具 '{toolName}' 在 {mode} 模式下应放行到后续中间件确认");
    }

    [Fact]
    public void PermissionConfig_CreateDefault_ShouldIncludeTaskOutputInAutoApproved()
    {
        // Act
        var config = PermissionConfig.CreateDefault();

        // Assert — TaskOutput 是只读工具（获取后台任务输出），应在默认 AutoApprovedTools 中
        Assert.Contains(config.AutoApprovedTools.Values, r => r.ToolName == SystemToolNameConstants.TaskOutput);
    }
}
