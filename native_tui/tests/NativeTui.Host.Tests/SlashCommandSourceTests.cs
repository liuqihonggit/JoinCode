namespace NativeTui.Host.Tests;

/// <summary>
/// 斜杠命令清单源测试 — DI 解析目录 → CommandSuggestion 纯数据转换：
/// name 去 / 前缀、IsEnabled=false 过滤、IsHidden 透传（补全层处理可见性）、DI 未注册兜底直接实例化生成目录。
/// </summary>
public sealed class SlashCommandSourceTests
{
    /// <summary>假命令目录（手动构造元数据）。</summary>
    private sealed class FakeCatalog : ISlashCommandCatalog
    {
        public IReadOnlyDictionary<string, IReadOnlyList<SlashCommandMetadata>> ByCategory { get; } =
            new Dictionary<string, IReadOnlyList<SlashCommandMetadata>>
            {
                ["General"] =
                [
                    new SlashCommandMetadata { Name = "/help", Description = "显示帮助", Aliases = ["h"], IsHidden = false },
                    new SlashCommandMetadata { Name = "/secret", Description = "隐藏", IsHidden = true },
                    new SlashCommandMetadata { Name = "/disabled", Description = "禁用", IsEnabled = false },
                ],
                ["Session"] =
                [
                    new SlashCommandMetadata { Name = "clear", Description = "清屏" },
                    new SlashCommandMetadata { Name = "theme", Description = "主题", Arguments = ["dark", "light", "auto"] },
                    new SlashCommandMetadata { Name = "effort", Description = "力度", Arguments = ["auto", "low", "medium", "high", "max"] },
                    new SlashCommandMetadata { Name = "config", Description = "配置", Arguments = ["get", "set", "list", "remove"] },
                ],
            };
    }

    /// <summary>构造注册假目录的服务容器。</summary>
    private static IServiceProvider Services() =>
        new ServiceCollection().AddSingleton<ISlashCommandCatalog>(new FakeCatalog()).BuildServiceProvider();

    /// <summary>转换：name 去 / 前缀、描述/别名/隐藏透传、禁用命令过滤、跨分类合并。</summary>
    [Fact]
    public void Load_ConvertsMetadataToSuggestions()
    {
        var commands = SlashCommandSource.Load(Services());
        Assert.Equal(["clear", "config", "effort", "help", "secret", "theme"], commands.Select(c => c.Name).OrderBy(n => n).ToArray());
        var help = commands.Single(c => c.Name == "help");
        Assert.Equal("显示帮助", help.Description);
        Assert.Equal(["h"], help.Aliases);
        Assert.False(help.IsHidden);
        Assert.True(commands.Single(c => c.Name == "secret").IsHidden);
    }

    /// <summary>参数元数据注入：ArgumentHint 透传特性；参数值域来自元数据 Arguments 字段（生成目录注入引擎真值）。</summary>
    [Fact]
    public void Load_InjectsArgumentMetadata()
    {
        var commands = SlashCommandSource.Load(Services());
        var theme = commands.Single(c => c.Name == "theme");
        Assert.Equal(["dark", "light", "auto"], theme.ArgumentValues);
        var effort = commands.Single(c => c.Name == "effort");
        Assert.Equal(5, effort.ArgumentValues.Length);
        var config = commands.Single(c => c.Name == "config");
        Assert.Equal(["get", "set", "list", "remove"], config.ArgumentValues);
        Assert.Empty(commands.Single(c => c.Name == "clear").ArgumentValues);
    }

    /// <summary>真实生成目录参数值域（修复验收缺陷：/theme d 无效果）— theme 7 值（含 daltonized/ansi）、
    /// effort 6 值（含 unset）、config 4 值，全部来自引擎 [ChatCommandArg].Enum 特性（唯一数据源，双源消除）。</summary>
    [Fact]
    public void Load_GeneratedCatalog_ArgumentValuesFromEngineTruth()
    {
        var commands = SlashCommandSource.Load(new ServiceCollection().BuildServiceProvider());

        var theme = commands.Single(c => c.Name == "theme");
        Assert.Equal(
            ["dark", "light", "auto", "dark-daltonized", "light-daltonized", "dark-ansi", "light-ansi"],
            theme.ArgumentValues);

        var effort = commands.Single(c => c.Name == "effort");
        Assert.Equal(6, effort.ArgumentValues.Length);
        Assert.Contains("unset", effort.ArgumentValues);

        var config = commands.Single(c => c.Name == "config");
        Assert.Equal(["get", "set", "list", "remove"], config.ArgumentValues);
    }

