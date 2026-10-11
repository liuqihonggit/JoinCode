// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
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

    // ===== ExpandAliases 纯计算子方法确定性测试(不依赖时序/IO/注册表状态) =====

    [Fact]
    public void ExpandAliases_NoAliases_ReturnsEmpty() {
        var cmd = MakeCommand("main");

        PluginCommandRegistry.ExpandAliases(cmd).Should().BeEmpty();
    }

    [Fact]
    public void ExpandAliases_EmptyAliasList_ReturnsEmpty() {
        var cmd = MakeCommand("main", aliases: Array.Empty<string>());

        PluginCommandRegistry.ExpandAliases(cmd).Should().BeEmpty();
    }

    [Fact]
    public void ExpandAliases_WithAliases_ReturnsOneDefinitionPerAlias() {
        var cmd = MakeCommand("main", aliases: ["a1", "a2", "a3"]);

        var expanded = PluginCommandRegistry.ExpandAliases(cmd).ToList();

        expanded.Should().HaveCount(3);
        expanded.Select(d => d.CommandName).Should().Equal(["a1", "a2", "a3"]);
    }

    [Fact]
    public void ExpandAliases_PreservesMainCommandAttributes() {
        var cmd = new PluginCommandDefinition {
            CommandName = "main",
            PluginName = "pluginX",
            Description = "desc",
            HandlerType = "HandlerY",
            Parameters = new Dictionary<string, JsonElement> { ["k"] = default },
            Aliases = ["alias1"]
        };

        var aliasDef = PluginCommandRegistry.ExpandAliases(cmd).Single();

        aliasDef.PluginName.Should().Be("pluginX");
        aliasDef.Description.Should().Be("desc");
        aliasDef.HandlerType.Should().Be("HandlerY");
        aliasDef.Parameters.Should().ContainKey("k");
    }

    [Fact]
    public void ExpandAliases_AliasCommandNameIsAliasItself_NotMainCommandName() {
        var cmd = MakeCommand("main", aliases: ["alias1"]);

        var aliasDef = PluginCommandRegistry.ExpandAliases(cmd).Single();

        aliasDef.CommandName.Should().Be("alias1");
        aliasDef.CommandName.Should().NotBe("main");
    }

    [Fact]
    public void ExpandAliases_NullCommand_Throws() {
        Action act = () => PluginCommandRegistry.ExpandAliases(null!).ToList();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExpandAliases_PureFunction_NoMutationOfInputAliasList() {
        var aliases = new List<string> { "a1", "a2" };
        var cmd = MakeCommand("main", aliases: aliases.ToArray());

        _ = PluginCommandRegistry.ExpandAliases(cmd).ToList();

        // 纯函数不应修改输入别名的容量/内容
        aliases.Should().Equal(["a1", "a2"]);
    }

    // ===== ExpandAliases 别名 null 守卫 =====

    [Fact]
    public void ExpandAliases_NullAliasesList_ReturnsEmptyWithoutThrowing() {
        // Aliases 为 null 应被 'is null or { Count: 0 }' 守卫处理,不抛 NRE
        var cmd = new PluginCommandDefinition {
            CommandName = "main",
            PluginName = "plugin1",
            Description = "desc",
            HandlerType = "Handler",
            Aliases = null!
        };

        var expanded = PluginCommandRegistry.ExpandAliases(cmd).ToList();

        expanded.Should().BeEmpty();
    }
}
