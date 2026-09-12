namespace Hands.Tests.Shell;

/// <summary>
/// ShellTimeoutKeywordMiddleware 单元测试 — 验证脚本内等待关键字的超时自动调整与冲突报错
/// </summary>
public class ShellTimeoutKeywordMiddlewareTests
{
    private static ShellExecutionConfig DefaultConfig => new() { TimeoutKeywordBufferSeconds = 30, DefaultTimeoutSeconds = 120 };

    [Fact]
    public async Task NoKeyword_PassesThroughWithoutModification()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("echo hello");

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.OverrideTimeout.Should().BeNull();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_NoUserTimeout_AutoExtendsOverrideTimeout()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 100", timeout: null);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().Be(130_000);
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_UserTimeoutSufficient_PassesThrough()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: 120_000);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().BeNull();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_UserTimeoutInsufficient_ReturnsErrorToAi()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: 30_000);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.OverrideTimeout.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_UserTimeoutInsufficient_ErrorContainsWaitAndUserSeconds()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: 30_000);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        var text = context.Result!.GetFirstText();
        text.Should().Contain("60 秒等待");
        text.Should().Contain("30 秒");
        text.Should().Contain("90 秒");
    }

    [Fact]
    public async Task Keyword_OverrideTimeoutAlreadySet_ExtendsIfInsufficient()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: null);
        context.OverrideTimeout = 30_000;

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().Be(90_000);
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_OverrideTimeoutAlreadySet_PassesIfSufficient()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: null);
        context.OverrideTimeout = 120_000;

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().Be(120_000);
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Keyword_UserTimeoutInsufficient_DoesNotCallNext()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("sleep 60", timeout: 30_000);

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PowerShellStartSleep_NoUserTimeout_AutoExtends()
    {
        var sut = new ShellTimeoutKeywordMiddleware(DefaultConfig);
        var context = CreateContext("Start-Sleep -Seconds 200", timeout: null);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().Be(230_000);
    }

    [Fact]
    public async Task CustomBuffer_AppliedToRequiredTimeout()
    {
        var config = new ShellExecutionConfig { TimeoutKeywordBufferSeconds = 10, DefaultTimeoutSeconds = 120 };
        var sut = new ShellTimeoutKeywordMiddleware(config);
        var context = CreateContext("sleep 200", timeout: null);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.OverrideTimeout.Should().Be(210_000);
    }

    [Fact]
    public void BuildConflictDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellTimeoutKeywordMiddleware.BuildConflictDiagnostic("sleep 60", 60, 30, 90);

        diagnostic.Reason.Should().Be("脚本超时关键字冲突");
        diagnostic.FormattedMessage.Should().Contain("60 秒等待");
        diagnostic.FormattedMessage.Should().Contain("30 秒");
        diagnostic.Details.Should().Contain(d => d.Key == "wait_seconds" && d.Value == "60");
        diagnostic.Details.Should().Contain(d => d.Key == "user_timeout_seconds" && d.Value == "30");
        diagnostic.Details.Should().Contain(d => d.Key == "required_timeout_seconds" && d.Value == "90");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    private static ShellPipelineContext CreateContext(string command, int? timeout = null)
    {
        var provider = new Mock<ISystemActuator>();
        provider.SetupGet(x => x.Kind).Returns(SystemActuatorKind.Bash);

        return new ShellPipelineContext
        {
            Command = command,
            Provider = provider.Object,
            Timeout = timeout,
            TimeoutPolicy = ToolTimeoutPolicy.None,
        };
    }
}
