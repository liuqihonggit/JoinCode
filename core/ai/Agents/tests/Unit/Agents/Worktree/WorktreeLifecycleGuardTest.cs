namespace JoinCode.Agents.Tests.Worktree;

/// <summary>
/// WorktreeLifecycleGuard 单元测试 — 验证路径锁定、标准 Dispose 模式、终结器兜底。
/// <para>核心设计：构造时锁定 path，Dispose/析构复用同一引用，禁止二次计算路径。</para>
/// </summary>
public class WorktreeLifecycleGuardTest
{
    private const string MainPath = "D:\\project\\w1";
    private const string WorktreePath = "D:\\project\\w1\\.jcc\\worktrees\\agent-abc";

    private static IFileOperationService CreateFs(bool worktreeExists = false)
    {
        var fs = new InMemoryFileOperationService();
        if (worktreeExists)
        {
            fs.CreateDirectory(WorktreePath);
        }
        return fs;
    }

    /// <summary>
    /// 路径等于主仓库时必须抛异常 — 防止误删主仓库代码。
    /// </summary>
    [Fact]
    public void Ctor_WhenSameAsMain_Throws()
    {
        var act = () => new WorktreeLifecycleGuard(MainPath, MainPath, CreateFs());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("worktreePath");
    }

    /// <summary>
    /// 路径不同时构造成功，WorktreePath 返回锁定的路径。
    /// </summary>
    [Fact]
    public void Ctor_WhenDifferent_LocksPath()
    {
        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs());

        guard.WorktreePath.Should().Be(WorktreePath);
        guard.MainPath.Should().Be(MainPath);
    }

    /// <summary>
    /// 路径大小写不同时按规范化比较 — 容忍大小写差异但仍然检测主路径。
    /// </summary>
    [Fact]
    public void Ctor_WhenCaseOnlyDifferent_Throws()
    {
        var act = () => new WorktreeLifecycleGuard("d:\\project\\w1", "D:\\Project\\W1", CreateFs());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 路径为 null 或空白时抛 ArgumentException — 参数校验。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WhenWorktreePathBlank_Throws(string? worktreePath)
    {
        var act = () => new WorktreeLifecycleGuard(worktreePath!, MainPath, CreateFs());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 主路径为 null 或空白时抛 ArgumentException — 参数校验。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WhenMainPathBlank_Throws(string? mainPath)
    {
        var act = () => new WorktreeLifecycleGuard(WorktreePath, mainPath!, CreateFs());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 路径规范化后比较 — 处理尾部分隔符。
    /// </summary>
    [Fact]
    public void Ctor_WhenPathDiffersOnlyByTrailingSeparator_Throws()
    {
        var act = () => new WorktreeLifecycleGuard("D:\\project\\w1\\", MainPath, CreateFs());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// gitRunner 为 null 时 ReleaseAsync 返回失败 — 不执行删除。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenGitRunnerNull_ReturnsFail()
    {
        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs(worktreeExists: true));

        var result = await guard.ReleaseAsync(force: true, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("gitRunner");
    }

    /// <summary>
    /// 路径不存在时 ReleaseAsync 返回失败 — 宽容处理已清理的 worktree。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenPathNotExists_ReturnsFail()
    {
        var gitRunner = new Mock<IGitCommandRunner>();
        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs(worktreeExists: false), gitRunner.Object);

        var result = await guard.ReleaseAsync(force: true, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    /// <summary>
    /// 路径存在且 gitRunner 返回成功时 ReleaseAsync 成功 — 正常删除流程。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_WhenPathExistsAndGitSucceeds_ReturnsOk()
    {
        var fs = CreateFs(worktreeExists: true);
        var gitRunner = new Mock<IGitCommandRunner>();
        gitRunner
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, ExitCode = 0 });

        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, fs, gitRunner.Object, branchName: "worktree-agent-abc");

        var result = await guard.ReleaseAsync(force: true, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Forced.Should().BeTrue();

        gitRunner.Verify(x => x.ExecuteAsync(
            It.Is<string>(s => s.Contains("worktree remove") && s.Contains("--force") && s.Contains(WorktreePath)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        gitRunner.Verify(x => x.ExecuteAsync(
            It.Is<string>(s => s.Contains("branch -D") && s.Contains("worktree-agent-abc")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// DisposeAsync 后 ReleaseAsync 抛 ObjectDisposedException — 标准资源生命周期管理。
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var gitRunner = new Mock<IGitCommandRunner>();
        gitRunner
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, ExitCode = 0 });

        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs(worktreeExists: true), gitRunner.Object);
        await guard.DisposeAsync();

        var act = async () => await guard.ReleaseAsync(force: true, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// DisposeAsync 可多次调用不报错 — 幂等释放。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_NoThrow()
    {
        var gitRunner = new Mock<IGitCommandRunner>();
        gitRunner
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, ExitCode = 0 });

        var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs(worktreeExists: true), gitRunner.Object);

        var act = async () =>
        {
            await guard.DisposeAsync();
            await guard.DisposeAsync();
            await guard.DisposeAsync();
        };

        await act.Should().NotThrowAsync();
        gitRunner.Verify(x => x.ExecuteAsync(
            It.Is<string>(s => s.Contains("worktree remove")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// await using 语法自动 DisposeAsync — 验证标准 IAsyncDisposable 用法。
    /// </summary>
    [Fact]
    public async Task AwaitUsing_AutoDisposeAsync_ExecutesGitRemove()
    {
        var gitRunner = new Mock<IGitCommandRunner>();
        gitRunner
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, ExitCode = 0 });

        {
            await using var guard = new WorktreeLifecycleGuard(WorktreePath, MainPath, CreateFs(worktreeExists: true), gitRunner.Object);
        }

        gitRunner.Verify(x => x.ExecuteAsync(
            It.Is<string>(s => s.Contains("worktree remove") && s.Contains(WorktreePath)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
