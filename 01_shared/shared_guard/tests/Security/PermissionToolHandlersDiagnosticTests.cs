namespace Guard.Security.Tests;

/// <summary>
/// PermissionToolHandlers 诊断方法单元测试
/// </summary>
public class PermissionToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildAgentPatternEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildAgentPatternEmptyDiagnostic();
        diag.Reason.Should().Be("PermissionAgentPatternEmpty");
        diag.FormattedMessage.Should().Be("agent_pattern 不能为空");
        diag.Details.Should().Contain(d => d.Key == "Field" && d.Value == "agent_pattern");
    }

    [Fact]
    public void BuildInvalidPermissionModeDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildInvalidPermissionModeDiagnostic("invalid");
        diag.Reason.Should().Be("PermissionInvalidMode");
        diag.FormattedMessage.Should().Contain("invalid");
        diag.FormattedMessage.Should().Contain("auto, plan, ask, deny");
        diag.Details.Should().Contain(d => d.Key == "ProvidedMode" && d.Value == "invalid");
    }

    [Fact]
    public void BuildInvalidPermissionLevelDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildInvalidPermissionLevelDiagnostic("super");
        diag.Reason.Should().Be("PermissionInvalidLevel");
        diag.FormattedMessage.Should().Contain("super");
        diag.Details.Should().Contain(d => d.Key == "ProvidedLevel" && d.Value == "super");
    }

    [Fact]
    public void BuildRuleNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildRuleNotFoundDiagnostic("agent-*");
        diag.Reason.Should().Be("PermissionRuleNotFound");
        diag.FormattedMessage.Should().Contain("agent-*");
        diag.Details.Should().Contain(d => d.Key == "AgentPattern" && d.Value == "agent-*");
    }

    [Fact]
    public void BuildAgentNameEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildAgentNameEmptyDiagnostic();
        diag.Reason.Should().Be("PermissionAgentNameEmpty");
        diag.FormattedMessage.Should().Be("agent_name 不能为空");
    }

    [Fact]
    public void BuildToolNameEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildToolNameEmptyDiagnostic();
        diag.Reason.Should().Be("PermissionToolNameEmpty");
        diag.FormattedMessage.Should().Be("tool_name 不能为空");
    }

    [Fact]
    public void BuildPathEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildPathEmptyDiagnostic();
        diag.Reason.Should().Be("PermissionPathEmpty");
        diag.FormattedMessage.Should().Be("path 不能为空");
    }

    [Fact]
    public void BuildClearConfirmInvalidDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PermissionToolHandlers.BuildClearConfirmInvalidDiagnostic();
        diag.Reason.Should().Be("PermissionClearConfirmInvalid");
        diag.FormattedMessage.Should().Contain("yes");
        diag.Details.Should().Contain(d => d.Key == "RequiredValue" && d.Value == "yes");
    }
}
