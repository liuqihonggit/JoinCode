namespace Hands.Tests.ToolHandlers;

/// <summary>
/// ShellToolBase / ShellToolHandlers / WebBrowserToolHandlers 诊断方法单元测试
/// </summary>
public class ShellToolBaseDiagnosticTests
{
    [Fact]
    public void BuildPowerShellUnavailableDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellToolBase.BuildPowerShellUnavailableDiagnostic();
        diagnostic.Reason.Should().Be("平台限制");
        diagnostic.Details.Should().Contain(d => d.Key == "tool" && d.Value == "PowerShell");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildSandboxPolicyViolationDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellToolBase.BuildSandboxPolicyViolationDiagnostic();
        diagnostic.Reason.Should().Be("安全策略冲突");
        diagnostic.Details.Should().Contain(d => d.Key == "platform" && d.Value == "Windows");
    }
}

public class ShellToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyTaskIdDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellToolHandlers.BuildEmptyTaskIdDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("task_id is required");
    }

    [Fact]
    public void BuildTaskNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellToolHandlers.BuildTaskNotFoundDiagnostic("task-123");
        diagnostic.Reason.Should().Be("任务未找到");
        diagnostic.Details.Should().Contain(d => d.Key == "task_id" && d.Value == "task-123");
    }

    [Fact]
    public void BuildCancelFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellToolHandlers.BuildCancelFailedDiagnostic("task-456");
        diagnostic.Reason.Should().Be("取消任务失败");
        diagnostic.Details.Should().Contain(d => d.Key == "task_id" && d.Value == "task-456");
        diagnostic.Suggestions.Should().HaveCount(2);
    }
}

public class WebBrowserToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyTargetDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebBrowserToolHandlers.BuildEmptyTargetDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "target");
    }

    [Fact]
    public void BuildInvalidWaitMsDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebBrowserToolHandlers.BuildInvalidWaitMsDiagnostic("wait_ms must be between 0 and 60000");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "wait_ms");
    }

    [Fact]
    public void BuildUnknownActionDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebBrowserToolHandlers.BuildUnknownActionDiagnostic("invalid");
        diagnostic.Reason.Should().Be("未知操作");
        diagnostic.Details.Should().Contain(d => d.Key == "action" && d.Value == "invalid");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = WebBrowserToolHandlers.BuildOperationFailedDiagnostic("timeout");
        diagnostic.Reason.Should().Be("操作失败");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "timeout");
    }
}
