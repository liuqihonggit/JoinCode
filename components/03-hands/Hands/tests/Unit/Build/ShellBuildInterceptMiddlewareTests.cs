namespace Hands.Tests.Build;

/// <summary>
/// ShellBuildInterceptMiddleware 单元测试
/// </summary>
public class ShellBuildInterceptMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_BuildCommand_SubmitsToQueue()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 0,
                Output = "ok",
                ErrorOutput = "",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx -c Release");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        queueMock.Verify(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        context.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_BuildCommand_DoesNotCallNext()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 0,
                Output = "ok",
                ErrorOutput = "",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var nextCalled = false;
        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx -c Release");

        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NonBuildCommand_CallsNext()
    {
        var queueMock = new Mock<IBuildQueueService>();
        var sut = CreateSut(buildQueueService: queueMock.Object);

        var nextCalled = false;
        var context = CreateContext(command: "dotnet --info");

        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        queueMock.Verify(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("dotnet build JoinCode.slnx")]
    [InlineData("dotnet test JoinCode.slnx")]
    [InlineData("dotnet publish JoinCode.slnx -c Release")]
    [InlineData("dotnet msbuild JoinCode.slnx")]
    [InlineData("dotnet.exe build JoinCode.slnx")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build JoinCode.slnx")]
    public async Task InvokeAsync_RecognizesBuildCommands(string command)
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 0,
                Output = "ok",
                ErrorOutput = "",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: command);

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        queueMock.Verify(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("dotnet run")]
    [InlineData("dotnet tool install")]
    [InlineData("dotnet --info")]
    [InlineData("dotnet --version")]
    [InlineData("dotnet new console")]
    [InlineData("dotnet add package Newtonsoft.Json")]
    [InlineData("echo dotnet build")]
    [InlineData("dir")]
    public async Task InvokeAsync_IgnoresNonBuildCommands(string command)
    {
        var queueMock = new Mock<IBuildQueueService>();
        var sut = CreateSut(buildQueueService: queueMock.Object);

        var nextCalled = false;
        var context = CreateContext(command: command);

        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        queueMock.Verify(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_BuildCommand_PassesWorkingDirectory()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 0,
                Output = "ok",
                ErrorOutput = "",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx", workingDirectory: "D:\\w1");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        queueMock.Verify(x => x.SubmitAsync(
            It.Is<BuildRequest>(r => r.WorkingDirectory == "D:\\w1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_CacheHitCompleted_ReturnsFullOutput()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns(new BuildQueueEntry
            {
                BuildId = "b-0001",
                Request = CreateRequest(),
                Status = BuildQueueEntryStatus.Completed,
                Result = new BuildQueueResult
                {
                    BuildId = "b-0001",
                    ExitCode = 0,
                    Output = "Build succeeded.",
                    ErrorOutput = "",
                    WaitDuration = TimeSpan.Zero,
                    BuildDuration = TimeSpan.FromSeconds(30),
                    QueuePosition = 0,
                },
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.GetTextContent().Should().Contain("Build succeeded.");
        context.ExecutionResult.Should().NotBeNull();
        context.ExecutionResult!.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_BuildCompletesWithinTimeout_ReturnsFullOutput()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 0,
                Output = "Build OK",
                ErrorOutput = "",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.GetTextContent().Should().Contain("Build OK");
        context.ExecutionResult.Should().NotBeNull();
        context.ExecutionResult!.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_BuildFails_ReturnsErrorOutput()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns((BuildQueueEntry?)null);
        queueMock.Setup(x => x.WaitAsync("b-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildQueueResult
            {
                BuildId = "b-0001",
                ExitCode = 1,
                Output = "Build output",
                ErrorOutput = "error CS0001",
                WaitDuration = TimeSpan.Zero,
                BuildDuration = TimeSpan.FromSeconds(5),
                QueuePosition = 0,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        context.ExecutionResult.Should().NotBeNull();
        context.ExecutionResult!.ExitCode.Should().Be(1);
        context.Result!.GetTextContent().Should().Contain("error CS0001");
    }

    [Fact]
    public async Task InvokeAsync_CancelledBuild_ReturnsCancelledMessage()
    {
        var queueMock = new Mock<IBuildQueueService>();
        queueMock.Setup(x => x.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b-0001");
        queueMock.Setup(x => x.GetBuild("b-0001"))
            .Returns(new BuildQueueEntry
            {
                BuildId = "b-0001",
                Request = CreateRequest(),
                Status = BuildQueueEntryStatus.Cancelled,
            });

        var sut = CreateSut(buildQueueService: queueMock.Object);
        var context = CreateContext(command: "dotnet build JoinCode.slnx");

        await sut.InvokeAsync(context, Next, CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.GetTextContent().Should().Contain("cancelled");
    }

    private static ShellBuildInterceptMiddleware CreateSut(
        IBuildQueueService? buildQueueService = null)
    {
        return new ShellBuildInterceptMiddleware(
            buildQueueService: buildQueueService ?? Mock.Of<IBuildQueueService>(),
            subAgentContextAccessor: new SubAgentContextAccessor(),
            clock: SystemClockService.Instance);
    }

    private static ShellContext CreateContext(string command, string? workingDirectory = null)
    {
        return new ShellContext
        {
            Command = command,
            IsPowerShell = false,
            WorkingDirectory = workingDirectory,
        };
    }

    private static BuildRequest CreateRequest() => new()
    {
        Command = "dotnet build JoinCode.slnx -c Release",
        AgentId = "test-agent",
    };

    private static Task Next(ShellContext ctx, CancellationToken ct) => Task.CompletedTask;
}
