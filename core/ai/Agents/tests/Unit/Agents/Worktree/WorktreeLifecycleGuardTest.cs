namespace JoinCode.Agents.Tests.Worktree;

/// <summary>
/// WorktreeLifecycleGuard 单元测试 — 专注 worktree 路径验证与释放/删除安全防护。
/// <para>bug 背景：worktree 创建在当前目录（如 D:\project\w1），但清理时 FindGitRootAsync 解析到主仓库
/// （D:\project\JoinCode），导致 git worktree remove 命令路径不匹配，清理失败，worktree 静默残留。</para>
/// <para>修复：新建专注类 WorktreeLifecycleGuard，验证 worktree 路径不等于主仓库路径，统一释放/删除逻辑。</para>
/// </summary>
public class WorktreeLifecycleGuardTest
{
    private static WorktreeLifecycleGuard CreateGuard() => new(new InMemoryFileOperationService());

    /// <summary>
    /// 路径等于主仓库时必须抛异常 — 防止误删主仓库代码。
    /// </summary>
    [Fact]
    public void EnsureNotMainPath_WhenSameAsMain_Throws()
    {
        var guard = CreateGuard();
        var mainPath = "D:\\project\\w1";

        var act = () => guard.EnsureNotMainPath(mainPath, mainPath);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("worktreePath");
    }

    /// <summary>
    /// 路径不同时不抛异常 — 正常场景。
    /// </summary>
    [Fact]
    public void EnsureNotMainPath_WhenDifferent_NoThrow()
    {
        var guard = CreateGuard();

        var act = () => guard.EnsureNotMainPath("D:\\project\\w1\\.jcc\\worktrees\\agent-abc", "D:\\project\\w1");

        act.Should().NotThrow();
    }

    /// <summary>
    /// 路径大小写不同时按规范化比较 — 容忍大小写差异但仍然检测主路径。
    /// </summary>
    [Fact]
    public void EnsureNotMainPath_WhenCaseOnlyDifferent_Throws()
    {
        var guard = CreateGuard();
        var mainPath = "D:\\Project\\W1";

        var act = () => guard.EnsureNotMainPath("d:\\project\\w1", mainPath);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 路径为 null 或空白时抛 ArgumentException — 参数校验。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureNotMainPath_WhenWorktreePathBlank_Throws(string? worktreePath)
    {
        var guard = CreateGuard();

        var act = () => guard.EnsureNotMainPath(worktreePath!, "D:\\project\\w1");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 主路径为 null 或空白时抛 ArgumentException — 参数校验。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureNotMainPath_WhenMainPathBlank_Throws(string? mainPath)
    {
        var guard = CreateGuard();

        var act = () => guard.EnsureNotMainPath("D:\\project\\w1\\.jcc\\worktrees\\agent-abc", mainPath!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 路径规范化后比较 — 处理尾部分隔符、相对路径等。
    /// </summary>
    [Fact]
    public void EnsureNotMainPath_WhenPathDiffersOnlyByTrailingSeparator_Throws()
    {
        var guard = CreateGuard();
        var mainPath = "D:\\project\\w1";

        var act = () => guard.EnsureNotMainPath("D:\\project\\w1\\", mainPath);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 删除时路径是主仓库必须抛异常 — 安全防护，防止误删主仓库。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_WhenPathIsMain_Throws()
    {
        var guard = CreateGuard();
        var mainPath = "D:\\project\\w1";

        var act = async () => await guard.RemoveAsync(mainPath, mainPath, force: true, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// 删除时路径不存在返回 false 而非抛异常 — 宽容处理已清理的 worktree。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_WhenPathNotExists_ReturnsFalse()
    {
        var guard = CreateGuard();

        var result = await guard.RemoveAsync("D:\\nonexistent\\worktree", "D:\\project\\w1", force: true, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    /// <summary>
    /// 释放时有变更保留 worktree — 返回 Kept=true。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenHasChanges_KeepsWorktree()
    {
        var guard = CreateGuard();

        var result = await guard.ReleaseAsync("D:\\project\\w1\\.jcc\\worktrees\\agent-abc", "D:\\project\\w1", hasChanges: true, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Kept.Should().BeTrue();
        result.Reason.Should().Be("has_changes");
    }

    /// <summary>
    /// 释放时无变更且路径是主仓库必须抛异常 — 安全防护。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenNoChangesAndPathIsMain_Throws()
    {
        var guard = CreateGuard();
        var mainPath = "D:\\project\\w1";

        var act = async () => await guard.ReleaseAsync(mainPath, mainPath, hasChanges: false, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
