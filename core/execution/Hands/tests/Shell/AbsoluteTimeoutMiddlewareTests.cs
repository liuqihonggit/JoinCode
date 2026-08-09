namespace Hands.Tests.Shell;

/// <summary>
/// AbsoluteTimeoutMiddleware 单元测试 — 验证超时策略驱动的绝对超时中间件
/// </summary>
public class AbsoluteTimeoutMiddlewareTests
{
    private static ShellExecutionConfig DefaultConfig => new() { AbsoluteTimeoutSeconds = 120 };

    [Fact]
    public async Task NonePolicy_DoesNotEnforceTimeout()
    {
        var sut = new AbsoluteTimeoutMiddleware(DefaultConfig);
        var context = CreateContext(ToolTimeoutPolicy.None);

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task AbsoluteTwoMinutes_NextCompletes_ResultNotModified()
    {
        var sut = new AbsoluteTimeoutMiddleware(DefaultConfig);
        var context = CreateContext(ToolTimeoutPolicy.AbsoluteTwoMinutes);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task AbsoluteTwoMinutes_TimeoutFires_SetsErrorResultWithResumeHint()
    {
        var config = new ShellExecutionConfig { AbsoluteTimeoutSeconds = 1 };
        var sut = new AbsoluteTimeoutMiddleware(config);
        var context = CreateContext(ToolTimeoutPolicy.AbsoluteTwoMinutes);

        await sut.InvokeAsync(context, async (_, ct) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { throw; }
        }, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        var text = context.Result.GetFirstText();
        text.Should().Contain("超时");
        text.Should().Contain("resume_timed_out_task");
        text.Should().Contain(context.Command);
    }

    [Fact]
    public async Task ConfigZero_FallsBackToPolicyValue()
    {
        var config = new ShellExecutionConfig { AbsoluteTimeoutSeconds = 0 };
        var sut = new AbsoluteTimeoutMiddleware(config);
        var context = CreateContext(ToolTimeoutPolicy.AbsoluteTwoMinutes);

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ConfigOverride_UsesConfigValue()
    {
        var config = new ShellExecutionConfig { AbsoluteTimeoutSeconds = 1 };
        var sut = new AbsoluteTimeoutMiddleware(config);
        var context = CreateContext(ToolTimeoutPolicy.AbsoluteTwoMinutes);

        await sut.InvokeAsync(context, async (_, ct) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { throw; }
        }, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        var text = context.Result.GetFirstText();
        text.Should().Contain("1秒");
    }

    [Fact]
    public async Task ExternalCancellation_PropagatesNormally()
    {
        var sut = new AbsoluteTimeoutMiddleware(DefaultConfig);
        var context = CreateContext(ToolTimeoutPolicy.AbsoluteTwoMinutes);
        using var cts = new CancellationTokenSource();

        await sut.Invoking(async x => await x.InvokeAsync(context, async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(100, ct);
        }, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static ShellPipelineContext CreateContext(ToolTimeoutPolicy policy)
    {
        var provider = new Mock<ISystemActuator>();
        provider.SetupGet(x => x.Kind).Returns(SystemActuatorKind.Bash);

        return new ShellPipelineContext
        {
            Command = "echo test",
            Provider = provider.Object,
            TimeoutPolicy = policy,
        };
    }
}

/// <summary>
/// ToolTimeoutPolicy 单元测试 — 验证超时策略记录的预设值
/// </summary>
public class ToolTimeoutPolicyTests
{
    [Fact]
    public void None_HasNoAbsoluteTimeout()
    {
        var policy = ToolTimeoutPolicy.None;

        policy.AbsoluteTimeoutSeconds.Should().BeNull();
        policy.SupportsResume.Should().BeFalse();
        policy.KillOnTimeout.Should().BeFalse();
    }

    [Fact]
    public void AbsoluteTwoMinutes_HasCorrectValues()
    {
        var policy = ToolTimeoutPolicy.AbsoluteTwoMinutes;

        policy.AbsoluteTimeoutSeconds.Should().Be(120);
        policy.SupportsResume.Should().BeTrue();
        policy.KillOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void None_IsImmutable()
    {
        var policy1 = ToolTimeoutPolicy.None;
        var policy2 = ToolTimeoutPolicy.None;

        policy1.Should().Be(policy2);
    }

    [Fact]
    public void AbsoluteTwoMinutes_IsImmutable()
    {
        var policy1 = ToolTimeoutPolicy.AbsoluteTwoMinutes;
        var policy2 = ToolTimeoutPolicy.AbsoluteTwoMinutes;

        policy1.Should().Be(policy2);
    }
}

/// <summary>
/// ToolHandlerGroupBase 继承体系单元测试 — 验证组基类的超时策略
/// </summary>
public class ToolHandlerGroupTests
{
    [Fact]
    public void OneShotCommandGroup_HasAbsoluteTwoMinutesPolicy()
    {
        var group = new TestOneShotCommandGroup();

        group.TimeoutPolicy.Should().Be(ToolTimeoutPolicy.AbsoluteTwoMinutes);
    }

    [Fact]
    public void LongRunningGroup_HasNonePolicy()
    {
        var group = new TestLongRunningGroup();

        group.TimeoutPolicy.Should().Be(ToolTimeoutPolicy.None);
    }

    private sealed class TestOneShotCommandGroup : OneShotCommandGroup { }
    private sealed class TestLongRunningGroup : LongRunningGroup { }
}
