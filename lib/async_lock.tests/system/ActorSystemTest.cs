namespace Core.Utils;

/// <summary>
/// ActorSystem 测试 — DSG033 S4 注册表 + 路径寻址 + Guardian 兜底。
/// </summary>
public class ActorSystemTest {
    [Fact]
    public async Task Register_And_Select_ReturnsSameInstance() {
        await using var system = new ActorSystem("test");
        var actor = new DisposableTestActor();
        system.Register("my-actor", actor);

        system.Selection("my-actor").Should().BeSameAs(actor);
    }

    [Fact]
    public async Task Select_Unregistered_ReturnsNull() {
        await using var system = new ActorSystem();
        system.Selection("nope").Should().BeNull();
    }

    [Fact]
    public async Task Unregister_RemovesActor() {
        await using var system = new ActorSystem();
        var actor = new DisposableTestActor();
        system.Register("a1", actor);

        system.Unregister("a1").Should().BeTrue();
        system.Selection("a1").Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_CascadesDisposeToAllActors() {
        var system = new ActorSystem();
        var actor = new DisposableTestActor();
        system.Register("a1", actor);

        await system.DisposeAsync();

        actor.IsDisposed.Should().BeTrue("ActorSystem Dispose 应级联停止所有注册 Actor");
    }

    [Fact]
    public async Task GetRegisteredIds_ReturnsAllIds() {
        await using var system = new ActorSystem();
        system.Register("a1", new DisposableTestActor());
        system.Register("a2", new DisposableTestActor());

        system.GetRegisteredIds().Should().BeEquivalentTo(new[] { "a1", "a2" });
    }

    [Fact]
    public async Task Register_AfterDispose_ThrowsObjectDisposed() {
        var system = new ActorSystem();
        await system.DisposeAsync();

        var act = () => system.Register("x", new DisposableTestActor());
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>ActorSelection.Resolve 找到注册的 Actor(Akka 对齐)</summary>
    [Fact]
    public async Task ActorSelection_Resolve_FindsActor() {
        await using var system = new ActorSystem();
        var actor = new SelectionTestActor();
        system.Register("target", actor);

        var selection = system.ActorSelection("target");
        selection.Resolve().Should().BeSameAs(actor);
    }

    /// <summary>ActorSelection.Tell 发消息给选中的 Actor(Akka 对齐)</summary>
    [Fact]
    public async Task ActorSelection_Tell_SendsMessage() {
        await using var system = new ActorSystem();
        var actor = new SelectionTestActor();
        system.Register("target", actor);

        system.ActorSelection("target").Tell("hello");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        actor.ProcessedCommands.Should().Contain("hello");
    }

    /// <summary>ActorSelection.Resolve 不存在时返回 null(Akka 对齐)</summary>
    [Fact]
    public async Task ActorSelection_Resolve_ReturnsNull_WhenNotFound() {
        await using var system = new ActorSystem();
        system.ActorSelection("nope").Resolve().Should().BeNull();
    }

    /// <summary>ActorSelection.Tell 找不到 Actor 时发布 DeadLetter 事件(Akka 对齐)</summary>
    [Fact]
    public async Task DeadLetter_Published_WhenActorNotFound() {
        await using var system = new ActorSystem();
        DeadLetter? captured = null;
        system.EventStream.Subscribe<DeadLetter>(dl => captured = dl);

        system.ActorSelection("nonexistent").Tell("lost-msg");

        await TestWaitHelper.WaitUntilAsync(() => captured is not null, TimeSpan.FromMilliseconds(500));
        captured.Should().NotBeNull();
        captured!.Message.Should().Be("lost-msg");
        captured.Path.Should().Be("nonexistent");
    }
}

/// <summary>可追踪 Dispose 状态的测试 Actor — IsDisposed 由 ActorBase 提供</summary>
internal sealed class DisposableTestActor : ActorBase<string, string> {
    protected override void Handle(string command, CancellationToken ct) { }
}

/// <summary>ActorSelection 测试 Actor — 记录处理过的命令</summary>
internal sealed class SelectionTestActor : ActorBase<string, Unit> {
    public readonly List<string> ProcessedCommands = new();
    protected override void Handle(string command, CancellationToken ct) {
        ProcessedCommands.Add(command);
    }
}
