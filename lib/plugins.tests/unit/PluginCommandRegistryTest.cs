namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginCommandRegistry 单元测试 — 验证命令注册、别名展开、注销和查询
/// <para>确定性测试:不依赖时序/IO,纯字典操作</para>
/// </summary>
public sealed class PluginCommandRegistryTest {
    private static PluginCommandDefinition MakeCommand(string name, string plugin = "plugin1", params string[] aliases) => new() {
        CommandName = name,
        PluginName = plugin,
        Description = $"desc for {name}",
        HandlerType = "FakeHandler",
        Aliases = [.. aliases]
    };

    [Fact]
    public async Task RegisterCommandAsync_PrimaryCommand_Registered() {
        var registry = new PluginCommandRegistry();

        await registry.RegisterCommandAsync(MakeCommand("test"));

        registry.GetCommand("test").Should().NotBeNull();
        registry.GetCommand("test")!.PluginName.Should().Be("plugin1");
    }

    [Fact]
    public async Task RegisterCommandAsync_AliasesExpanded_AllAliasesRegistered() {
        var registry = new PluginCommandRegistry();

        await registry.RegisterCommandAsync(MakeCommand("main", aliases: ["alias1", "alias2"]));

        registry.GetCommand("main").Should().NotBeNull();
        registry.GetCommand("alias1").Should().NotBeNull();
        registry.GetCommand("alias2").Should().NotBeNull();
        // 别名定义的 CommandName 应为别名本身
        registry.GetCommand("alias1")!.CommandName.Should().Be("alias1");
        // 别名应继承主命令的 PluginName/HandlerType
        registry.GetCommand("alias1")!.PluginName.Should().Be("plugin1");
        registry.GetCommand("alias1")!.HandlerType.Should().Be("FakeHandler");
    }

    [Fact]
    public async Task RegisterCommandAsync_DuplicateCommand_Skipped() {
        var registry = new PluginCommandRegistry();
        var first = MakeCommand("dup", plugin: "pluginA");
        var second = MakeCommand("dup", plugin: "pluginB");

        await registry.RegisterCommandAsync(first);
        await registry.RegisterCommandAsync(second);

        // 第二次注册应跳过,保留第一次
        registry.GetCommand("dup")!.PluginName.Should().Be("pluginA");
    }

    [Fact]
    public async Task RegisterCommandAsync_ReturnsUndoFunction_RemovesCommandAndAliases() {
        var registry = new PluginCommandRegistry();

        var undo = await registry.RegisterCommandAsync(MakeCommand("cmd", aliases: ["a1", "a2"]));
        registry.GetCommand("cmd").Should().NotBeNull();
        registry.GetCommand("a1").Should().NotBeNull();

        undo();

        registry.GetCommand("cmd").Should().BeNull();
        registry.GetCommand("a1").Should().BeNull();
        registry.GetCommand("a2").Should().BeNull();
    }

    [Fact]
    public async Task UnregisterCommandAsync_RemovesCommandAndAliases() {
        var registry = new PluginCommandRegistry();
        await registry.RegisterCommandAsync(MakeCommand("cmd", aliases: ["a1"]));

        await registry.UnregisterCommandAsync("cmd");

        registry.GetCommand("cmd").Should().BeNull();
        registry.GetCommand("a1").Should().BeNull();
    }

    [Fact]
    public async Task UnregisterCommandAsync_NonExistent_NoOp() {
        var registry = new PluginCommandRegistry();

        // 不抛异常即可
        await registry.UnregisterCommandAsync("nonexistent");
    }

    [Fact]
    public async Task GetCommand_NotFound_ReturnsNull() {
        var registry = new PluginCommandRegistry();

        (await registry.RegisterCommandAsync(MakeCommand("x"))).Should().NotBeNull();
        registry.GetCommand("y").Should().BeNull();
    }

    [Fact]
    public async Task GetCommand_NullOrWhiteSpace_Throws() {
        var registry = new PluginCommandRegistry();

        Action act = () => registry.GetCommand("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GetRegisteredCommands_ReturnsAllRegistered() {
        var registry = new PluginCommandRegistry();
        await registry.RegisterCommandAsync(MakeCommand("cmd1", plugin: "p1"));
        await registry.RegisterCommandAsync(MakeCommand("cmd2", plugin: "p2"));

        var all = registry.GetRegisteredCommands().ToList();
        all.Should().HaveCount(2);
        all.Select(c => c.CommandName).Should().Contain(["cmd1", "cmd2"]);
    }

    [Fact]
    public async Task RegisterCommandAsync_NullCommand_Throws() {
        var registry = new PluginCommandRegistry();

        Func<Task> act = () => registry.RegisterCommandAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterCommandAsync_CaseInsensitive_KeyComparison() {
        var registry = new PluginCommandRegistry();
        await registry.RegisterCommandAsync(MakeCommand("TestCommand"));

        // OrdinalIgnoreCase 比较器:大小写不敏感
        registry.GetCommand("testcommand").Should().NotBeNull();
        registry.GetCommand("TESTCOMMAND").Should().NotBeNull();
    }
}
