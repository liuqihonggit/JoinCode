namespace Core.Tests.ChatCommands;


/// <summary>
/// /ultraplan 命令单元测试
/// 验证 --execute 标志应触发 PlanService.ExecutePlanWithResultAsync
/// </summary>
public class UltraplanCommandTests {
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IPlanService> _planServiceMock;
    private readonly UltraplanCommand _command;

    public UltraplanCommandTests() {
        _chatServiceMock = new Mock<IChatService>();
        _chatServiceMock.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("模拟计划文本");

        _planServiceMock = new Mock<IPlanService>();
        _planServiceMock.Setup(p => p.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanExecutionResult {
                Success = true,
                Result = "模拟执行结果",
                ExecutionTimeMs = 100
            });

        _command = new UltraplanCommand();
    }

    private ChatCommandContext CreateContext(string args) => new() {
        Arguments = args,
        CancellationToken = CancellationToken.None,
        Services = new CommandServiceProvider(new CommandServices {
            ChatService = _chatServiceMock.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = _planServiceMock.Object,
            FileSystem = TestFileSystem.Current,
        }),
    };

    [Fact]
    public async Task Execute_WithExecuteFlag_ShouldInvokePlanServiceExecutePlanWithResultAsync() {
        var context = CreateContext("test-goal --execute");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(
                It.Is<string>(s => s.Contains("test-goal", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithExecuteFlag_ShouldNotInvokeChatServiceSendMessage() {
        var context = CreateContext("test-goal --execute");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _chatServiceMock.Verify(
            c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_WithoutExecuteFlag_ShouldInvokeChatServiceSendMessage() {
        var context = CreateContext("test-goal");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _chatServiceMock.Verify(
            c => c.SendMessageAsync(
                It.Is<string>(s => s.Contains("test-goal", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithoutExecuteFlag_ShouldNotInvokePlanService() {
        var context = CreateContext("test-goal");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_WithExecuteFlagAndSteps_ShouldPassStepsToPlanService() {
        var context = CreateContext("test-goal --steps 10 --execute");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(
                It.Is<string>(s => s.Contains("test-goal", StringComparison.OrdinalIgnoreCase)
                                && s.Contains("10", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithExecuteFlagAndDefaultSteps_ShouldPassDefaultStepsToPlanService() {
        var context = CreateContext("test-goal --execute");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(
                It.Is<string>(s => s.Contains("test-goal", StringComparison.OrdinalIgnoreCase)
                                && s.Contains("5", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WhenPlanServiceThrows_ShouldReturnContinueAndNotCrash() {
        _planServiceMock
            .Setup(p => p.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("模拟执行失败"));

        var context = CreateContext("test-goal --execute");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public void Name_ShouldReturnUltraplan() {
        _command.Name.Should().Be("ultraplan");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty() {
        _command.Description.Should().NotBeEmpty();
    }

    [Fact]
    public void Usage_ShouldNotBeEmpty() {
        _command.Usage.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("--execute")]
    [InlineData("-e")]
    public async Task Execute_WithExecuteAlias_ShouldInvokePlanService(string alias) {
        var context = CreateContext($"goal {alias}");

        await _command.ExecuteAsync(context).ConfigureAwait(true);

        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(
                It.Is<string>(s => s.Contains("goal", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithEmptyArguments_ShouldShowHelpAndContinue() {
        var context = CreateContext("");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        _planServiceMock.Verify(
            p => p.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}