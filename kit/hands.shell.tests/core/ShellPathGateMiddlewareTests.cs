namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathGateMiddleware 单元测试 — 验证路径门控中间件不再自动转换路径(报错让 LLM 自己修正)
/// 仅保留 UNC 路径警告(只读不改) + next 调用
/// </summary>
public class ShellPathGateMiddlewareTests
{
    [Theory]
    [InlineData("C:\\Users\\test", "bash")]
    [InlineData("C:\\Users\\test", "powershell")]
    [InlineData("/c/Users/test", "powershell")]
    [InlineData("/c/Users/test", "bash")]
    [InlineData(null, "bash")]
    [InlineData("", "bash")]
    public async Task InvokeAsync_DoesNotModifyWorkingDirectory(string? input, string kindId)
    {
        var kind = SystemActuatorKind.FromId(kindId)!;
        var probeService = new Mock<IEnvironmentProbeService>();

        var provider = CreateMockProvider(kind);
        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = input,
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.WorkingDirectory.Should().Be(input);
    }

    [Theory]
    [InlineData("echo D:\\a\\b\\c/d", "bash")]
    [InlineData("cat C:\\proj\\src/file.txt", "powershell")]
    public async Task InvokeAsync_DoesNotModifyCommand(string command, string kindId)
    {
        var kind = SystemActuatorKind.FromId(kindId)!;
        var probeService = new Mock<IEnvironmentProbeService>();

        var provider = CreateMockProvider(kind);
        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = command,
            Provider = provider.Object,
            WorkingDirectory = "/home/user",
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Command.Should().Be(command);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        var provider = CreateMockProvider(SystemActuatorKind.Bash);
        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = "/home/user",
        };

        var nextCalled = false;
        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    private static Mock<ISystemActuator> CreateMockProvider(SystemActuatorKind kind)
    {
        var mock = new Mock<ISystemActuator>();
        mock.SetupGet(x => x.Kind).Returns(kind);
        mock.SetupGet(x => x.ShellPath).Returns(kind == SystemActuatorKind.Bash ? "bash" : kind == SystemActuatorKind.PowerShell ? "pwsh" : "cmd.exe");
        return mock;
    }
}
