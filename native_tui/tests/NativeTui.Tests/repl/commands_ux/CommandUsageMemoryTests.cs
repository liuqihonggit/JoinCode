namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>固定时间提供者 — CommandUsageMemory 的时间注入面（手动推进时间轴，LRU 确定性验证）。</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <summary>创建（初始时刻）。</summary>
    public FixedTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;

    /// <summary>推进到指定时刻（时间轴单调性由测试用例保证）。</summary>
    public void AdvanceTo(DateTimeOffset utcNow) => _utcNow = utcNow;

    /// <summary>当前 UTC 时刻（注入值）。</summary>
    public override DateTimeOffset GetUtcNow() => _utcNow;
}

/// <summary>
/// CommandUsageMemory 单元测试 — 使用计数累加/时间戳刷新、LRU 容量淘汰（最旧出局/刷新保活）、
/// 快照排序（计数降序 → 时间降序 → 名字序）、参数校验。
/// </summary>
public sealed class CommandUsageMemoryTests
{
    /// <summary>时间轴基准（所有用例的相对零点）。</summary>
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>记录使用：计数累加 + 最近时间刷新。</summary>
    [Fact]
    public void RecordUse_AccumulatesCountAndRefreshesTimestamp()
    {
        var time = new FixedTimeProvider(Base);
        var memory = new CommandUsageMemory(timeProvider: time);

        memory.RecordUse("compact");
        time.AdvanceTo(Base.AddMinutes(5));
        memory.RecordUse("compact");

        var usage = memory.TryGetUsage("compact");
        Assert.NotNull(usage);
        Assert.Equal(2, usage.Count);
        Assert.Equal(Base.AddMinutes(5), usage.LastUsed);
    }

    /// <summary>未记录命令：TryGetUsage 返回 null。</summary>
    [Fact]
    public void TryGetUsage_Untracked_ReturnsNull()
    {
        var memory = new CommandUsageMemory();
        Assert.Null(memory.TryGetUsage("compact"));
    }

    /// <summary>LRU 淘汰：超容量时最近使用时间最旧的出局。</summary>
    [Fact]
    public void RecordUse_EvictsLeastRecentlyUsedBeyondCapacity()
    {
        var time = new FixedTimeProvider(Base);
        var memory = new CommandUsageMemory(capacity: 3, timeProvider: time);

        memory.RecordUse("a");
        time.AdvanceTo(Base.AddMinutes(1));
        memory.RecordUse("b");
        time.AdvanceTo(Base.AddMinutes(2));
        memory.RecordUse("c");
        time.AdvanceTo(Base.AddMinutes(3));
        memory.RecordUse("d");

        Assert.Equal(3, memory.Count);
        Assert.Null(memory.TryGetUsage("a"));
        Assert.NotNull(memory.TryGetUsage("b"));
        Assert.NotNull(memory.TryGetUsage("c"));
        Assert.NotNull(memory.TryGetUsage("d"));
    }

    /// <summary>LRU 保活：刷新旧条目时间后它不再是最旧，淘汰落在未刷新条目。</summary>
    [Fact]
    public void RecordUse_RefreshKeepsEntryAlive()
    {
        var time = new FixedTimeProvider(Base);
        var memory = new CommandUsageMemory(capacity: 3, timeProvider: time);

        memory.RecordUse("a");
        time.AdvanceTo(Base.AddMinutes(1));
        memory.RecordUse("b");
        time.AdvanceTo(Base.AddMinutes(2));
        memory.RecordUse("c");
        time.AdvanceTo(Base.AddMinutes(3));
        memory.RecordUse("a");
        time.AdvanceTo(Base.AddMinutes(4));
        memory.RecordUse("d");

        Assert.Equal(3, memory.Count);
        Assert.NotNull(memory.TryGetUsage("a"));
        Assert.Null(memory.TryGetUsage("b"));
        Assert.NotNull(memory.TryGetUsage("c"));
        Assert.NotNull(memory.TryGetUsage("d"));
    }

    /// <summary>快照排序：计数降序 → 最近使用降序 → 名字字母序。</summary>
    [Fact]
    public void Snapshot_OrdersByCountDescThenLastUsedDescThenName()
    {
        var time = new FixedTimeProvider(Base);
        var memory = new CommandUsageMemory(timeProvider: time);

        memory.RecordUse("alpha");
        time.AdvanceTo(Base.AddMinutes(1));
        memory.RecordUse("beta");
        memory.RecordUse("beta");
        time.AdvanceTo(Base.AddMinutes(2));
        memory.RecordUse("gamma");
        memory.RecordUse("gamma");
        time.AdvanceTo(Base.AddMinutes(3));
        memory.RecordUse("delta");
        memory.RecordUse("delta");

        Assert.Equal(
            ["delta", "gamma", "beta", "alpha"],
            memory.Snapshot().Select(pair => pair.Name).ToArray());
    }

    /// <summary>参数校验：空命令名抛 ArgumentException。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RecordUse_EmptyName_Throws(string commandName)
    {
        var memory = new CommandUsageMemory();
        Assert.Throws<ArgumentException>(() => memory.RecordUse(commandName));
    }

    /// <summary>参数校验：容量小于 1 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    public void Constructor_ZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandUsageMemory(capacity: 0));
    }

    /// <summary>默认容量常量 = 32（任务契约：容量上限 LRU）。</summary>
    [Fact]
    public void DefaultCapacity_Is32()
    {
        Assert.Equal(32, CommandUsageMemory.DefaultCapacity);
    }
}
