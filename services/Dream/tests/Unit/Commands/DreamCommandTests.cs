namespace Dream.Tests.Commands;

/// <summary>
/// /dream 与 /dream-tasks 命令单元测试
/// </summary>
public sealed class DreamCommandTests
{
    #region DreamCommand

    [Fact]
    public void DreamCommand_Constructor_NullFeature_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DreamCommand(null!));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_ForceArgument_OutputsForceMode()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.Is<DreamRequest>(r => r.Force), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Success("ok", "t1", 1, 0));
        var ctx = new FakeCommandContext { Arguments = ["force"] };
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Outputs, o => o.Contains("强制触发"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_NoForceArgument_OutputsAutoMode()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.Is<DreamRequest>(r => !r.Force), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Success("ok", "t1", 1, 0));
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Outputs, o => o.Contains("自动门控"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_SkippedResult_OutputsWarning()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Skipped("skipped"));
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Warnings, w => w.Contains("skipped"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_FailureResult_OutputsError()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Failure("boom"));
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Errors, e => e.Contains("boom"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_SuccessResultWithContent_OutputsSuccessAndDetails()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Success("memory", "t1", 3, 42));
        var ctx = new FakeCommandContext { Arguments = ["force"] };
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Successes, s => s.Contains("完成"));
        Assert.Contains(ctx.Outputs, o => o.Contains("t1"));
        Assert.Contains(ctx.Outputs, o => o.Contains("3"));
        Assert.Contains(ctx.Outputs, o => o.Contains("42"));
        Assert.Contains(ctx.Outputs, o => o.Contains("memory"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_EmptySuccessResult_OutputsWarning()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Success(string.Empty, "t1", 1, 0));
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Warnings, w => w.Contains("未产生结果"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_Cancelled_OutputsWarning()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Warnings, w => w.Contains("取消"));
    }

    [Fact]
    public async Task DreamCommand_ExecuteAsync_Exception_LogsErrorAndOutputsError()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad"));
        var logger = new Mock<ILogger<DreamCommand>>();
        var ctx = new FakeCommandContext();
        var command = new DreamCommand(feature.Object, logger.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Errors, e => e.Contains("bad"));
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<InvalidOperationException>(ex => ex.Message == "bad"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region DreamTasksCommand

    [Fact]
    public void DreamTasksCommand_Constructor_NullFeature_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DreamTasksCommand(null!));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_List_NoTasks_OutputsNoTasks()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.ListTasksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DreamTaskState>());
        var ctx = new FakeCommandContext();
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Outputs, o => o.Contains("没有做梦任务"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_List_WithTasks_OutputsTasks()
    {
        var feature = new Mock<IDreamFeature>();
        var tasks = new Dictionary<string, DreamTaskState>
        {
            ["d12345678"] = new()
            {
                Id = "d12345678",
                Description = "dreaming",
                StartTime = DateTime.UtcNow,
                SessionsReviewing = 2,
                Phase = DreamPhase.Updating,
                Status = DreamTaskStatus.Running,
                PriorMtime = 0
            }
        };
        tasks["d12345678"].FilesTouched.Add("file.md");
        tasks["d12345678"].Turns.Add(new DreamTurn { Text = "turn text", ToolUseCount = 1 });

        feature.Setup(f => f.ListTasksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);
        var ctx = new FakeCommandContext();
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Outputs, o => o.Contains("d12345678"));
        Assert.Contains(ctx.Outputs, o => o.Contains("Updating"));
        Assert.Contains(ctx.Outputs, o => o.Contains("1"));
        Assert.Contains(ctx.Outputs, o => o.Contains("1"));
        Assert.Contains(ctx.Outputs, o => o.Contains("turn text"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_List_LastTurnPreview_Truncated()
    {
        var feature = new Mock<IDreamFeature>();
        var tasks = new Dictionary<string, DreamTaskState>
        {
            ["d12345678"] = new()
            {
                Id = "d12345678",
                Description = "dreaming",
                StartTime = DateTime.UtcNow,
                SessionsReviewing = 1,
                PriorMtime = 0
            }
        };
        tasks["d12345678"].Turns.Add(new DreamTurn { Text = new string('x', 100), ToolUseCount = 0 });

        feature.Setup(f => f.ListTasksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);
        var ctx = new FakeCommandContext();
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Outputs, o => o.Contains("..."));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_KillMissingArgument_OutputsError()
    {
        var feature = new Mock<IDreamFeature>();
        var ctx = new FakeCommandContext { Arguments = ["kill"] };
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Errors, e => e.Contains("用法"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_KillNonExistentTask_OutputsError()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.GetTaskStatusAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DreamTaskState?)null);
        var ctx = new FakeCommandContext { Arguments = ["kill", "missing"] };
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Errors, e => e.Contains("不存在"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_KillTerminalTask_OutputsWarning()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.GetTaskStatusAsync("d12345678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DreamTaskState
            {
                Id = "d12345678",
                Description = "dreaming",
                StartTime = DateTime.UtcNow,
                SessionsReviewing = 1,
                Status = DreamTaskStatus.Completed,
                PriorMtime = 0
            });
        var ctx = new FakeCommandContext { Arguments = ["kill", "d12345678"] };
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Warnings, w => w.Contains("终态"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_KillRunningTask_Success()
    {
        var feature = new Mock<IDreamFeature>();
        feature.Setup(f => f.GetTaskStatusAsync("d12345678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DreamTaskState
            {
                Id = "d12345678",
                Description = "dreaming",
                StartTime = DateTime.UtcNow,
                SessionsReviewing = 1,
                Status = DreamTaskStatus.Running,
                PriorMtime = 0
            });
        var ctx = new FakeCommandContext { Arguments = ["kill", "d12345678"] };
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        feature.Verify(f => f.KillTaskAsync("d12345678", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(ctx.Successes, s => s.Contains("已终止"));
    }

    [Fact]
    public async Task DreamTasksCommand_ExecuteAsync_UnknownAction_OutputsError()
    {
        var feature = new Mock<IDreamFeature>();
        var ctx = new FakeCommandContext { Arguments = ["unknown"] };
        var command = new DreamTasksCommand(feature.Object);

        await command.ExecuteAsync(ctx).ConfigureAwait(true);

        Assert.Contains(ctx.Errors, e => e.Contains("未知操作"));
    }

    #endregion

    private sealed class FakeCommandContext : ICommandContext
    {
        public string RawInput { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public string[] Arguments { get; set; } = Array.Empty<string>();
        public string SessionId { get; set; } = string.Empty;
        public ILogger Logger { get; } = NullLogger.Instance;
        public IConsoleOutput ConsoleOutput { get; } = null!;

        public List<string> Outputs { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Successes { get; } = new();
        public List<string> Warnings { get; } = new();

        public void Output(string message) => Outputs.Add(message);
        public void OutputError(string message) => Errors.Add(message);
        public void OutputSuccess(string message) => Successes.Add(message);
        public void OutputWarning(string message) => Warnings.Add(message);
        public string? Prompt(string message) => null;
        public bool Confirm(string message) => false;
        public void Output(string message, ConsoleColor color) => Outputs.Add(message);
        public string ReadPassword(string prompt) => string.Empty;
    }
}
