namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// SlashSuggester 分组集成测试 — 空查询分组叠加（非替换）：
/// 注入 CommandGrouping 后 "/" 走分组视图、有查询仍走 6 级匹配排序；
/// 未注入（默认构造）空查询维持字母序（既有测试零破坏回归保障）。
/// </summary>
public sealed class SlashSuggesterGroupingTests
{
    /// <summary>时间轴基准。</summary>
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>多来源命令清单。</summary>
    private static List<CommandSuggestion> Catalog() =>
    [
        new("compact", "压缩", [], IsHidden: false),
        new("hello", "问候", [], IsHidden: false),
        new("help", "帮助", [], IsHidden: false),
        new("theme", "主题", [], IsHidden: false, Arguments: ["dark", "light", "auto"]),
        new("agent", "智能体", [], IsHidden: false, Source: CommandSource.User),
        new("deploy", "部署", [], IsHidden: false, Source: CommandSource.Project),
    ];

    /// <summary>注入分组：空查询走分组视图（recently-used 前置 → builtin → user → project）。</summary>
    [Fact]
    public void Update_SlashOnly_WithGrouping_RecentThenSources()
    {
        var time = new FixedTimeProvider(Base);
        var usage = new CommandUsageMemory(timeProvider: time);
        usage.RecordUse("theme");
        usage.RecordUse("theme");
        usage.RecordUse("hello");

        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(usage));
        suggester.Update("/");

        Assert.Equal(
            ["theme", "hello", "compact", "help", "agent", "deploy"],
            suggester.Items.Select(item => item.Name).ToArray());
    }

    /// <summary>未注入分组（默认构造）：空查询维持既有字母序（零破坏回归保障）。</summary>
    [Fact]
    public void Update_SlashOnly_WithoutGrouping_Alphabetical()
    {
        var suggester = new SlashSuggester(Catalog());
        suggester.Update("/");

        Assert.Equal(
            ["agent", "compact", "deploy", "hello", "help", "theme"],
            suggester.Items.Select(item => item.Name).ToArray());
    }

    /// <summary>注入分组但有查询：仍走 6 级匹配排序（分组只管空查询 — 两套排序并存）。</summary>
    [Fact]
    public void Update_PrefixQuery_WithGrouping_MatchRankingUnchanged()
    {
        var usage = new CommandUsageMemory();
        usage.RecordUse("theme");

        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(usage));
        suggester.Update("/he");

        Assert.Equal(
            ["help", "hello", "theme"],
            suggester.Items.Select(item => item.Name).ToArray());
    }

    /// <summary>注入分组后使用记录更新：分组视图实时反映（重新 Update 重投影）。</summary>
    [Fact]
    public void Update_AfterRecordUse_RecentOrderRefreshes()
    {
        var time = new FixedTimeProvider(Base);
        var usage = new CommandUsageMemory(timeProvider: time);
        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(usage));

        usage.RecordUse("hello");
        suggester.Update("/");
        Assert.Equal("hello", suggester.Items[0].Name);

        time.AdvanceTo(Base.AddMinutes(1));
        usage.RecordUse("theme");
        suggester.Update("/");
        Assert.Equal("theme", suggester.Items[0].Name);
    }

    /// <summary>分组视图与 Esc 抑制共存：Dismiss 后同一输入不再触发（既有抑制语义不受分组影响）。</summary>
    [Fact]
    public void Dismiss_WithGrouping_SuppressesSameInput()
    {
        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(new CommandUsageMemory()));
        suggester.Update("/");
        Assert.True(suggester.IsActive);

        suggester.Dismiss();
        Assert.False(suggester.IsActive);

        suggester.Update("/");
        Assert.False(suggester.IsActive);
    }

    /// <summary>分组视图选中导航：循环语义不受分组影响。</summary>
    [Fact]
    public void Move_WithGrouping_WrapsAround()
    {
        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(new CommandUsageMemory()));
        suggester.Update("/");

        suggester.Move(-1);
        Assert.Equal(suggester.Items.Count - 1, suggester.SelectedIndex);
    }

    /// <summary>分组注入不影响参数模式与幽灵文本（既有行为保持）。</summary>
    [Fact]
    public void Update_ArgumentMode_WithGrouping_Unchanged()
    {
        var suggester = new SlashSuggester(Catalog(), new CommandGrouping(new CommandUsageMemory()));
        suggester.Update("/theme ");

        Assert.True(suggester.IsArgumentMode);
        Assert.Equal(["dark", "light", "auto"], suggester.Items.Select(item => item.Name).ToArray());
    }

    /// <summary>CommandSuggestion 默认 Source = Builtin（宿主未注入来源时的兜底）。</summary>
    [Fact]
    public void CommandSuggestion_DefaultSource_IsBuiltin()
    {
        var command = new CommandSuggestion("compact", "压缩", [], IsHidden: false);
        Assert.Equal(CommandSource.Builtin, command.Source);
    }
}
