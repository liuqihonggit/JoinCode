namespace Hands.Tests.Shell;

/// <summary>
/// ShellClassificationMiddleware 单元测试 — 验证命令分类中间件的结构化诊断
/// </summary>
public class ShellClassificationMiddlewareTests
{
    [Fact]
    public void BuildDestructiveCommandDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellClassificationMiddleware.BuildDestructiveCommandDiagnostic(
            "rm -rf /", "Recursive delete", ["DataLoss"]);

        diagnostic.Reason.Should().Be("危险命令检测");
        diagnostic.Details.Should().Contain(d => d.Key == "command" && d.Value == "rm -rf /");
        diagnostic.Details.Should().Contain(d => d.Key == "details" && d.Value == "Recursive delete");
        diagnostic.Details.Should().Contain(d => d.Key == "risks" && d.Value == "DataLoss");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildPathViolationDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellClassificationMiddleware.BuildPathViolationDiagnostic("cat /etc/passwd", "Path outside project");

        diagnostic.Reason.Should().Be("路径违规");
        diagnostic.Details.Should().Contain(d => d.Key == "command" && d.Value == "cat /etc/passwd");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildExcessiveSearchScopeDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellClassificationMiddleware.BuildExcessiveSearchScopeDiagnostic("rg pattern /", "Root path");

        diagnostic.Reason.Should().Be("搜索范围过大");
        diagnostic.Details.Should().Contain(d => d.Key == "command" && d.Value == "rg pattern /");
        diagnostic.Suggestions.Should().HaveCount(3);
    }

    [Fact]
    public void BuildDestructiveCommandFallbackDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellClassificationMiddleware.BuildDestructiveCommandFallbackDiagnostic(
            "rm -rf /", "Recursive delete warning", "High");

        diagnostic.Reason.Should().Be("危险命令检测");
        diagnostic.Details.Should().Contain(d => d.Key == "command" && d.Value == "rm -rf /");
        diagnostic.Details.Should().Contain(d => d.Key == "danger_level" && d.Value == "High");
    }
}

/// <summary>
/// ShellOutputMiddleware 单元测试 — 验证输出中间件的结构化诊断
/// </summary>
public class ShellOutputMiddlewareTests
{
    [Fact]
    public void BuildNoExecutionResultDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellOutputMiddleware.BuildNoExecutionResultDiagnostic();

        diagnostic.Reason.Should().Be("无执行结果");
        diagnostic.FormattedMessage.Should().Be("No execution result available");
    }

    [Fact]
    public void BuildInterruptedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellOutputMiddleware.BuildInterruptedDiagnostic("echo test", -1);

        diagnostic.Reason.Should().Be("命令中断");
        diagnostic.Details.Should().Contain(d => d.Key == "command" && d.Value == "echo test");
        diagnostic.Details.Should().Contain(d => d.Key == "exit_code" && d.Value == "-1");
    }

    [Fact]
    public void BuildCommandFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellOutputMiddleware.BuildCommandFailedDiagnostic("dotnet build", 1);

        diagnostic.Reason.Should().Be("命令执行失败");
        diagnostic.FormattedMessage.Should().Contain("exit code 1");
        diagnostic.Details.Should().Contain(d => d.Key == "exit_code" && d.Value == "1");
        diagnostic.Suggestions.Should().HaveCount(2);
    }
}
