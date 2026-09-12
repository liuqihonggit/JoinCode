namespace Hands.Tests.ToolHandlers;

/// <summary>
/// PowerShellToolHandlers 诊断方法单元测试
/// </summary>
public class PowerShellToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildCommandEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildCommandEmptyDiagnostic();
        diag.Reason.Should().Be("PowerShellCommandEmpty");
        diag.FormattedMessage.Should().Be("command cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Field" && d.Value == "command");
        diag.Suggestions.Should().ContainSingle(s => s.Contains("PowerShell command"));
    }

    [Fact]
    public void BuildPermissionDeniedDiagnostic_Deny_ReturnsCorrectStructure()
    {
        var permResult = new PsSecurityResult(PermissionBehavior.Deny, "Blocked by policy");
        var diag = PowerShellToolHandlers.BuildPermissionDeniedDiagnostic(permResult, "⚠ Operation denied\n\nBlocked by policy");
        diag.Reason.Should().Be("PowerShellPermissionDenied");
        diag.FormattedMessage.Should().Contain("Operation denied");
        diag.Details.Should().Contain(d => d.Key == "Behavior" && d.Value == "Denied");
        diag.Details.Should().Contain(d => d.Key == "Message" && d.Value == "Blocked by policy");
    }

    [Fact]
    public void BuildPermissionDeniedDiagnostic_Ask_ReturnsCorrectStructure()
    {
        var permResult = new PsSecurityResult(PermissionBehavior.Ask, "Approval needed");
        var diag = PowerShellToolHandlers.BuildPermissionDeniedDiagnostic(permResult, "⚠ User approval required");
        diag.Reason.Should().Be("PowerShellPermissionAsk");
        diag.Details.Should().Contain(d => d.Key == "Behavior" && d.Value == "Ask");
    }

    [Fact]
    public void BuildDestructiveCommandDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildDestructiveCommandDiagnostic("Remove-Item -Recurse", "Destructive operation", "⚠ Potentially dangerous command detected");
        diag.Reason.Should().Be("PowerShellDestructiveCommand");
        diag.FormattedMessage.Should().Contain("dangerous command");
        diag.Details.Should().Contain(d => d.Key == "Command" && d.Value == "Remove-Item -Recurse");
        diag.Suggestions.Should().ContainSingle(s => s.Contains("confirm"));
    }

    [Fact]
    public void BuildScriptPathEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildScriptPathEmptyDiagnostic();
        diag.Reason.Should().Be("PowerShellScriptPathEmpty");
        diag.FormattedMessage.Should().Be("script_path cannot be empty");
    }

    [Fact]
    public void BuildInvalidScriptExtensionDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildInvalidScriptExtensionDiagnostic("script.txt");
        diag.Reason.Should().Be("PowerShellInvalidScriptExtension");
        diag.Details.Should().Contain(d => d.Key == "ProvidedPath" && d.Value == "script.txt");
        diag.Details.Should().Contain(d => d.Key == "RequiredExtension" && d.Value == ".ps1");
    }

    [Fact]
    public void BuildScriptNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildScriptNotFoundDiagnostic("C:\\missing.ps1");
        diag.Reason.Should().Be("PowerShellScriptNotFound");
        diag.FormattedMessage.Should().Contain("C:\\missing.ps1");
    }

    [Fact]
    public void BuildScriptInterruptedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildScriptInterruptedDiagnostic("C:\\test.ps1", "Terminated");
        diag.Reason.Should().Be("PowerShellScriptInterrupted");
        diag.Details.Should().Contain(d => d.Key == "ScriptPath" && d.Value == "C:\\test.ps1");
    }

    [Fact]
    public void BuildScriptFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildScriptFailedDiagnostic("C:\\test.ps1", "Syntax error");
        diag.Reason.Should().Be("PowerShellScriptFailed");
        diag.FormattedMessage.Should().Be("Syntax error");
    }

    [Fact]
    public void BuildPolicyEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildPolicyEmptyDiagnostic();
        diag.Reason.Should().Be("PowerShellPolicyEmpty");
        diag.FormattedMessage.Should().Be("policy cannot be empty");
    }

    [Fact]
    public void BuildInvalidPolicyDiagnostic_ReturnsCorrectStructure()
    {
        var valid = new[] { "Restricted", "Bypass" };
        var diag = PowerShellToolHandlers.BuildInvalidPolicyDiagnostic("Foo", valid);
        diag.Reason.Should().Be("PowerShellInvalidPolicy");
        diag.FormattedMessage.Should().Contain("Foo");
        diag.FormattedMessage.Should().Contain("Restricted");
        diag.Details.Should().Contain(d => d.Key == "ProvidedPolicy" && d.Value == "Foo");
    }

    [Fact]
    public void BuildSetExecutionPolicyFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PowerShellToolHandlers.BuildSetExecutionPolicyFailedDiagnostic("RemoteSigned", "LocalMachine", "Access is denied");
        diag.Reason.Should().Be("PowerShellSetExecutionPolicyFailed");
        diag.FormattedMessage.Should().Be("Access is denied");
        diag.Details.Should().Contain(d => d.Key == "Policy" && d.Value == "RemoteSigned");
        diag.Details.Should().Contain(d => d.Key == "Scope" && d.Value == "LocalMachine");
        diag.Suggestions.Should().ContainSingle(s => s.Contains("Process"));
    }
}
