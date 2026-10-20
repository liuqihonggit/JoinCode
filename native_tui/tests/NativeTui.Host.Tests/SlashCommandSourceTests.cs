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
                    new SlashCommandMetadata { Name = "theme", Description = "主题" },
                    new SlashCommandMetadata { Name = "effort", Description = "力度" },
                    new SlashCommandMetadata { Name = "config", Description = "配置" },
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

    /// <summary>参数元数据注入：ArgumentHint 透传特性；theme/effort/config 静态参数清单注入。</summary>
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
}
