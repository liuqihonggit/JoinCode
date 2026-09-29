namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// BoundedUUIDSet 确定性测试 — FIFO 环形缓冲去重集合，单线程无时序依赖。
/// </summary>
public class BoundedUUIDSetTest {
    /// <summary>容量非正抛 ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveCapacity_Throws(int capacity) {
        var act = () => new BoundedUUIDSet(capacity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>同步 Add 按添加顺序保留元素。</summary>
    [Fact]
    public async Task Add_PreservesInsertionOrder() {
        await using var set = new BoundedUUIDSet(5);
        set.Add("a");
        set.Add("b");
        set.Add("c");

        var list = await set.ToListAsync();

        list.Should().Equal("a", "b", "c");
    }

    /// <summary>同步 Add 重复元素被忽略，不增加计数。</summary>
    [Fact]
    public async Task Add_Duplicate_Ignored() {
        await using var set = new BoundedUUIDSet(5);
        set.Add("a");
        set.Add("b");
        set.Add("a"); // 重复

        var list = await set.ToListAsync();

        list.Should().Equal("a", "b");
        (await set.GetCountAsync()).Should().Be(2);
    }

    /// <summary>满容量后 Add 触发 FIFO 淘汰最旧元素。</summary>
    [Fact]
    public async Task Add_FullCapacity_EvictsOldestFifo() {
        await using var set = new BoundedUUIDSet(3);
        set.Add("a");
        set.Add("b");
        set.Add("c");
        set.Add("d"); // 满，淘汰 a

        var list = await set.ToListAsync();

        list.Should().Equal("b", "c", "d");
        set.Contains("a").Should().BeFalse();
        set.Contains("d").Should().BeTrue();
    }

    /// <summary>连续 FIFO 淘汰多轮后顺序正确。</summary>
    [Fact]
    public async Task Add_MultipleEvictions_OrderCorrect() {
        await using var set = new BoundedUUIDSet(3);
        set.Add("a");
        set.Add("b");
        set.Add("c");
        set.Add("d"); // 淘汰 a → [b,c,d]
        set.Add("e"); // 淘汰 b → [c,d,e]

        var list = await set.ToListAsync();

        list.Should().Equal("c", "d", "e");
    }

    /// <summary>淘汰后的旧元素可重新添加。</summary>
    [Fact]
    public async Task Add_EvictedElement_CanBeReAdded() {
        await using var set = new BoundedUUIDSet(2);
        set.Add("a");
        set.Add("b");
        set.Add("c"); // 淘汰 a → [b,c]
        set.Add("a"); // a 已被淘汰，可重新添加 → 淘汰 b → [c,a]

        var list = await set.ToListAsync();

        list.Should().Equal("c", "a");
        set.Contains("a").Should().BeTrue();
    }

    /// <summary>空集合 ToListAsync 返回空列表。</summary>
    [Fact]
    public async Task ToListAsync_Empty_ReturnsEmptyList() {
        await using var set = new BoundedUUIDSet(3);

        var list = await set.ToListAsync();

        list.Should().BeEmpty();
    }

    /// <summary>Contains 对空字符串返回 false。</summary>
    [Fact]
    public async Task Contains_EmptyString_ReturnsFalse() {
        await using var set = new BoundedUUIDSet(3);
        set.Add("a");

        set.Contains("").Should().BeFalse();
        set.Contains(null!).Should().BeFalse();
    }

    /// <summary>同步 Add 空字符串/null 静默跳过（不抛异常）。</summary>
    [Fact]
    public async Task Add_EmptyOrNull_SilentlySkipped() {
        await using var set = new BoundedUUIDSet(3);

        var act = () => {
            set.Add("");
            set.Add(null!);
        };

        act.Should().NotThrow();
        (await set.GetCountAsync()).Should().Be(0);
    }

    /// <summary>异步 AddAsync 返回 true 表示新增、false 表示已存在。</summary>
    [Fact]
    public async Task AddAsync_ReturnsTrueForNewFalseForDuplicate() {
        await using var set = new BoundedUUIDSet(3);

        (await set.AddAsync("x")).Should().BeTrue();
        (await set.AddAsync("y")).Should().BeTrue();
        (await set.AddAsync("x")).Should().BeFalse(); // 已存在
    }

    /// <summary>异步 AddAsync 空字符串抛 ArgumentException。</summary>
    [Fact]
    public async Task AddAsync_EmptyString_Throws() {
        await using var set = new BoundedUUIDSet(3);

        var act = async () => await set.AddAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>HasAsync 查询存在性。</summary>
    [Fact]
    public async Task HasAsync_QueriesExistence() {
        await using var set = new BoundedUUIDSet(3);
        set.Add("a");
        set.Add("b");

        (await set.HasAsync("a")).Should().BeTrue();
        (await set.HasAsync("b")).Should().BeTrue();
        (await set.HasAsync("z")).Should().BeFalse();
        (await set.HasAsync("")).Should().BeFalse();
    }

    /// <summary>ClearAsync 清空集合。</summary>
    [Fact]
    public async Task ClearAsync_EmptiesSet() {
        await using var set = new BoundedUUIDSet(3);
        set.Add("a");
        set.Add("b");

        await set.ClearAsync();

        (await set.GetCountAsync()).Should().Be(0);
        (await set.ToListAsync()).Should().BeEmpty();
    }

    /// <summary>Capacity 属性返回构造容量。</summary>
    [Fact]
    public async Task Capacity_ReturnsConfiguredValue() {
        await using var set = new BoundedUUIDSet(7);

        set.Capacity.Should().Be(7);
    }
}
