namespace Hands.Tests.Build;

/// <summary>
/// BuildQueueRouter 单元测试 — 验证多 Worker 并行编译、提交/等待/取消/查询。
/// </summary>
public class BuildQueueRouterTests
{
    [Fact]
    public async Task SubmitAsync_ReturnsBuildId()
    {
        var sut = CreateSut();
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);

        buildId.Should().NotBeNullOrEmpty();
        buildId.Should().StartWith("b-");
    }

    [Fact]
    public async Task WaitAsync_ReturnsResult_WhenBuildCompletes()
    {
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemActuatorExecutionResult.SuccessResult("Build succeeded.", ""));

        var sut = CreateSut(actuator: shellMock.Object);
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);
        var result = await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.BuildId.Should().Be(buildId);
        result.ExitCode.Should().Be(0);
        result.Output.Should().Be("Build succeeded.");
    }

    [Fact]
    public async Task WaitAsync_ReturnsFailedResult_WhenBuildFails()
    {
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemActuatorExecutionResult.FailureResult("Build failed.", "error output", "stderr"));

        var sut = CreateSut(actuator: shellMock.Object);
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);
        var result = await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("stderr");
    }

    [Fact]
    public async Task MultipleBuilds_ParallelExecution_WithMultipleWorkers()
    {
        var buildDelay = TimeSpan.FromMilliseconds(200);
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, int? _, string? _, bool _, CancellationToken ct) =>
            {
                await Task.Delay(buildDelay, ct).ConfigureAwait(true);
                return SystemActuatorExecutionResult.SuccessResult("ok", "");
            });

        var sut = CreateSut(actuator: shellMock.Object, workerCount: 3);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var buildIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var id = await sut.SubmitAsync(CreateRequest($"build {i}"), CancellationToken.None).ConfigureAwait(true);
            buildIds.Add(id);
        }

        foreach (var id in buildIds)
        {
            var result = await sut.WaitAsync(id, CancellationToken.None).ConfigureAwait(true);
            result.ExitCode.Should().Be(0);
        }
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(buildDelay * 2,
            "3 个 Worker 并行执行 3 个 200ms 编译,总时间应远小于串行 600ms");
    }

    [Fact]
    public async Task CancelAsync_BuildingBuild_ReturnsTrue()
    {
        var buildTcs = new TaskCompletionSource<SystemActuatorExecutionResult>();
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
#pragma warning disable VSTHRD003
            .Returns(async () => await buildTcs.Task.ConfigureAwait(true));
#pragma warning restore VSTHRD003

        var sut = CreateSut(actuator: shellMock.Object);
        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);

        await Task.Delay(100).ConfigureAwait(true);

        var cancelled = await sut.CancelAsync(buildId, CancellationToken.None).ConfigureAwait(true);
        cancelled.Should().BeTrue();

        buildTcs.SetCanceled();

        var entry = sut.GetBuild(buildId);
        entry.Should().NotBeNull();
        entry!.Status.Should().BeOneOf(BuildQueueEntryStatus.Cancelling, BuildQueueEntryStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAsync_NonExistentBuild_ReturnsFalse()
    {
        var sut = CreateSut();

        var cancelled = await sut.CancelAsync("nonexistent", CancellationToken.None).ConfigureAwait(true);

        cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task GetBuild_ReturnsEntry()
    {
        var sut = CreateSut();
        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);

        var entry = sut.GetBuild(buildId);

        entry.Should().NotBeNull();
        entry!.BuildId.Should().Be(buildId);
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectStatus()
    {
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemActuatorExecutionResult.SuccessResult("ok", ""));

        var sut = CreateSut(actuator: shellMock.Object);
        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        var status = sut.GetStatus();

        status.Should().NotBeNull();
        status.PendingCount.Should().Be(0);
        status.IsBuilding.Should().BeFalse();
        status.RecentBuilds.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOutputRange_ReturnsSpecifiedLines()
    {
        var shellMock = new Mock<ISystemActuator>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemActuatorExecutionResult.SuccessResult("line1\nline2\nline3", ""));

        var sut = CreateSut(actuator: shellMock.Object);
        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        var range = sut.GetOutputRange(buildId, 2, 0);

        range.Should().Be("line2\nline3");
    }

    [Fact]
    public async Task DisposeAsync_DisposesResources()
    {
        var sut = CreateSut();
        await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);

        await sut.DisposeAsync().ConfigureAwait(true);

        var act = async () => await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static BuildQueueRouter CreateSut(
        ISystemActuator? actuator = null,
        int workerCount = 2,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueRouter>? logger = null)
    {
        var actuatorMock = actuator ?? Mock.Of<ISystemActuator>();
        var registryMock = new Mock<ISystemActuatorRegistry>();
        registryMock.Setup(r => r.Get(It.IsAny<SystemActuatorKind>())).Returns(actuatorMock);
        return new BuildQueueRouter(
            actuatorRegistry: registryMock.Object,
            workerCount: workerCount,
            preventSleepService: preventSleepService ?? Mock.Of<IPreventSleepService>(),
            logger: logger);
    }

    private static BuildRequest CreateRequest(string? command = null, string? agentId = null)
    {
        return new BuildRequest
        {
            Command = command ?? "dotnet build JoinCode.slnx -c Release",
            AgentId = agentId ?? "test-agent",
        };
    }
}
