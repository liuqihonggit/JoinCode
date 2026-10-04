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
}

/// <summary>可追踪 Dispose 状态的测试 Actor — IsDisposed 由 ActorBase 提供</summary>
internal sealed class DisposableTestActor : ActorBase<string, string> {
    protected override void Handle(string command, CancellationToken ct) { }
}
