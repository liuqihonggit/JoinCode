namespace Hands.Tests.ToolHandlers;

/// <summary>
/// SearchToolHandlers 错误诊断方法单元测试
/// </summary>
public class SearchToolHandlersErrorDiagnosticTests
{
    [Fact]
    public void BuildPathNotDirectoryDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildPathNotDirectoryDiagnostic("/some/file.txt");
        diag.Reason.Should().Be("SearchPathNotDirectory");
        diag.FormattedMessage.Should().Contain("Path is not a directory");
        diag.Details.Should().Contain(d => d.Key == "Path" && d.Value == "/some/file.txt");
    }

    [Fact]
    public void BuildDirectoryNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildDirectoryNotFoundDiagnostic("/missing", "Directory does not exist: /missing", null);
        diag.Reason.Should().Be("SearchDirectoryNotFound");
        diag.FormattedMessage.Should().Contain("Directory does not exist");
        diag.Details.Should().Contain(d => d.Key == "Path" && d.Value == "/missing");
    }

    [Fact]
    public void BuildGlobTimeoutDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildGlobTimeoutDiagnostic();
        diag.Reason.Should().Be("GlobSearchTimeout");
        diag.FormattedMessage.Should().Contain("timed out");
    }

    [Fact]
    public void BuildGrepTimeoutDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildGrepTimeoutDiagnostic();
        diag.Reason.Should().Be("GrepSearchTimeout");
        diag.FormattedMessage.Should().Contain("timed out");
    }

    [Fact]
    public void BuildSearchFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildSearchFailedDiagnostic("glob", "ripgrep crashed");
        diag.Reason.Should().Be("SearchglobFailed");
        diag.FormattedMessage.Should().Be("ripgrep crashed");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "glob");
    }

    [Fact]
    public void BuildSearchFailedDiagnostic_NullError_ReturnsDefault()
    {
        var diag = SearchToolHandlers.BuildSearchFailedDiagnostic("grep", null);
        diag.FormattedMessage.Should().Be("Search failed");
    }

    [Fact]
    public void BuildGrepValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildGrepValidationErrorDiagnostic("before must be >= 0");
        diag.Reason.Should().Be("GrepValidationError");
        diag.FormattedMessage.Should().Be("before must be >= 0");
    }

    [Fact]
    public void BuildGrepPathNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildGrepPathNotFoundDiagnostic("/missing", "Path does not exist: /missing", null);
        diag.Reason.Should().Be("GrepPathNotFound");
        diag.FormattedMessage.Should().Contain("Path does not exist");
    }

    [Fact]
    public void BuildPathPermissionDeniedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildPathPermissionDeniedDiagnostic("/secret", "Blocked by policy");
        diag.Reason.Should().Be("SearchPathPermissionDenied");
        diag.FormattedMessage.Should().Be("Blocked by policy");
        diag.Details.Should().Contain(d => d.Key == "Path" && d.Value == "/secret");
    }

    [Fact]
    public void BuildPathPermissionDeniedDiagnostic_NullReason_ReturnsDefault()
    {
        var diag = SearchToolHandlers.BuildPathPermissionDeniedDiagnostic("/secret", null);
        diag.FormattedMessage.Should().Contain("Access denied");
    }

    [Fact]
    public void BuildPathPermissionAskDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildPathPermissionAskDiagnostic("/confirm", null);
        diag.Reason.Should().Be("SearchPathPermissionAsk");
        diag.FormattedMessage.Should().Contain("requires confirmation");
    }

    [Fact]
    public void BuildUncPathDeniedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildUncPathDeniedDiagnostic("\\\\server\\share");
        diag.Reason.Should().Be("SearchUncPathDenied");
        diag.FormattedMessage.Should().Contain("UNC path");
    }

    [Fact]
    public void BuildSuspiciousPathDiagnostic_ReturnsCorrectStructure()
    {
        var diag = SearchToolHandlers.BuildSuspiciousPathDiagnostic("../../etc/passwd");
        diag.Reason.Should().Be("SearchSuspiciousPath");
        diag.FormattedMessage.Should().Contain("suspicious pattern");
        diag.Details.Should().Contain(d => d.Key == "Path" && d.Value == "../../etc/passwd");
    }
}
