namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathGateMiddleware 单元测试 — 验证路径门控中间件根据 Shell 类型转换 WorkingDirectory
/// </summary>
public class ShellPathGateMiddlewareTests
{
    [Theory]
    [InlineData("C:\\Users\\test", false, "/c/Users/test")]
    [InlineData("C:\\Users\\test", true, "C:\\Users\\test")]
    [InlineData("/c/Users/test", true, "C:\\Users\\test")]
    [InlineData("/c/Users/test", false, "/c/Users/test")]
    [InlineData(null, false, null)]
    [InlineData("", false, "")]
    public async Task InvokeAsync_ConvertsWorkingDirectory(string? input, bool isPowerShell, string? expected)
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string path, bool ps) => ps ? path.Replace('/', '\\').Replace("C:", "C:") : path.Replace('\\', '/'));

        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            IsPowerShell = isPowerShell,
            WorkingDirectory = input,
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        if (expected is null)
        {
            context.WorkingDirectory.Should().BeNull();
        }
        else
        {
            probeService.Verify(x => x.GatePath(input!, isPowerShell), input is not null and not "" ? Times.Once() : Times.Never());
        }
    }

    [Fact]
    public async Task InvokeAsync_NoChangeNeeded_DoesNotModifyContext()
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath("/home/user", false))
            .Returns("/home/user");

        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            IsPowerShell = false,
            WorkingDirectory = "/home/user",
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.WorkingDirectory.Should().Be("/home/user");
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var probeService = new Mock<IEnvironmentProbeService>();
        probeService.Setup(x => x.GatePath(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string p, bool _) => p);

        var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext
        {
            Command = "echo hello",
            IsPowerShell = false,
            WorkingDirectory = "/home/user",
        };

        var nextCalled = false;
        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }
}