    /// <summary>ArgumentHint 透传：特性声明的参数提示进建议项。</summary>
    [Fact]
    public void Load_PassesThroughArgumentHint()
    {
        var catalog = new FakeCatalog();
        var services = new ServiceCollection().AddSingleton<ISlashCommandCatalog>(catalog).BuildServiceProvider();
        var commands = SlashCommandSource.Load(services);
        // FakeCatalog 未声明 ArgumentHint → 空串；真实生成目录的 model 命令带 "model-id"（生成器提取）
        Assert.All(commands, c => Assert.NotNull(c.ArgumentHint));
        var generated = SlashCommandSource.Load(new ServiceCollection().BuildServiceProvider());
        Assert.Contains(generated, c => c.Name == "model" && c.ArgumentHint.Length > 0);
    }

    /// <summary>DI 未注册目录：兜底直接实例化生成目录（真实生成器输出，非空）。</summary>
    [Fact]
    public void Load_NoCatalog_FallsBackToGenerated()
    {
        var commands = SlashCommandSource.Load(new ServiceCollection().BuildServiceProvider());
        Assert.NotEmpty(commands);
        Assert.All(commands, c => Assert.False(c.Name.StartsWith('/')));
    }

    /// <summary>F5 运行时门控（TASK065）— DI 提供 ChatCommandRegistry 时按运行时实例 IsEnabled 过滤：
    /// 静态 IsEnabled=true 但 registry 实例禁用的命令（entitlement 门控形态）不进 TUI 建议列表。</summary>
    [Fact]
    public void Load_WithRuntimeRegistry_FiltersByRuntimeIsEnabled()
    {
        var registry = new ChatCommandRegistry();
        registry.Register(RuntimeGateCommand.Disabled("help"));
        registry.Register(RuntimeGateCommand.Enabled("theme"));
        var services = new ServiceCollection()
            .AddSingleton<ISlashCommandCatalog>(new FakeCatalog())
            .AddSingleton(registry)
            .BuildServiceProvider();

        var commands = SlashCommandSource.Load(services);

        Assert.DoesNotContain(commands, c => c.Name == "help");
        Assert.Contains(commands, c => c.Name == "theme");
    }

    /// <summary>F5 — registry 缺席（DI 未注册/精简宿主）：保持现状降级，静态目录全量（静态 IsEnabled 过滤依旧）。</summary>
    [Fact]
    public void Load_WithoutRuntimeRegistry_KeepsStaticCatalog()
    {
        var services = new ServiceCollection().AddSingleton<ISlashCommandCatalog>(new FakeCatalog()).BuildServiceProvider();

        var commands = SlashCommandSource.Load(services);

        Assert.Contains(commands, c => c.Name == "help");
        Assert.DoesNotContain(commands, c => c.Name == "disabled");
    }

    /// <summary>F5 — registry 有实例但命令未注册（目录与注册表理论同源，容错面）：以静态 IsEnabled 兜底不过滤。</summary>
    [Fact]
    public void Load_RegistryMissingCommand_FallsBackToStaticIsEnabled()
    {
        var registry = new ChatCommandRegistry();
        registry.Register(RuntimeGateCommand.Disabled("no-such-catalog-entry"));
        var services = new ServiceCollection()
            .AddSingleton<ISlashCommandCatalog>(new FakeCatalog())
            .AddSingleton(registry)
            .BuildServiceProvider();

        var commands = SlashCommandSource.Load(services);

        Assert.Contains(commands, c => c.Name == "help");
    }

    /// <summary>运行时门控假命令 — IsEnabled 可控的 IChatCommand 最小面（F5 测试专用）。</summary>
    private sealed class RuntimeGateCommand(string name, bool enabled) : IChatCommand
    {
        public string Name => name;
        public string Description => string.Empty;
        public string Usage => string.Empty;
        public string[] Aliases => [];
        public string ArgumentHint => string.Empty;
        public bool IsHidden => false;
        public bool IsEnabled => enabled;
        public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) => Task.FromResult(ChatCommandResult.Continue());

        /// <summary>构造启用命令。</summary>
        public static RuntimeGateCommand Enabled(string name) => new(name, true);

        /// <summary>构造禁用命令。</summary>
        public static RuntimeGateCommand Disabled(string name) => new(name, false);
    }
}
