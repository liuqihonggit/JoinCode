namespace Core.Utils;

/// <summary>
/// SupervisedActor 单元测试 — 验证父子关系、监督策略、重启、生命周期级联。
/// </summary>
public class SupervisedActorTest
{
    [Fact]
    public void SupervisorStrategy_PredefinedConfigs_HaveCorrectValues()
    {
        SupervisorStrategy.OneForOne.MaxRestarts.Should().Be(3);
        SupervisorStrategy.AllForOne.MaxRestarts.Should().Be(3);
        SupervisorStrategy.Escalate.MaxRestarts.Should().Be(0);
    }

    [Fact]
    public void SupervisorStrategy_Decider_ReturnsRestartForGenericException()
    {
        var ex = new InvalidOperationException("test");
        SupervisorStrategy.OneForOne.Decider(ex).Should().Be(SupervisorDirective.Restart);
        SupervisorStrategy.AllForOne.Decider(ex).Should().Be(SupervisorDirective.Restart);
    }

    [Fact]
    public void SupervisorStrategy_Decider_ReturnsStopForCancellation()
    {
        var ex = new OperationCanceledException();
        SupervisorStrategy.OneForOne.Decider(ex).Should().Be(SupervisorDirective.Stop);
    }

    [Fact]
    public void SupervisorStrategy_Escalate_Decider_ReturnsEscalate()
    {
        var ex = new InvalidOperationException("test");
        SupervisorStrategy.Escalate.Decider(ex).Should().Be(SupervisorDirective.Escalate);
    }

    [Fact]
    public async Task SpawnChild_RegistersChild_AndStartsIt()
    {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("child-1");

        handle.Id.Should().Be("child-1");
        handle.State.Should().Be(ChildActorState.Running);
        handle.Instance.Should().NotBeNull();
    }

    [Fact]
    public async Task GetChildren_ReturnsAllSpawnedChildren()
    {
        await using var parent = new TestSupervisedActor();
        await parent.SpawnTestChild("child-a");
        await parent.SpawnTestChild("child-b");
        await parent.SpawnTestChild("child-c");

        var children = parent.GetTestChildren();
        children.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetChild_ById_ReturnsCorrectHandle()
    {
        await using var parent = new TestSupervisedActor();
        await parent.SpawnTestChild("child-x");
        await parent.SpawnTestChild("child-y");

        var child = parent.GetTestChild("child-x");
        child.Should().NotBeNull();
        child!.Id.Should().Be("child-x");
    }

    [Fact]
    public async Task StopAllChildren_StopsAllChildActors()
    {
        await using var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1");
        var h2 = await parent.SpawnTestChild("c2");

        await parent.StopAllTestChildrenAsync();

        h1.State.Should().Be(ChildActorState.Stopped);
        h2.State.Should().Be(ChildActorState.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_CascadesStopToChildren()
    {
        var parent = new TestSupervisedActor();
        var h1 = await parent.SpawnTestChild("c1");
        var h2 = await parent.SpawnTestChild("c2");

        await parent.DisposeAsync();

        h1.State.Should().Be(ChildActorState.Stopped);
        h2.State.Should().Be(ChildActorState.Stopped);
    }

    [Fact]
    public async Task HandleFailure_RestartDirective_RestartsChild()
    {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("restart-child", SupervisorStrategy.OneForOne);

        await handle.HandleTestFailureAsync(new InvalidOperationException("crash"));

        handle.State.Should().Be(ChildActorState.Running);
        handle.RestartCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailure_StopDirective_StopsChild()
    {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("stop-child", SupervisorStrategy.OneForOne);

        await handle.HandleTestFailureAsync(new OperationCanceledException());

        handle.State.Should().Be(ChildActorState.Failed);
    }

    [Fact]
    public async Task HandleFailure_RestartExceedsMax_MarksFailed()
    {
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
    public async Task HandleFailure_EscalateDirective_CallsOnChildFailure()
    {
        await using var parent = new TestSupervisedActor();
        var handle = await parent.SpawnTestChild("escalate-child", SupervisorStrategy.Escalate);

        await handle.HandleTestFailureAsync(new InvalidOperationException("escalate"));

        parent.FailureReports.Should().ContainSingle();
        parent.FailureReports[0].Item1.Should().Be("escalate-child");
    }

    [Fact]
    public async Task OnChildFailureAsync_IsAbstract_ForcesImplementation()
    {
        // TestSupervisedActor 实现了 OnChildFailureAsync — 编译通过证明 abstract 强制实现
        // 如果子类不实现 OnChildFailureAsync,编译会报错
        await using var parent = new TestSupervisedActor();
        parent.FailureReports.Should().BeEmpty();
    }
}

/// <summary>
/// 测试用监督 Actor — 实现 OnChildFailureAsync,记录失败报告。
/// </summary>
internal sealed class TestSupervisedActor : SupervisedActor<TestSupervisedActor.ICommand>
{
    internal interface ICommand;

    private readonly List<(string, Exception)> _failureReports = new();

    public List<(string, Exception)> FailureReports => _failureReports;

    protected override ValueTask OnChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct)
    {
        _failureReports.Add((child.Id, ex));
        return ValueTask.CompletedTask;
    }

    protected override ValueTask HandleAsync(ICommand command, CancellationToken ct) => ValueTask.CompletedTask;

    public async ValueTask<ChildActorHandle> SpawnTestChild(string id, SupervisorStrategy? strategy = null)
    {
        return await SpawnChildAsync(id, _ => new ValueTask<IAsyncDisposable>(new DisposableStub()), strategy ?? SupervisorStrategy.OneForOne);
    }

    public IReadOnlyCollection<ChildActorHandle> GetTestChildren() => GetChildren();
    public ChildActorHandle? GetTestChild(string id) => GetChild(id);
    public ValueTask StopAllTestChildrenAsync() => StopAllChildrenAsync();
}

/// <summary>简单可释放对象 — 用于测试子 Actor 实例</summary>
internal sealed class DisposableStub : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>ChildActorHandle 测试扩展 — 暴露 HandleFailureAsync 供测试调用</summary>
internal static class ChildActorHandleTestExtensions
{
    public static ValueTask HandleTestFailureAsync(this ChildActorHandle handle, Exception ex)
        => handle.HandleFailureAsync(ex, CancellationToken.None);
}
