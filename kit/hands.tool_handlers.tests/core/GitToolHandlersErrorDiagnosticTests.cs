namespace Hands.Tests.ToolHandlers;

/// <summary>
/// GitToolHandlers 错误诊断方法单元测试
/// </summary>
public class GitToolHandlersErrorDiagnosticTests
{
    [Fact]
    public void BuildGitStatusFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitStatusFailedDiagnostic("not a git repo");
        diag.Reason.Should().Be("GitStatusFailed");
        diag.FormattedMessage.Should().Be("Git status failed:\nnot a git repo");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "not a git repo");
    }

    [Fact]
    public void BuildPathEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildPathEmptyDiagnostic();
        diag.Reason.Should().Be("GitPathEmpty");
        diag.FormattedMessage.Should().Be("path cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "path");
    }

    [Fact]
    public void BuildGitAddFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitAddFailedDiagnostic("pathspec did not match");
        diag.Reason.Should().Be("GitAddFailed");
        diag.FormattedMessage.Should().Be("Git add failed:\npathspec did not match");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "pathspec did not match");
    }

    [Fact]
    public void BuildMessageEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildMessageEmptyDiagnostic();
        diag.Reason.Should().Be("GitMessageEmpty");
        diag.FormattedMessage.Should().Be("message cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "message");
    }

    [Fact]
    public void BuildGitCommitFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitCommitFailedDiagnostic("nothing to commit");
        diag.Reason.Should().Be("GitCommitFailed");
        diag.FormattedMessage.Should().Be("Git commit failed:\nnothing to commit");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "nothing to commit");
    }

    [Fact]
    public void BuildGitPushFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitPushFailedDiagnostic("permission denied");
        diag.Reason.Should().Be("GitPushFailed");
        diag.FormattedMessage.Should().Be("Git push failed:\npermission denied");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "permission denied");
    }

    [Fact]
    public void BuildGitPullFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitPullFailedDiagnostic("merge conflict");
        diag.Reason.Should().Be("GitPullFailed");
        diag.FormattedMessage.Should().Be("Git pull failed:\nmerge conflict");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "merge conflict");
    }

    [Fact]
    public void BuildGitLogValidationDiagnostic_ReturnsCorrectStructure()
    {
        const string validationError = "count 必须在 1-1000 之间";
        var diag = GitToolHandlers.BuildGitLogValidationDiagnostic(validationError);
        diag.Reason.Should().Be("GitLogValidationError");
        diag.FormattedMessage.Should().Be(validationError);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == validationError);
    }

    [Fact]
    public void BuildGitLogFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitLogFailedDiagnostic("no commits yet");
        diag.Reason.Should().Be("GitLogFailed");
        diag.FormattedMessage.Should().Be("Git log failed:\nno commits yet");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "no commits yet");
    }

    [Fact]
    public void BuildGitDiffFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitDiffFailedDiagnostic("bad revision");
        diag.Reason.Should().Be("GitDiffFailed");
        diag.FormattedMessage.Should().Be("Git diff failed:\nbad revision");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "bad revision");
    }

    [Fact]
    public void BuildBranchNameEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildBranchNameEmptyDiagnostic();
        diag.Reason.Should().Be("GitBranchNameEmpty");
        diag.FormattedMessage.Should().Be("branch_name cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "branch_name");
    }

    [Fact]
    public void BuildUnsupportedBranchOperationDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildUnsupportedBranchOperationDiagnostic("rename");
        diag.Reason.Should().Be("GitUnsupportedOperation");
        diag.FormattedMessage.Should().Be("Unsupported operation: rename");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "rename");
    }

    [Fact]
    public void BuildUnsupportedBranchOperationDiagnostic_NullOperation_ReturnsNullText()
    {
        var diag = GitToolHandlers.BuildUnsupportedBranchOperationDiagnostic(null);
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "(null)");
    }

    [Fact]
    public void BuildGitBranchFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitBranchFailedDiagnostic("switch", "branch not found");
        diag.Reason.Should().Be("GitBranchFailed");
        diag.FormattedMessage.Should().Be("Git branch switch failed:\nbranch not found");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "switch");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "branch not found");
    }

    [Fact]
    public void BuildUrlEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildUrlEmptyDiagnostic();
        diag.Reason.Should().Be("GitUrlEmpty");
        diag.FormattedMessage.Should().Be("url cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "url");
    }

    [Fact]
    public void BuildGitCloneFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = GitToolHandlers.BuildGitCloneFailedDiagnostic("repository not found");
        diag.Reason.Should().Be("GitCloneFailed");
        diag.FormattedMessage.Should().Be("Git clone failed:\nrepository not found");
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == "repository not found");
    }

    [Fact]
    public void BuildSecurityScanBlockedDiagnostic_ReturnsCorrectStructure()
    {
        const string report = "Blocked: secret detected";
        var diag = GitToolHandlers.BuildSecurityScanBlockedDiagnostic(report);
        diag.Reason.Should().Be("GitSecurityScanBlocked");
        diag.FormattedMessage.Should().Be(report);
        diag.Details.Should().Contain(d => d.Key == "Report" && d.Value == report);
    }
}
