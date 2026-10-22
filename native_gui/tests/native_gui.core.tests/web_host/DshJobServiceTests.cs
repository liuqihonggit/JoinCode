namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 后台任务投影（TASK066 C2）：DSH JobView 映射、输出字节游标与 kill 受理语义。
/// </summary>
public sealed class DshJobServiceTests {
    /// <summary>运行中/完成/失败/停止 → DSH 状态词表与合成输出</summary>
    [Fact]
    public async Task ListAsync_MapsStatusesAndComposesOutput() {
        var agents = new[] {
            Agent("subagent-1", AgentStatus.Running, activities: ["读取文件", "运行测试"]),
            Agent("subagent-2", AgentStatus.Completed, success: true, final: "done"),
            Agent("subagent-3", AgentStatus.Completed, success: false),
            Agent("subagent-4", AgentStatus.Stopped),
        };
        var service = Fake(agents);

        var jobs = await service.ListAsync("session-1", CancellationToken.None);

        jobs.Select(job => job.Status).Should().Equal("running", "completed", "failed", "killed");
        jobs.Should().OnlyContain(job => job.Owner == "session-1");
        jobs.Should().OnlyContain(job => job.Kind == "subagent");
        var runningTotal = Encoding.UTF8.GetByteCount("· 读取文件\n· 运行测试\n");
        jobs[0].Output.Total.Should().Be(runningTotal);
        jobs[0].FinishedAt.Should().BeNull();
        jobs[1].FinishedAt.Should().NotBeNull();
    }

    /// <summary>输出总字节数与 UTF-8 编码一致（含中文，游标按字节不按字符）</summary>
    [Fact]
    public async Task SnapshotAsync_TotalMatchesUtf8Bytes() {
        var service = Fake([Agent("subagent-1", AgentStatus.Running, activities: ["中文活动"])]);

        var snapshot = (await service.SnapshotAsync("session-1", "subagent-1", CancellationToken.None))
            .Should().BeOfType<DshJobSnapshot>().Subject;

        snapshot.OutputText.Should().Be("· 中文活动\n");
        snapshot.View.Output.Total.Should().Be(Encoding.UTF8.GetByteCount(snapshot.OutputText));
        snapshot.Terminal.Should().BeFalse();
    }

    /// <summary>已结束任务再 kill：返回 already-finished 且不调用引擎停止</summary>
    [Fact]
    public async Task KillAsync_TerminalJob_DoesNotCallEngine() {
        var stopped = 0;
        var service = Fake([Agent("subagent-1", AgentStatus.Completed, success: true)], _ => {
            stopped++;
            return Task.FromResult(true);
        });

        var (outcome, error) = await service.KillAsync("session-1", "subagent-1", CancellationToken.None);

        outcome.Should().Be("already-finished");
        error.Should().BeNull();
        stopped.Should().Be(0);
    }

    /// <summary>运行中任务 kill：受理并委托引擎停止</summary>
    [Fact]
    public async Task KillAsync_RunningJob_RequestsStop() {
        string? stoppedId = null;
        var service = Fake([Agent("subagent-1", AgentStatus.Running)], id => {
            stoppedId = id;
            return Task.FromResult(true);
        });

        var (outcome, error) = await service.KillAsync("session-1", "subagent-1", CancellationToken.None);

        outcome.Should().Be("requested");
        error.Should().BeNull();
        stoppedId.Should().Be("subagent-1");
    }

    /// <summary>不存在的任务：返回 job/not-found 引导</summary>
    [Fact]
    public async Task KillAsync_UnknownJob_ReturnsNotFound() {
        var service = Fake([Agent("subagent-1", AgentStatus.Running)]);

        var (outcome, error) = await service.KillAsync("session-1", "missing", CancellationToken.None);

        outcome.Should().BeNull();
        var failure = error.Should().BeOfType<DshRpcError>().Subject;
        failure.Code.Should().Be("job/not-found");
        failure.Message.Should().Contain("missing");
    }

    /// <summary>构造只读假数据源（停止委托可缺省）</summary>
    private static DshJobService Fake(
        IReadOnlyList<BackgroundAgentInfo> agents,
        Func<string, Task<bool>>? stop = null) =>
        new(_ => Task.FromResult(agents), (id, _) => stop?.Invoke(id) ?? Task.FromResult(false));

    /// <summary>构造后台代理快照</summary>
    private static BackgroundAgentInfo Agent(
        string id,
        AgentStatus state,
        bool? success = null,
        string? final = null,
        params string[] activities) =>
        new(id, id, $"任务 {id}", state, DateTime.UtcNow.AddMinutes(-1), 2, 128,
            activities.Select((text, index) => new AgentActivityEntry {
                Timestamp = DateTime.UtcNow.AddSeconds(index),
                Type = AgentActivityType.Content,
                Text = text,
            }).ToArray(),
            activities.LastOrDefault(), final, success, 1_500, "worker");
}
