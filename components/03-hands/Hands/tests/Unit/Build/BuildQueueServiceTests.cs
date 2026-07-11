namespace Hands.Tests.Build;

/// <summary>
/// BuildQueueService 单元测试
/// </summary>
public class BuildQueueServiceTests
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
    public async Task SubmitAsync_EntryExists()
    {
        var sut = CreateSut();
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);

        var entry = sut.GetBuild(buildId);
        entry.Should().NotBeNull();
        entry!.BuildId.Should().Be(buildId);
    }

    [Fact]
    public async Task WaitAsync_ReturnsResult_WhenBuildCompletes()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("Build succeeded.", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);
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
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.FailureResult("Build failed.", "error output", "stderr"));

        var sut = CreateSut(shellExecutionService: shellMock.Object);
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);
        var result = await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("stderr");
    }

    [Fact]
    public async Task CancelAsync_QueuedBuild_ReturnsTrue()
    {
        var tcs = new TaskCompletionSource<ShellExecutionResult>();
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var sut = CreateSut(shellExecutionService: shellMock.Object);
        var request = CreateRequest();

        var buildId = await sut.SubmitAsync(request, CancellationToken.None).ConfigureAwait(true);
        var cancelled = await sut.CancelAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        tcs.TrySetCanceled();
        cancelled.Should().BeTrue();
        var entry = sut.GetBuild(buildId);
        entry!.Status.Should().Be(BuildQueueEntryStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAsync_NonExistentBuild_ReturnsFalse()
    {
        var sut = CreateSut();

        var cancelled = await sut.CancelAsync("b-9999", CancellationToken.None).ConfigureAwait(true);

        cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_ReflectsCompletedBuilds()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("ok", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        var status = sut.GetStatus();
        status.RecentBuilds.Should().ContainSingle(e => e.BuildId == buildId);
    }

    [Fact]
    public async Task SubmitAsync_BufferHit_ReturnsCompletedResult()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("Build OK", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var id1 = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(id1, CancellationToken.None).ConfigureAwait(true);

        var id2 = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        var entry = sut.GetBuild(id2);
        entry!.Status.Should().Be(BuildQueueEntryStatus.Completed);
        entry.Result!.Output.Should().Be("Build OK");
    }

    [Fact]
    public async Task BuildsAreSerialized_OneAtATime()
    {
        var buildOrder = new List<string>();
        var tcs1 = new TaskCompletionSource<ShellExecutionResult>();
        var tcs2 = new TaskCompletionSource<ShellExecutionResult>();

        var callCount = 0;
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
#pragma warning disable VSTHRD003
            .Returns(async () =>
            {
                callCount++;
                if (callCount == 1)
                {
                    buildOrder.Add("start1");
                    var result = await tcs1.Task.ConfigureAwait(true);
                    buildOrder.Add("end1");
                    return result;
                }
                else
                {
                    buildOrder.Add("start2");
                    var result = await tcs2.Task.ConfigureAwait(true);
                    buildOrder.Add("end2");
                    return result;
                }
            });
#pragma warning restore VSTHRD003

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var id1 = await sut.SubmitAsync(CreateRequest(command: "dotnet build ProjA.slnx"), CancellationToken.None).ConfigureAwait(true);
        var id2 = await sut.SubmitAsync(CreateRequest(command: "dotnet build ProjB.slnx"), CancellationToken.None).ConfigureAwait(true);

        await Task.Delay(200).ConfigureAwait(true);

        tcs1.SetResult(ShellExecutionResult.SuccessResult("ok1", ""));
        tcs2.SetResult(ShellExecutionResult.SuccessResult("ok2", ""));

        await sut.WaitAsync(id1, CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(id2, CancellationToken.None).ConfigureAwait(true);

        buildOrder.Should().ContainInOrder("start1", "end1", "start2", "end2");
    }

    /// <summary>
    /// 跨进程构建锁集成测试 — 验证当锁文件被其他进程持有时，构建会等待锁释放后才执行
    /// 使用 PhysicalFileSystem + 真实文件 I/O 验证 FileShare.None 跨进程互斥语义
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrossProcessLock_BuildWaits_WhenLockHeldByOtherProcess()
    {
        // 使用唯一的锁文件路径，避免与其他测试冲突
        var lockPath = Path.Combine(Path.GetTempPath(), $"JoinCode.Build.CrossProc.{Guid.NewGuid():N}.lock");
        var fs = new PhysicalFileSystem();

        // 模拟另一个进程持有锁文件 — 通过 IFileSystem 以独占方式打开
        var holdingStream = fs.CreateStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var shellMock = new Mock<IShellExecutionService>();
            var buildStarted = new TaskCompletionSource<bool>();
            shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    buildStarted.TrySetResult(true);
                    return ShellExecutionResult.SuccessResult("Build ok", "");
                });

            await using var sut = new BuildQueueService(
                shellExecutionService: shellMock.Object,
                fs: fs,
                crossProcessLockPath: lockPath);

            var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);

            // 等待 500ms — 构建应该因为锁被持有而无法启动
            await Task.Delay(500).ConfigureAwait(true);
            buildStarted.Task.IsCompleted.Should().BeFalse("build should be blocked waiting for cross-process lock");

            // 释放锁文件 — 模拟其他进程完成构建
            holdingStream.Dispose();

            // 等待构建完成 — 锁释放后构建应立即启动并完成
            var result = await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);
            result.ExitCode.Should().Be(0);
            result.Output.Should().Be("Build ok");
        }
        finally
        {
            // 清理锁文件（如果还存在）
            try
            {
                if (fs.FileExists(lockPath)) fs.DeleteFile(lockPath);
            }
            catch (IOException ex)
            {
                // 清理失败不影响测试结果
                System.Diagnostics.Debug.WriteLine($"Failed to clean up lock file {lockPath}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void BuildBufferKey_IncludesWorkingDirectory()
    {
        var key1 = BuildQueueService.BuildBufferKey("dotnet build", "D:\\w1");
        var key2 = BuildQueueService.BuildBufferKey("dotnet build", "D:\\w2");
        key1.Should().NotBe(key2);
    }

    [Fact]
    public async Task ClearCacheAsync_ClearsResultBuffer()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("ok", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var id1 = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(id1, CancellationToken.None).ConfigureAwait(true);

        await sut.ClearCacheAsync(CancellationToken.None).ConfigureAwait(true);

        var id2 = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        var entry = sut.GetBuild(id2);
        entry!.Status.Should().BeOneOf(BuildQueueEntryStatus.Queued, BuildQueueEntryStatus.Building, BuildQueueEntryStatus.Completed);
    }

    [Fact]
    public async Task GetOutputRange_ReturnsSpecifiedLines()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("line1\nline2\nline3\nline4\nline5", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        var range = sut.GetOutputRange(buildId, 2, 4);
        range.Should().Be("line2\nline3\nline4");
    }

    [Fact]
    public async Task GetOutputRange_EndLineZero_ReturnsToEnd()
    {
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShellExecutionResult.SuccessResult("line1\nline2\nline3", ""));

        var sut = CreateSut(shellExecutionService: shellMock.Object);

        var buildId = await sut.SubmitAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(true);
        await sut.WaitAsync(buildId, CancellationToken.None).ConfigureAwait(true);

        var range = sut.GetOutputRange(buildId, 2, 0);
        range.Should().Be("line2\nline3");
    }

    [Fact]
    public void TruncateOutput_Under15Lines_ReturnsFullOutput()
    {
        var output = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}"));
        var result = ShellBuildInterceptMiddleware.TruncateOutput(output, "b-0001");
        result.Should().Be(output);
    }

    [Fact]
    public void TruncateOutput_Over15Lines_ReturnsTailWithHint()
    {
        var output = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line{i}"));
        var result = ShellBuildInterceptMiddleware.TruncateOutput(output, "b-0001");
        result.Should().Contain("truncated");
        result.Should().Contain("build_output");
        result.Should().Contain("line30");
        result.Should().NotContain("\nline1\n");
    }

    private static BuildQueueService CreateSut(
        IShellExecutionService? shellExecutionService = null,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueService>? logger = null)
    {
        // 每个测试实例使用唯一的锁文件路径，避免并行测试互相阻塞
        var uniqueLockPath = Path.Combine(Path.GetTempPath(), $"JoinCode.Build.{Guid.NewGuid():N}.lock");
        return new BuildQueueService(
            shellExecutionService: shellExecutionService ?? Mock.Of<IShellExecutionService>(),
            fs: new Testing.Common.Services.InMemoryFileSystem(),
            preventSleepService: preventSleepService ?? Mock.Of<IPreventSleepService>(),
            logger: logger,
            crossProcessLockPath: uniqueLockPath);
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
