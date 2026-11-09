namespace McpToolDispatch.Tests.Middleware;

/// <summary>
/// Unattended 模式工具限制测试 — Unattended 有独立的 allowed/denied 集合，
/// 通过 [SecurityClass(UnattendedDenied = true)] 标记的工具在 Unattended 模式下被系统级拦截。
/// ask_user_question / confirm_action 标记了 UnattendedDenied = true，因为它们会阻塞等待用户回答，
/// 与无人值守语义矛盾。
/// </summary>
public class AgentToolRestrictionsUnattendedTests {
    [Fact]
    public async Task GetDeniedTools_Unattended_ContainsAskUserAndConfirmAction() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var unattendedDenied = restrictions.GetDeniedTools(PermissionMode.Unattended);
        unattendedDenied.Should().Contain("ask_user_question", "ask_user 在无人值守模式下会阻塞等待用户回答");
        unattendedDenied.Should().Contain("confirm_action", "confirm_action 在无人值守模式下会阻塞等待用户确认");
    }

    [Fact]
    public async Task GetDeniedTools_Unattended_DoesNotContainAutoDeniedTools() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var unattendedDenied = restrictions.GetDeniedTools(PermissionMode.Unattended);
        var autoDenied = restrictions.GetDeniedTools(PermissionMode.Auto);
        // Unattended 的 denied 集合是独立的，不等于 Auto 的 denied 集合
        unattendedDenied.Should().NotBeEquivalentTo(autoDenied,
            "Unattended 有独立的 denied 集合，通过 [SecurityClass(UnattendedDenied = true)] 标记");
    }

    [Fact]
    public async Task IsToolAllowedForMode_Unattended_AskUserQuestion_ReturnsFalse() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        restrictions.IsToolAllowedForMode("ask_user_question", PermissionMode.Unattended)
            .Should().BeFalse("ask_user_question 在 Unattended 模式下被系统级拦截");
    }

    [Fact]
    public async Task IsToolAllowedForMode_Unattended_ConfirmAction_ReturnsFalse() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        restrictions.IsToolAllowedForMode("confirm_action", PermissionMode.Unattended)
            .Should().BeFalse("confirm_action 在 Unattended 模式下被系统级拦截");
    }

    [Fact]
    public async Task IsToolAllowedForMode_Unattended_AutoDeniedTool_ReturnsFalse() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        var autoDenied = restrictions.GetDeniedTools(PermissionMode.Auto);
        if (autoDenied.Count > 0) {
            var someDeniedTool = autoDenied.First();
            restrictions.IsToolAllowedForMode(someDeniedTool, PermissionMode.Unattended)
                .Should().BeFalse("Unattended 模式下 Auto denied 工具仍应被拒绝");
        }
    }

    [Fact]
    public async Task IsToolAllowedForMode_Auto_AskUserQuestion_ReturnsTrue() {
        await using var restrictions = new Core.Utils.AgentToolRestrictions();
        restrictions.IsToolAllowedForMode("ask_user_question", PermissionMode.Auto)
            .Should().BeTrue("ask_user_question 在 Auto 模式下是允许的");
    }
}
