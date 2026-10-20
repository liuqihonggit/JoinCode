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
        Assert.Equal(["clear", "help", "secret"], commands.Select(c => c.Name).OrderBy(n => n).ToArray());
        var help = commands.Single(c => c.Name == "help");
        Assert.Equal("显示帮助", help.Description);
        Assert.Equal(["h"], help.Aliases);
        Assert.False(help.IsHidden);
        Assert.True(commands.Single(c => c.Name == "secret").IsHidden);
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
