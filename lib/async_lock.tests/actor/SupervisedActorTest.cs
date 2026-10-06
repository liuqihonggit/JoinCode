namespace Core.Utils;

/// <summary>
/// SupervisedActor 单元测试 — 验证父子关系、监督策略、重启、生命周期级联。
/// </summary>
public class SupervisedActorTest {
    [Fact]
    public void SupervisorStrategy_PredefinedConfigs_HaveCorrectValues() {
        SupervisorStrategy.OneForOne.MaxRestarts.Should().Be(3);
        SupervisorStrategy.AllForOne.MaxRestarts.Should().Be(3);
        SupervisorStrategy.Escalate.MaxRestarts.Should().Be(0);
    }

    [Fact]
    public void SupervisorStrategy_Decider_ReturnsRestartForGenericException() {
        var ex = new InvalidOperationException("test");
        SupervisorStrategy.OneForOne.Decider(ex).Should().Be(SupervisorDirective.Restart);
        SupervisorStrategy.AllForOne.Decider(ex).Should().Be(SupervisorDirective.Restart);
    }

    [Fact]
    public void SupervisorStrategy_Decider_ReturnsStopForCancellation() {
        var ex = new OperationCanceledException();
        SupervisorStrategy.OneForOne.Decider(ex).Should().Be(SupervisorDirective.Stop);
    }

    [Fact]
    public void SupervisorStrategy_Escalate_Decider_ReturnsEscalate() {
        var ex = new InvalidOperationException("test");
        SupervisorStrategy.Escalate.Decider(ex).Should().Be(SupervisorDirective.Escalate);
    }

    [Fact]
    public async Task SpawnChild_RegistersChild_AndStartsIt() {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("child-1");

        handle.Id.Should().Be("child-1");
        handle.State.Should().Be(ChildActorState.Running);
        handle.Instance.Should().NotBeNull();
    }

