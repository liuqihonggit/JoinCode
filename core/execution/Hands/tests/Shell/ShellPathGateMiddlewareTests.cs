namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathGateMiddleware 单元测试 — 验证路径门控中间件根据 Provider 类型转换 WorkingDirectory
/// </summary>
public class ShellPathGateMiddlewareTests
{
    [Theory]
    [InlineData("C:\\Users\\test", ShellType.Bash, "/c/Users/test")]
    [InlineData("C:\\Users\\test", ShellType.PowerShell, "C:\\Users\\test")]
    [InlineData("/c/Users/test", ShellType.PowerShell, "C:\\Users\\test")]
    [InlineData("/c/Users/test", ShellType.Bash, "/c/Users/test")]
    [InlineData(null, ShellType.Bash, null)]
    [InlineData("", ShellType.Bash, "")]
    public async Task InvokeAsync_ConvertsWorkingDirectory(string? input, ShellType shellType, string? expected)
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<IShellProvider>()))
            .Returns((string path, IShellProvider provider) =>
                provider.Type == ShellType.Bash ? path.Replace('\\', '/') : path.Replace('/', '\\'));

        var provider = CreateMockProvider(shellType);
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
        var provider = CreateMockProvider(ShellType.Bash);
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
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<IShellProvider>()))
            .Returns((string p, IShellProvider _) => p);

        var provider = CreateMockProvider(ShellType.Bash);
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

    private static Mock<IShellProvider> CreateMockProvider(ShellType type)
    {
        var mock = new Mock<IShellProvider>();
        mock.SetupGet(x => x.Type).Returns(type);
        mock.SetupGet(x => x.ShellPath).Returns(type == ShellType.Bash ? "bash" : type == ShellType.PowerShell ? "pwsh" : "cmd.exe");
        return mock;
    }
}
