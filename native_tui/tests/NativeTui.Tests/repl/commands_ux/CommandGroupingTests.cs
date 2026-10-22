namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// CommandGrouping 单元测试 — 空查询分组投影：recently-used top5（计数降序 → 时间降序）→
/// builtin → user → project → policy → other；组内字母序；recently-used 去重不进来源组；
/// 隐藏命令排除；空组省略；扁平/分组双投影一致性。
/// </summary>
public sealed class CommandGroupingTests
{
    /// <summary>时间轴基准。</summary>
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>构造多来源命令清单（覆盖 builtin/user/project/policy/other + 隐藏命令）。</summary>
    private static List<CommandSuggestion> Catalog() =>
    [
        new("compact", "压缩", [], IsHidden: false),
        new("theme", "主题", [], IsHidden: false),
        new("agent", "智能体", [], IsHidden: false, Source: CommandSource.User),
        new("deploy", "部署", [], IsHidden: false, Source: CommandSource.Project),
        new("audit", "审计", [], IsHidden: false, Source: CommandSource.Policy),
        new("secret", "隐藏", [], IsHidden: true, Source: CommandSource.Policy),
    ];

    /// <summary>无使用记录：纯来源组拼接（builtin → user → project → policy），组内字母序。</summary>
    [Fact]
    public void Project_WithoutUsage_SourceGroupsInOrder()
    {
        var grouping = new CommandGrouping(new CommandUsageMemory());
        Assert.Equal(
            ["compact", "theme", "agent", "deploy", "audit"],
            grouping.Project(Catalog()).Select(command => command.Name).ToArray());
    }

    /// <summary>有使用记录：recently-used 前置（计数降序），其余进来源组。</summary>
    [Fact]
    public void Project_WithUsage_RecentFirstThenSources()
    {
        var time = new FixedTimeProvider(Base);
        var usage = new CommandUsageMemory(timeProvider: time);
        usage.RecordUse("theme");
        usage.RecordUse("theme");
        usage.RecordUse("theme");
        usage.RecordUse("compact");

        var grouping = new CommandGrouping(usage);
        Assert.Equal(
            ["theme", "compact", "agent", "deploy", "audit"],
            grouping.Project(Catalog()).Select(command => command.Name).ToArray());
    }

    /// <summary>recently-used 计数并列：最近使用时间降序（新者在前）。</summary>
    [Fact]
    public void Project_RecentTieBreaksByLatestUse()
    {
        var time = new FixedTimeProvider(Base);
        var usage = new CommandUsageMemory(timeProvider: time);
        usage.RecordUse("compact");
        time.AdvanceTo(Base.AddMinutes(1));
        usage.RecordUse("theme");

        var grouping = new CommandGrouping(usage);
        var names = grouping.Project(Catalog()).Select(command => command.Name).ToArray();
        Assert.Equal("theme", names[0]);
        Assert.Equal("compact", names[1]);
    }

    /// <summary>recently-used 上限 top5：第 6 个已用命令不再前置（容量外的进来源组）。</summary>
    [Fact]
    public void Project_RecentLimitedToTop5()
    {
        var time = new FixedTimeProvider(Base);
        var usage = new CommandUsageMemory(timeProvider: time);
        var minute = 0;
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f" })
        {
            usage.RecordUse(name);
            minute++;
            time.AdvanceTo(Base.AddMinutes(minute));
        }

        var commands = new List<CommandSuggestion>
        {
            new("a", "描述", [], IsHidden: false),
            new("b", "描述", [], IsHidden: false),
            new("c", "描述", [], IsHidden: false),
            new("d", "描述", [], IsHidden: false),
            new("e", "描述", [], IsHidden: false),
            new("f", "描述", [], IsHidden: false),
        };
        var recent = new CommandGrouping(usage).RecentCommands(commands);
        Assert.Equal(["f", "e", "d", "c", "b"], recent.Select(command => command.Name).ToArray());
    }

    /// <summary>去重：recently-used 已含命令不再出现在来源组。</summary>
    [Fact]
    public void Project_RecentCommandsNotDuplicatedInSourceGroups()
    {
        var usage = new CommandUsageMemory();
        usage.RecordUse("theme");

        var flattened = new CommandGrouping(usage).Project(Catalog());
        Assert.Equal(flattened.Count, flattened.Select(command => command.Name).Distinct().Count());
        Assert.Equal("theme", flattened[0].Name);
    }

    /// <summary>隐藏命令不参与分组投影。</summary>
    [Fact]
    public void Project_HiddenCommandsExcluded()
    {
        var usage = new CommandUsageMemory();
        usage.RecordUse("secret");

        var flattened = new CommandGrouping(usage).Project(Catalog());
        Assert.DoesNotContain(flattened, command => command.Name == "secret");
    }

    /// <summary>分组投影：空组省略（无使用记录无 RecentlyUsed 组；缺来源无对应组）。</summary>
    [Fact]
    public void ProjectGrouped_EmptyGroupsOmitted()
    {
        var grouping = new CommandGrouping(new CommandUsageMemory());
        var groups = grouping.ProjectGrouped(Catalog());

        Assert.Equal(
            [CommandGroupKind.Builtin, CommandGroupKind.User, CommandGroupKind.Project, CommandGroupKind.Policy],
            groups.Select(group => group.Kind).ToArray());
    }

    /// <summary>分组投影：有使用记录时 RecentlyUsed 组置首，组内排序与扁平投影一致。</summary>
    [Fact]
    public void ProjectGrouped_RecentGroupFirst()
    {
        var usage = new CommandUsageMemory();
        usage.RecordUse("audit");

        var groups = new CommandGrouping(usage).ProjectGrouped(Catalog());
        Assert.Equal(CommandGroupKind.RecentlyUsed, groups[0].Kind);
        Assert.Equal(["audit"], groups[0].Items.Select(command => command.Name).ToArray());

        var flattened = new CommandGrouping(usage).Project(Catalog());
        Assert.Equal(
            flattened.Select(command => command.Name).ToArray(),
            groups.SelectMany(group => group.Items).Select(command => command.Name).ToArray());
    }

    /// <summary>Other 来源兜底组排在最后。</summary>
    [Fact]
    public void Project_OtherSourceGoesLast()
    {
        var commands = new List<CommandSuggestion>
        {
            new("zeta", "描述", [], IsHidden: false, Source: CommandSource.Other),
            new("alpha", "描述", [], IsHidden: false, Source: CommandSource.Builtin),
        };

        var grouping = new CommandGrouping(new CommandUsageMemory());
        Assert.Equal(
            [CommandGroupKind.Builtin, CommandGroupKind.Other],
            grouping.ProjectGrouped(commands).Select(group => group.Kind).ToArray());
    }

    /// <summary>组头中文标题（渲染分组头消费的翻译点 — 全类别覆盖）。</summary>
    [Theory]
    [InlineData(CommandGroupKind.RecentlyUsed, "最近使用")]
    [InlineData(CommandGroupKind.Builtin, "内置命令")]
    [InlineData(CommandGroupKind.User, "用户命令")]
    [InlineData(CommandGroupKind.Project, "项目命令")]
    [InlineData(CommandGroupKind.Policy, "策略命令")]
    [InlineData(CommandGroupKind.Other, "其他命令")]
    public void Title_CoversAllKinds(CommandGroupKind kind, string expected)
    {
        Assert.Equal(expected, CommandGroupKindInfo.Title(kind));
    }
}
