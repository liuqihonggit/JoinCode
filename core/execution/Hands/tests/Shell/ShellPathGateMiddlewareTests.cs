namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathGateMiddleware 单元测试 — 验证路径门控中间件根据 Provider 类型转换 WorkingDirectory
/// </summary>
public class ShellPathGateMiddlewareTests
{
    [Theory]
    [InlineData("C:\\Users\\test", "bash", "/c/Users/test")]
    [InlineData("C:\\Users\\test", "powershell", "C:\\Users\\test")]
    [InlineData("/c/Users/test", "powershell", "C:\\Users\\test")]
    [InlineData("/c/Users/test", "bash", "/c/Users/test")]
    [InlineData(null, "bash", null)]
    [InlineData("", "bash", "")]
    public async Task InvokeAsync_ConvertsWorkingDirectory(string? input, string kindId, string? expected)
    {
        var kind = SystemActuatorKind.FromId(kindId)!;
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<ISystemActuator>()))
            .Returns((string path, ISystemActuator provider) =>
                provider.Kind == SystemActuatorKind.Bash ? path.Replace('\\', '/') : path.Replace('/', '\\'));

        var provider = CreateMockProvider(kind);
        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = input,
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        if (expected is null)
        {
            context.WorkingDirectory.Should().BeNull();
        }
        else
        {
            probeService.Verify(x => x.GatePath(input!, provider.Object), input is not null and not "" ? Times.Once() : Times.Never());
        }
    }

    [Fact]
    public async Task InvokeAsync_NoChangeNeeded_DoesNotModifyContext()
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        var provider = CreateMockProvider(SystemActuatorKind.Bash);
        probeService.Setup(x => x.GatePath("/home/user", provider.Object))
            .Returns("/home/user");

        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = "/home/user",
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.WorkingDirectory.Should().Be("/home/user");
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<ISystemActuator>()))
            .Returns((string p, ISystemActuator _) => p);

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
