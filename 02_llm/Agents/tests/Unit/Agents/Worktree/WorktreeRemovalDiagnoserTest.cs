namespace JoinCode.Agents.Tests.Worktree;

/// <summary>
/// WorktreeRemovalDiagnoser 单元测试 — 删除失败时状态机诊断根因（进程占用/路径/权限/git错误）。
/// </summary>
public class WorktreeRemovalDiagnoserTest
{
    /// <summary>
    /// 删除成功时状态为 Succeeded，无根因。
    /// </summary>
    [Fact]
    public void Diagnose_WhenSucceeds_StateSucceeded()
    {
        var result = new WorktreeRemovalResult
        {
            State = RemovalState.Succeeded,
            Reason = RemovalFailureReason.None
        };

        result.State.Should().Be(RemovalState.Succeeded);
        result.Reason.Should().Be(RemovalFailureReason.None);
    }

    /// <summary>
    /// 路径不存在时诊断为 PathNotFound。
    /// </summary>
    [Fact]
    public async Task DiagnoseAsync_WhenPathNotExists_ReturnsPathNotFound()
    {
        var fs = new InMemoryFileOperationService();
        var diagnoser = new WorktreeRemovalDiagnoser(fs);

        var result = await diagnoser.DiagnoseAsync(
            "D:\\nonexistent\\worktree",
            "git worktree remove failed: not a working tree",
            CancellationToken.None);

        result.State.Should().Be(RemovalState.Diagnosed);
        result.Reason.Should().Be(RemovalFailureReason.PathNotFound);
    }

    /// <summary>
    /// 路径存在但 git 错误含 "being used" 时诊断为 ProcessOccupied。
    /// </summary>
    [Fact]
    public async Task DiagnoseAsync_WhenErrorContainsBeingUsed_ReturnsProcessOccupied()
    {
        var fs = new InMemoryFileOperationService();
        fs.CreateDirectory("D:\\project\\w1\\.jcc\\worktrees\\agent-x");
        var diagnoser = new WorktreeRemovalDiagnoser(fs);

        var result = await diagnoser.DiagnoseAsync(
            "D:\\project\\w1\\.jcc\\worktrees\\agent-x",
            "fatal: unable to remove: being used by another process",
            CancellationToken.None);

        result.State.Should().Be(RemovalState.Diagnosed);
        result.Reason.Should().Be(RemovalFailureReason.ProcessOccupied);
    }

    /// <summary>
    /// 路径存在但 git 错误含 "Permission denied" 时诊断为 PermissionDenied。
    /// </summary>
    [Fact]
    public async Task DiagnoseAsync_WhenErrorContainsPermissionDenied_ReturnsPermissionDenied()
    {
        var fs = new InMemoryFileOperationService();
        fs.CreateDirectory("D:\\project\\w1\\.jcc\\worktrees\\agent-x");
        var diagnoser = new WorktreeRemovalDiagnoser(fs);

        var result = await diagnoser.DiagnoseAsync(
            "D:\\project\\w1\\.jcc\\worktrees\\agent-x",
            "fatal: Permission denied",
            CancellationToken.None);

        result.State.Should().Be(RemovalState.Diagnosed);
        result.Reason.Should().Be(RemovalFailureReason.PermissionDenied);
    }

    /// <summary>
    /// 路径存在且错误无已知模式时诊断为 GitError。
    /// </summary>
    [Fact]
    public async Task DiagnoseAsync_WhenUnknownError_ReturnsGitError()
    {
        var fs = new InMemoryFileOperationService();
        fs.CreateDirectory("D:\\project\\w1\\.jcc\\worktrees\\agent-x");
        var diagnoser = new WorktreeRemovalDiagnoser(fs);

        var result = await diagnoser.DiagnoseAsync(
            "D:\\project\\w1\\.jcc\\worktrees\\agent-x",
            "fatal: some unknown git error",
            CancellationToken.None);

        result.State.Should().Be(RemovalState.Diagnosed);
        result.Reason.Should().Be(RemovalFailureReason.GitError);
    }

    /// <summary>
    /// 错误消息为空且路径存在时诊断为 Unknown。
    /// </summary>
    [Fact]
    public async Task DiagnoseAsync_WhenEmptyErrorAndPathExists_ReturnsUnknown()
    {
        var fs = new InMemoryFileOperationService();
        fs.CreateDirectory("D:\\project\\w1\\.jcc\\worktrees\\agent-x");
        var diagnoser = new WorktreeRemovalDiagnoser(fs);

        var result = await diagnoser.DiagnoseAsync(
            "D:\\project\\w1\\.jcc\\worktrees\\agent-x",
            "",
            CancellationToken.None);

        result.State.Should().Be(RemovalState.Diagnosed);
        result.Reason.Should().Be(RemovalFailureReason.Unknown);
    }
}
