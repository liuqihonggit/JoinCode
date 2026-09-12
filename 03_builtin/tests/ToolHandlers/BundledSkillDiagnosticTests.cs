namespace Hands.Tests.ToolHandlers;

/// <summary>
/// BundledSkillToolHandlers 诊断方法单元测试
/// </summary>
public class BundledSkillToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyFilePathDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildEmptyFilePathDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("file_path cannot be empty");
    }

    [Fact]
    public void BuildFileReadFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildFileReadFailedDiagnostic("/test/file.cs");
        diagnostic.Reason.Should().Be("文件读取失败");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.cs");
    }

    [Fact]
    public void BuildEmptyPathDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildEmptyPathDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("path cannot be empty");
    }

    [Fact]
    public void BuildPathNotExistDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildPathNotExistDiagnostic("/missing/path");
        diagnostic.Reason.Should().Be("路径不存在");
        diagnostic.Details.Should().Contain(d => d.Key == "path" && d.Value == "/missing/path");
    }

    [Fact]
    public void BuildVerificationFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildVerificationFailedDiagnostic(3);
        diagnostic.Reason.Should().Be("验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "failed_count" && d.Value == "3");
    }

    [Fact]
    public void BuildEmptyPatternDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = BundledSkillToolHandlers.BuildEmptyPatternDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("pattern cannot be empty");
    }
}
