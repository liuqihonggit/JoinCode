namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// ProfileManager 单元测试 — 验证运行期 Profile 动态切换
/// </summary>
public sealed class ProfileManagerTest {
    [Fact]
    public void Current_ReturnsInitialProfile() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        manager.Current.Should().BeSameAs(initial);
        manager.Current.Name.Should().Be("base");
    }

    [Fact]
    public async Task SwitchAsync_UpdatesCurrentProfile() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        await manager.SwitchAsync("headless");

        manager.Current.Name.Should().Be("headless");
    }

    [Fact]
    public async Task SwitchAsync_RaisesProfileSwitchingEvent() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        var events = new List<(string OldName, string NewName)>();
        manager.ProfileSwitching += (old, @new) => {
            events.Add((old.Name, @new.Name));
            return ValueTask.CompletedTask;
        };

        await manager.SwitchAsync("headless");

        events.Should().HaveCount(1);
        events[0].OldName.Should().Be("base");
        events[0].NewName.Should().Be("headless");
    }

    [Fact]
    public async Task SwitchAsync_EventHandlerException_DoesNotPreventSwitch() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        manager.ProfileSwitching += (_, _) => throw new InvalidOperationException("handler error");

        await manager.SwitchAsync("headless");

        manager.Current.Name.Should().Be("headless");
    }

    [Fact]
    public async Task SwitchAsync_EmptyProfileName_ThrowsArgumentException() {
        var resolver = MakeResolver(MakeBaseProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        await manager.Invoking(m => m.SwitchAsync(""))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SwitchAsync_UnknownProfileName_ThrowsArgumentException() {
        var resolver = MakeResolver(MakeBaseProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        await manager.Invoking(m => m.SwitchAsync("nonexistent"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SwitchAsync_MultipleSwitches_AllTakeEffect() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile(), MakeSdkProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        await manager.SwitchAsync("headless");
        manager.Current.Name.Should().Be("headless");

        await manager.SwitchAsync("sdk");
        manager.Current.Name.Should().Be("sdk");

        await manager.SwitchAsync("base");
        manager.Current.Name.Should().Be("base");
    }

    [Fact]
    public async Task SwitchAsync_ProfileSwitchingEvent_ReceivesCorrectProfiles() {
        var resolver = MakeResolver(MakeBaseProfile(), MakeHeadlessProfile());
        var initial = resolver.Resolve("base");
        var manager = new ProfileManager(resolver, initial);

        var events = new List<(string OldName, string NewName)>();
        manager.ProfileSwitching += (old, @new) => {
            events.Add((old.Name, @new.Name));
            return ValueTask.CompletedTask;
        };

        await manager.SwitchAsync("headless");
        await manager.SwitchAsync("base");

        events.Should().HaveCount(2);
        events[0].Should().Be(("base", "headless"));
        events[1].Should().Be(("headless", "base"));
    }

    // ═══════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════

    private static NodeProfileResolver MakeResolver(params NodeProfile[] profiles) {
        var dict = profiles.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);
        return new NodeProfileResolver(dict);
    }

    private static NodeProfile MakeBaseProfile() =>
        MakeProfile("base", consumers: MakeConsumers(
            ("chat-pipeline", [MakeNode("node-a", 100), MakeNode("node-b", 200)])
        ));

    private static NodeProfile MakeHeadlessProfile() =>
        MakeProfile("headless", inherits: "base", excludes: ["node-b"]);

    private static NodeProfile MakeSdkProfile() =>
        MakeProfile("sdk", inherits: "headless");

    private static NodeProfile MakeProfile(
        string name,
        string? inherits = null,
        string[]? excludes = null,
        FrozenDictionary<string, ConsumerConfig>? consumers = null) =>
        new(name, inherits,
            consumers ?? FrozenDictionary<string, ConsumerConfig>.Empty,
            (excludes ?? []).ToFrozenSet(StringComparer.Ordinal),
            [],
            FrozenDictionary<string, ConsumerConfig>.Empty);

    private static FrozenDictionary<string, ConsumerConfig> MakeConsumers(params (string Id, NodeEntryConfig[] Nodes)[] consumers) =>
        consumers.ToFrozenDictionary(c => c.Id, c => new ConsumerConfig(c.Id, c.Nodes), StringComparer.Ordinal);

    private static NodeEntryConfig MakeNode(string id, int priority) =>
        new(id, priority, null, FrozenDictionary<string, string>.Empty);
}
