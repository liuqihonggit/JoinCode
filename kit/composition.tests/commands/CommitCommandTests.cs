namespace Composition.Tests.Commands;

/// <summary>
/// /commit 命令单元测试 — 覆盖两阶段渐进式披露(首次返回说明不执行,二次确认执行)
/// <para>静态状态隔离:每个测试用唯一 SessionId(Guid),避免 ReadConfirmedSessions 静态字典跨测试污染</para>
/// </summary>
public sealed class CommitCommandTests
{
    private static CommandServices CreateCommandServices(IGitCommandRunner gitRunner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gitRunner);
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
        };
    }

    private static Mock<IGitCommandRunner> CreateMockGitRunner(string statusOutput = "")
    {
        var mock = new Mock<IGitCommandRunner>();
        mock.Setup(r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = statusOutput, ExitCode = 0 });
        return mock;
    }

    [Fact]
    public void Name_Should_Be_commit()
    {
        var cmd = new CommitCommand();
        cmd.Name.Should().Be("commit");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new CommitCommand();
        cmd.Usage.Should().StartWith("/commit");
    }

    [Fact]
    public async Task Execute_FirstCall_WithSessionId_ShouldReturnContinue_WithoutExecutingGit()
    {
        var sessionId = $"test-first-{Guid.NewGuid():N}";
        var gitRunner = CreateMockGitRunner();
        var cmd = new CommitCommand();
        var context = new ChatCommandContext
        {
            Arguments = "",
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(gitRunner.Object)),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue("首次调用应返回 Continue");
        gitRunner.Verify(
            r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "首次调用应仅显示说明,不执行任何 git 命令");
    }

    [Fact]
    public async Task Execute_SecondCall_WithSessionId_ShouldExecuteGit()
    {
        var sessionId = $"test-second-{Guid.NewGuid():N}";
        var gitRunner = CreateMockGitRunner();
        var cmd = new CommitCommand();
        var services = new CommandServiceProvider(CreateCommandServices(gitRunner.Object));

        var context1 = new ChatCommandContext
        {
            Arguments = "",
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
        await cmd.ExecuteAsync(context1).ConfigureAwait(true);

        var context2 = new ChatCommandContext
        {
            Arguments = "",
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
        var result = await cmd.ExecuteAsync(context2).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        gitRunner.Verify(
            r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "二次调用应确认执行 git 命令");
    }

    [Fact]
    public async Task Execute_WithoutSessionId_ShouldSkipDisclosure_AndExecuteGit()
    {
        var gitRunner = CreateMockGitRunner();
        var cmd = new CommitCommand();
        var context = new ChatCommandContext
        {
            Arguments = "",
            SessionId = "",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(gitRunner.Object)),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        gitRunner.Verify(
            r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "无 SessionId 应跳过渐进式披露,直接执行 git");
    }

    [Fact]
    public async Task Execute_SecondCall_WithinWindow_ShouldExecuteGit()
    {
        var sessionId = $"test-window-{Guid.NewGuid():N}";
        var gitRunner = CreateMockGitRunner();
        var cmd = new CommitCommand();
        var services = new CommandServiceProvider(CreateCommandServices(gitRunner.Object));

        var context1 = new ChatCommandContext
        {
            Arguments = "",
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
        await cmd.ExecuteAsync(context1).ConfigureAwait(true);

        var context2 = new ChatCommandContext
        {
            Arguments = "",
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
        var result = await cmd.ExecuteAsync(context2).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        gitRunner.Verify(
            r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "60s 窗口内二次调用应执行 git");
    }
}
