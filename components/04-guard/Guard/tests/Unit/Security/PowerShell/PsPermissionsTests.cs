// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Guard.Tests.Security.PowerShell;

/// <summary>
/// PsPermissions 单元测试 — 验证收集-归约权限检查
/// </summary>
public class PsPermissionsTests
{
    private static readonly string WorkDir = Directory.GetCurrentDirectory();

    [Fact]
    public void CheckPermission_EmptyCommand_ReturnsPassthrough()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "", WorkDir, [], [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Passthrough, result.Behavior);
    }

    [Fact]
    public void CheckPermission_ReadOnlyCommand_ReturnsAllow()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Get-Process", WorkDir, [], [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public void CheckPermission_DenyRule_ReturnsDeny()
    {
        var denyRules = new List<string> { "Remove-Item" };
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Remove-Item test.txt", WorkDir, denyRules, [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPermission_DenyRuleByAlias_ReturnsDeny()
    {
        var denyRules = new List<string> { "Remove-Item" };
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "ri test.txt", WorkDir, denyRules, [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPermission_InvokeExpression_ReturnsAsk()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Invoke-Expression 'Get-Process'", WorkDir, [], [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
    }

    [Fact]
    public void CheckPermission_AskRule_ReturnsAsk()
    {
        var askRules = new List<string> { "Set-Content" };
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Set-Content -Path test.txt -Value 'hello'", WorkDir, [], askRules, [], [], [], false);
        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
    }

    [Fact]
    public void CheckPermission_AllowRule_ReturnsAllow()
    {
        var allowRules = new List<string> { "Set-Content" };
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Set-Content -Path test.txt -Value 'hello'", WorkDir, [], [], allowRules, [], [], false);
        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public void CheckPermission_DenyOverridesAllow_ReturnsDeny()
    {
        // deny 规则优先于 allow
        var denyRules = new List<string> { "Remove-Item" };
        var allowRules = new List<string> { "Remove-Item" };
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Remove-Item test.txt", WorkDir, denyRules, [], allowRules, [], [], false);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public void CheckPermission_AcceptEdits_ReturnsAllow()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            "Set-Content -Path test.txt -Value 'hello'", WorkDir, [], [], [], [], [], true);
        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public void CheckPermission_UncPath_ReturnsDeny()
    {
        var result = JoinCode.Guard.Security.PowerShell.PsPermissions.CheckPermission(
            @"Get-Content -Path \\server\share\file.txt", WorkDir, [], [], [], [], [], false);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }
}