    [Fact]
    public async Task GetChildren_ReturnsAllSpawnedChildren() {
        await using var parent = new TestSupervisedActor();
        await parent.SpawnTestChild("child-a");
        await parent.SpawnTestChild("child-b");
        await parent.SpawnTestChild("child-c");

        var children = parent.GetTestChildren();
        children.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetChild_ById_ReturnsCorrectHandle() {
        await using var parent = new TestSupervisedActor();
        await parent.SpawnTestChild("child-x");
        await parent.SpawnTestChild("child-y");

        using var child = parent.GetTestChild("child-x");
        child.Should().NotBeNull();
        child!.Id.Should().Be("child-x");
    }

    [Fact]
    public async Task StopAllChildren_StopsAllChildActors() {
        await using var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1");
        var h2 = await parent.SpawnTestChild("c2");

        await parent.StopAllTestChildrenAsync();

        h1.State.Should().Be(ChildActorState.Stopped);
        h2.State.Should().Be(ChildActorState.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_CascadesStopToChildren() {
        var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1");
        var h2 = await parent.SpawnTestChild("c2");

        await parent.DisposeAsync();

        h1.State.Should().Be(ChildActorState.Stopped);
        h2.State.Should().Be(ChildActorState.Stopped);
    }

    [Fact]
    public async Task HandleFailure_RestartDirective_RestartsChild() {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("restart-child", SupervisorStrategy.OneForOne);

        await handle.HandleTestFailureAsync(new InvalidOperationException("crash"));

        handle.State.Should().Be(ChildActorState.Running);
        handle.RestartCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailure_StopDirective_StopsChild() {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("stop-child", SupervisorStrategy.OneForOne);

        await handle.HandleTestFailureAsync(new OperationCanceledException());

        handle.State.Should().Be(ChildActorState.Failed);
    }

    [Fact]
    public async Task HandleFailure_RestartExceedsMax_MarksFailed() {
        var strategy = new SupervisorStrategy(2, TimeSpan.FromMinutes(1),
            static _ => SupervisorDirective.Restart);
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("max-restart", strategy);

        await handle.HandleTestFailureAsync(new InvalidOperationException("crash1"));
        await handle.HandleTestFailureAsync(new InvalidOperationException("crash2"));
        await handle.HandleTestFailureAsync(new InvalidOperationException("crash3"));

        handle.RestartCount.Should().Be(2);
        handle.State.Should().Be(ChildActorState.Failed);
    }

    [Fact]
    public async Task HandleFailure_EscalateDirective_CallsOnChildFailure() {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("escalate-child", SupervisorStrategy.Escalate);

        await handle.HandleTestFailureAsync(new InvalidOperationException("escalate"));

        parent.FailureReports.Should().ContainSingle();
        parent.FailureReports[0].Item1.Should().Be("escalate-child");
    }

    [Fact]
    public async Task OnChildFailureAsync_IsAbstract_ForcesImplementation() {
        // TestSupervisedActor 实现了 OnChildFailureAsync — 编译通过证明 abstract 强制实现
        // 如果子类不实现 OnChildFailureAsync,编译会报错
        await using var parent = new TestSupervisedActor();
        parent.FailureReports.Should().BeEmpty();
    }

    /// <summary>
    /// AllForOne 策略: 一个子失败应重启所有兄弟子 Actor(DSG033 S1)。
    /// 红测试: 当前只重启失败子,兄弟 RestartCount=0,断言=1 失败。
    /// </summary>
    [Fact]
    public async Task AllForOne_OneChildFails_AllSiblingsRestarted() {
        await using var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1", SupervisorStrategy.AllForOne);
        var h2 = await parent.SpawnTestChild("c2", SupervisorStrategy.AllForOne);
        var h3 = await parent.SpawnTestChild("c3", SupervisorStrategy.AllForOne);

        await h2.HandleTestFailureAsync(new InvalidOperationException("crash"));

        h2.RestartCount.Should().Be(1);
        h1.RestartCount.Should().Be(1, "AllForOne 应重启所有兄弟");
        h3.RestartCount.Should().Be(1, "AllForOne 应重启所有兄弟");
    }

    /// <summary>
    /// AllForOne 策略: 已 Stopped 的子不参与重启(DSG033 S1)。
    /// </summary>
    [Fact]
    public async Task AllForOne_StoppedSiblingNotRestarted() {
        await using var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1", SupervisorStrategy.AllForOne);
        var h2 = await parent.SpawnTestChild("c2", SupervisorStrategy.AllForOne);

        await h1.StopAsync();
        await h2.HandleTestFailureAsync(new InvalidOperationException("crash"));

        h2.RestartCount.Should().Be(1);
        h1.RestartCount.Should().Be(0, "已 Stopped 的子不参与 AllForOne 重启");
        h1.State.Should().Be(ChildActorState.Stopped);
    }

    /// <summary>DeathWatch: Watch 后子 Stop,父收到 Terminated 事件(DSG033 S3)</summary>
    [Fact]
    public async Task DeathWatch_ChildStops_ParentReceivesTerminated() {
        await using var parent = new TestSupervisedActor();
        var child = await parent.SpawnTestChild("c1");
        parent.WatchTestChild(child);

        await child.StopAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        SupervisorEvent? received = null;
        try {
            await foreach (var evt in parent.OutputAsync(cts.Token)) {
                received = evt;
                if (evt is Terminated) break;
            }
        } catch (OperationCanceledException) { }
        received.Should().BeOfType<Terminated>("Watch 后子 Stop 应触发 Terminated 事件");
    }

    /// <summary>DeathWatch: Unwatch 后不再收到 Terminated(DSG033 S3)</summary>
    [Fact]
    public async Task DeathWatch_Unwatch_StopsReceiving() {
        await using var parent = new TestSupervisedActor();
        var child = await parent.SpawnTestChild("c1");
        parent.WatchTestChild(child);
        parent.UnwatchTestChild(child);

        await child.StopAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sawTerminated = false;
        try {
            await foreach (var evt in parent.OutputAsync(cts.Token)) {
                if (evt is Terminated) { sawTerminated = true; break; }
            }
        } catch (OperationCanceledException) { }
        sawTerminated.Should().BeFalse("Unwatch 后不应收到 Terminated 事件");
    }

    /// <summary>DeathPact: watch(deathPact:true) 的子终止后,父处理下一条命令时抛 DeathPactException(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task DeathPact_子终止后父抛DeathPactException() {
        await using var parent = new TestSupervisedActor();
        var child = await parent.SpawnTestChild("c1");
        parent.WatchTestChild(child, deathPact: true);

        await child.StopAsync();

        parent.Tell(new TestSupervisedActor.NoopCmd());

        await TestWaitHelper.WaitUntilAsync(() => parent.LastConsumerError is not null, TimeSpan.FromMilliseconds(500));

        parent.LastConsumerError.Should().BeOfType<DeathPactException>("watch(deathPact:true) 的子终止后应抛 DeathPactException");
    }

    /// <summary>DeathPact: watch(deathPact:false) 的子终止后,父不抛异常(仅通知)(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task DeathPact_不启用_子终止后父不抛异常() {
        await using var parent = new TestSupervisedActor();
        var child = await parent.SpawnTestChild("c1");
        parent.WatchTestChild(child, deathPact: false);

        await child.StopAsync();

        parent.Tell(new TestSupervisedActor.NoopCmd());

        await TestWaitHelper.WaitUntilAsync(() => parent.OutputCount >= 1, TimeSpan.FromMilliseconds(500));

        parent.LastConsumerError.Should().BeNull("deathPact=false 时不应抛异常");
    }

    /// <summary>
    /// AllForOne 语义对齐 Akka: 任何子超出重启限制 → 全部 Stop(不重启)(DSG033 API对齐)。
    /// </summary>
    [Fact]
    public async Task AllForOne_AnyChildExceedsLimit_AllStopped() {
        var strategy = new SupervisorStrategy(1, TimeSpan.FromMinutes(1),
            static _ => SupervisorDirective.Restart, RestartAllSiblings: true);
        await using var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1", strategy);
        var h2 = await parent.SpawnTestChild("c2", strategy);

        await h1.HandleTestFailureAsync(new InvalidOperationException("crash1"));
        h1.RestartCount.Should().Be(1);

        await h2.HandleTestFailureAsync(new InvalidOperationException("crash2"));

        h1.State.Should().Be(ChildActorState.Stopped, "h1 已达重启上限,AllForOne 应 Stop 全部而非重启");
        h2.State.Should().Be(ChildActorState.Stopped, "AllForOne 兄弟超限,本子也应 Stop");
    }
}

/// <summary>
/// 测试用监督 Actor — 实现 OnChildFailureAsync,记录失败报告。
/// </summary>
internal sealed class TestSupervisedActor : SupervisedActor<TestSupervisedActor.ICommand> {
    internal interface ICommand;
    internal sealed record NoopCmd : ICommand;

    private readonly List<(string, Exception)> _failureReports = new();

    public List<(string, Exception)> FailureReports => _failureReports;
    public Exception? LastConsumerError;

    protected override ValueTask OnChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct) {
        _failureReports.Add((child.Id, ex));
        return ValueTask.CompletedTask;
    }

    protected override void Handle(ICommand command, CancellationToken ct) { }

    protected override void OnConsumerError(Exception ex) {
        LastConsumerError = ex;
    }

    public async ValueTask<ChildActorHandle> SpawnTestChild(string id, SupervisorStrategy? strategy = null) {
        return await SpawnChildAsync(id, _ => new ValueTask<IAsyncDisposable>(new DisposableStub()), strategy ?? SupervisorStrategy.OneForOne);
    }

    public IReadOnlyCollection<ChildActorHandle> GetTestChildren() => GetChildren();
    public ChildActorHandle? GetTestChild(string id) => GetChild(id);
    public ValueTask StopAllTestChildrenAsync() => StopAllChildrenAsync();
    public void WatchTestChild(ChildActorHandle child, bool deathPact = false) => Watch(child, deathPact);
    public void UnwatchTestChild(ChildActorHandle child) => Unwatch(child);
}

/// <summary>简单可释放对象 — 用于测试子 Actor 实例</summary>
internal sealed class DisposableStub : IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>ChildActorHandle 测试扩展 — 暴露 HandleFailureAsync 供测试调用</summary>
internal static class ChildActorHandleTestExtensions {
    public static ValueTask HandleTestFailureAsync(this ChildActorHandle handle, Exception ex)
        => handle.HandleFailureAsync(ex, CancellationToken.None);
}