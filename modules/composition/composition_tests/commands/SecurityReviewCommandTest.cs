namespace Composition.Tests.Commands;

/// <summary>
/// /security-review 命令单元测试 — 覆盖 Git 执行器未注册降级/无变更提示/
/// 有变更主路径(git 四连查询+prompt 上下文段+LLM 结果渲染)/LLM 异常降级/
/// unstaged 为空时回退 staged diff
/// <para>IGitCommandRunner 经 DI 注入按参数桩化;ChatService 用 Moq 桩经本地构造 CommandServices 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class SecurityReviewCommandTest {
    private static Mock<IGitCommandRunner> CreateGitRunner(
        string status = "M  src/a.cs",
        string diffNames = "src/a.cs",
        string log = "abc123 fix",
        string diff = "@@ -1 +1 @@\n+changed",
        string stagedDiff = "") {
        var runner = new Mock<IGitCommandRunner>();
        runner.Setup(r => r.ExecuteAsync("status --porcelain", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = status });
        runner.Setup(r => r.ExecuteAsync("diff --name-only", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = diffNames });
        runner.Setup(r => r.ExecuteAsync("log --oneline -10", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = log });
        runner.Setup(r => r.ExecuteAsync("diff", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = diff });
        runner.Setup(r => r.ExecuteAsync("diff --cached", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = stagedDiff });
        return runner;
    }

    private static (SecurityReviewCommand Command, Mock<IGitCommandRunner> Runner, Mock<IChatService> Chat, ChatCommandContext Context) Arrange(
        Mock<IGitCommandRunner>? runner = null,
        bool withRunner = true,
        string? llmResult = "SEC-1: 注入风险",
        Exception? llmError = null) {
        var chat = new Mock<IChatService>();
        if (llmError is not null) {
            chat.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(llmError);
        } else {
            chat.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResult ?? string.Empty);
        }

        var effectiveRunner = runner ?? CreateGitRunner();
        var services = new ServiceCollection();
        if (withRunner) services.AddSingleton(effectiveRunner.Object);
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(commandServices);
        return (new SecurityReviewCommand(), effectiveRunner, chat, context);
    }

    [Fact]
    public async Task Execute_WithoutGitRunner_ShowsNotRegistered() {
        var (cmd, runner, chat, context) = Arrange(withRunner: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("Git 命令执行器未注册，无法执行 /security-review");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_NoChanges_ShowsNothingToReview() {
        var (cmd, runner, chat, context) = Arrange(runner: CreateGitRunner(
            status: "", diffNames: "", log: "", diff: "", stagedDiff: ""));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("没有要审查的变更");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无变更时不应调用 LLM");
    }

    [Fact]
    public async Task Execute_WithChanges_RunsGitQueriesAndRendersLlmResult() {
        var (cmd, runner, chat, context) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        runner.Verify(r => r.ExecuteAsync("status --porcelain", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.ExecuteAsync("diff --name-only", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.ExecuteAsync("log --oneline -10", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.ExecuteAsync("diff", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("正在执行安全审查...");
        output.Should().Contain("SEC-1: 注入风险", "LLM 审查结果应直接渲染到终端");
        chat.Verify(c => c.SendMessageAsync(
            It.Is<string>(p => p.Contains("GIT STATUS") && p.Contains("src/a.cs") && p.Contains("abc123 fix")),
            It.IsAny<CancellationToken>()), Times.Once, "prompt 应包含 git 状态/文件/提交上下文");
    }

    [Fact]
    public async Task Execute_EmptyUnstagedDiff_FallsBackToStagedDiff() {
        var runner = CreateGitRunner(status: "M  src/b.cs", diff: "", stagedDiff: "@@ staged @@");
        var (cmd, runnerMock, chat, context) = Arrange(runner: runner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        runnerMock.Verify(r => r.ExecuteAsync("diff --cached", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once, "unstaged diff 为空时应回退查询 staged diff");
        chat.Verify(c => c.SendMessageAsync(
            It.Is<string>(p => p.Contains("@@ staged @@")), It.IsAny<CancellationToken>()), Times.Once,
            "prompt 的 DIFF CONTENT 应使用 staged diff");
    }

    [Fact]
    public async Task Execute_LlmThrows_ShowsErrorWithExceptionMessage() {
        var (cmd, _, chat, context) = Arrange(llmError: new InvalidOperationException("model timeout"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("LLM 异常不应中断会话");
        output.Should().Contain("安全审查失败");
        output.Should().Contain("model timeout");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new SecurityReviewCommand();
        cmd.Name.Should().Be("security-review");
        cmd.Usage.Should().Be("/security-review");
        cmd.ExposeToMcp.Should().BeTrue();
    }
}
