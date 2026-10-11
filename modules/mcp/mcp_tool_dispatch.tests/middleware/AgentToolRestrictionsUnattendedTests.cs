namespace McpToolDispatch.Tests.Middleware;

/// <summary>
/// Bug4 TDD：AgentToolRestrictions switch 未显式处理 PermissionMode.Unattended。
/// Unattended 语义：红灯自动执行+审计，黑灯仍拒绝 → 工具限制同 Auto（区别在中间件层）。
/// 修复前走 `_ => _autoAllowed` 默认分支，修复后显式 case。
/// </summary>
public class AgentToolRestrictionsUnattendedTests {
    [Fact]
    public async Task GetAllowedTools_Unattended_ReturnsSameAsAuto() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var unattendedAllowed = restrictions.GetAllowedTools(PermissionMode.Unattended);
        var autoAllowed = restrictions.GetAllowedTools(PermissionMode.Auto);
        unattendedAllowed.Should().BeEquivalentTo(autoAllowed);
    }

    [Fact]
    public async Task GetDeniedTools_Unattended_ReturnsSameAsAuto() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var unattendedDenied = restrictions.GetDeniedTools(PermissionMode.Unattended);
        var autoDenied = restrictions.GetDeniedTools(PermissionMode.Auto);
        unattendedDenied.Should().BeEquivalentTo(autoDenied);
    }

    [Fact]
    public async Task IsToolAllowedForMode_Unattended_NotBypassAllTools() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var denied = restrictions.GetDeniedTools(PermissionMode.Auto);
        if (denied.Count > 0) {
            var someDeniedTool = denied.First();
            restrictions.IsToolAllowedForMode(someDeniedTool, PermissionMode.Unattended)
                .Should().BeFalse("Unattended 黑灯仍拒绝，同 Auto");
        }
    }
}
