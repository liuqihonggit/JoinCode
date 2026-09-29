namespace Structura.Tests;

/// <summary>
/// SwissTableHelper 静态辅助方法的边界值与极值测试。
/// 覆盖 h2 负数极值、is_full/is_special/special_is_empty 边界 ctrl 字节。
/// </summary>
public class SwissTableHelperBoundaryTests {
    // === h2 负数与极值测试 ===

    /// <summary>验证 h2(0) 返回 0。</summary>
    [Fact]
    public void H2_Zero_ReturnsZero() {
        SwissTableHelper.h2(0).Should().Be(0);
    }

    /// <summary>验证 h2(-1) 提取高 7 位为 127(0x7F)。</summary>
    [Fact]
    public void H2_NegativeOne_Returns127() {
        // (uint)(-1) = 0xFFFFFFFF, >> 25 = 0x7F = 127
        SwissTableHelper.h2(-1).Should().Be(127);
    }

    /// <summary>验证 h2(int.MinValue) 提取高 7 位为 64(0x40)。</summary>
    [Fact]
    public void H2_IntMinValue_Returns64() {
        // (uint)int.MinValue = 0x80000000 = 2^31, >> 25 = 2^6 = 64
        SwissTableHelper.h2(int.MinValue).Should().Be(64);
    }

    /// <summary>验证 h2(int.MaxValue) 提取高 7 位为 63。</summary>
    [Fact]
    public void H2_IntMaxValue_Returns63() {
        // (uint)int.MaxValue = 0x7FFFFFFF, >> 25 = 0x3F = 63
        SwissTableHelper.h2(int.MaxValue).Should().Be(63);
    }

    /// <summary>验证 h2 在 2^25 阈值处正确切换。</summary>
    [Theory]
    [InlineData(0x01FFFFFF, 0)] // 2^25 - 1,高 7 位全 0
    [InlineData(0x02000000, 1)] // 2^25,高 7 位 = 1
    [InlineData(0x03FFFFFF, 1)] // 2^26 - 1,高 7 位 = 1
    [InlineData(0x04000000, 2)] // 2^26,高 7 位 = 2
    public void H2_PowerOf25Boundary_ReturnsExpected(int hash, byte expected) {
        SwissTableHelper.h2(hash).Should().Be(expected);
    }

    /// <summary>验证 h2 对负数与对应无符号数的一致性(强制无符号右移)。</summary>
    [Fact]
    public void H2_NegativeHash_UsesUnsignedShift() {
        // -2 = 0xFFFFFFFE, >> 25 = 0x7F = 127
        SwissTableHelper.h2(-2).Should().Be(127);
        // int.MinValue + 1 = 0x80000001, >> 25 = 0x40 = 64
        SwissTableHelper.h2(int.MinValue + 1).Should().Be(64);
    }

    /// <summary>验证 h2 返回值始终在 [0, 127] 范围内(高 7 位,顶位为 0)。</summary>
    [Fact]
    public void H2_AlwaysInByteRange0To127() {
        var rng = new Random(12345);
        for (var i = 0; i < 1000; i++) {
            var hash = rng.Next(int.MinValue, int.MaxValue);
            var result = SwissTableHelper.h2(hash);
            result.Should().BeInRange(0, 127, "h2 提取高 7 位,顶位必须为 0 以区分 full/special");
        }
    }

    // === is_full 边界测试 ===

    /// <summary>验证 is_full 对各种 ctrl 字节的判定。</summary>
    [Theory]
    [InlineData(0x00, true)]   // 最小 full 值
    [InlineData(0x01, true)]
    [InlineData(0x7F, true)]   // 最大 full 值(顶位为 0)
    [InlineData(0x80, false)]  // DELETED,顶位为 1
    [InlineData(0x81, false)]
    [InlineData(0xFE, false)]
    [InlineData(0xFF, false)]  // EMPTY,顶位为 1
    public void IsFull_VariousCtrl_ReturnsExpected(byte ctrl, bool expected) {
        SwissTableHelper.is_full(ctrl).Should().Be(expected);
    }

    /// <summary>验证 is_full(EMPTY) 为 false。</summary>
    [Fact]
    public void IsFull_EmptyCtrl_ReturnsFalse() {
        SwissTableHelper.is_full(SwissTableHelper.EMPTY).Should().BeFalse();
    }

    /// <summary>验证 is_full(DELETED) 为 false。</summary>
    [Fact]
    public void IsFull_DeletedCtrl_ReturnsFalse() {
        SwissTableHelper.is_full(SwissTableHelper.DELETED).Should().BeFalse();
    }

    // === is_special 边界测试 ===

    /// <summary>验证 is_special 对各种 ctrl 字节的判定。</summary>
    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x7F, false)]
    [InlineData(0x80, true)]   // DELETED
    [InlineData(0x81, true)]
    [InlineData(0xFF, true)]   // EMPTY
    public void IsSpecial_VariousCtrl_ReturnsExpected(byte ctrl, bool expected) {
        SwissTableHelper.is_special(ctrl).Should().Be(expected);
    }

    /// <summary>验证 is_special 与 is_full 互斥。</summary>
    [Fact]
    public void IsSpecial_AndIsFull_AreMutuallyExclusive() {
        for (var ctrl = 0; ctrl <= 0xFF; ctrl++) {
            var b = (byte)ctrl;
            var isFull = SwissTableHelper.is_full(b);
            var isSpecial = SwissTableHelper.is_special(b);
            (isFull && isSpecial).Should().BeFalse($"ctrl={ctrl:X2} 不能同时为 full 和 special");
            (isFull || isSpecial).Should().BeTrue($"ctrl={ctrl:X2} 必须为 full 或 special 之一");
        }
    }

    // === special_is_empty 边界测试 ===

    /// <summary>验证 special_is_empty(EMPTY) 为 true。</summary>
    [Fact]
    public void SpecialIsEmpty_EmptyCtrl_ReturnsTrue() {
        SwissTableHelper.special_is_empty(SwissTableHelper.EMPTY).Should().BeTrue();
    }

    /// <summary>验证 special_is_empty(DELETED) 为 false。</summary>
    [Fact]
    public void SpecialIsEmpty_DeletedCtrl_ReturnsFalse() {
        SwissTableHelper.special_is_empty(SwissTableHelper.DELETED).Should().BeFalse();
    }

    /// <summary>验证 special_is_empty 对 special 值的判定(检查最低位)。</summary>
    [Theory]
    [InlineData(0x80, false)] // DELETED,低位 0
    [InlineData(0x81, true)]  // 低位 1
    [InlineData(0x82, false)] // 低位 0
    [InlineData(0xFE, false)] // 低位 0
    [InlineData(0xFF, true)]  // EMPTY,低位 1
    public void SpecialIsEmpty_VariousSpecialCtrl_ReturnsExpected(byte ctrl, bool expected) {
        SwissTableHelper.special_is_empty(ctrl).Should().Be(expected);
    }

    /// <summary>验证 special_is_empty_with_int_return 返回 1/0 而非 bool。</summary>
    [Theory]
    [InlineData(SwissTableHelperConsts.EMPTY, 1)]
    [InlineData(SwissTableHelperConsts.DELETED, 0)]
    public void SpecialIsEmptyWithIntReturn_ReturnsInt(byte ctrl, int expected) {
        SwissTableHelper.special_is_empty_with_int_return(ctrl).Should().Be(expected);
    }

    /// <summary>辅助常量,避免在 InlineData 中引用静态成员。</summary>
    private static class SwissTableHelperConsts {
        public const byte EMPTY = 0xFF;
        public const byte DELETED = 0x80;
    }
}

/// <summary>
/// SwissTable capacity_to_buckets 私有方法的边界测试(通过反射调用)。
/// 覆盖 capacity=0/1/最大值/阈值边界。
/// </summary>
public class SwissTableCapacityToBucketsBoundaryTests {
    private static int InvokeCapacityToBuckets(int cap) {
        var method = typeof(SwissTable<int, int>).GetMethod(
            "capacity_to_buckets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("capacity_to_buckets 方法未找到");
        return (int)method.Invoke(null, [cap])!;
    }

    /// <summary>验证 capacity=1 返回 4(最小桶数,跳过 2 桶)。</summary>
    [Fact]
    public void CapacityToBuckets_One_ReturnsFour() {
        InvokeCapacityToBuckets(1).Should().Be(4);
    }

    /// <summary>验证 capacity=3 返回 4(小于 4 走 4 桶分支)。</summary>
    [Fact]
    public void CapacityToBuckets_Three_ReturnsFour() {
        InvokeCapacityToBuckets(3).Should().Be(4);
    }

    /// <summary>验证 capacity=4 返回 8(4 桶仅能装 3 元素,跳到 8 桶)。</summary>
    [Fact]
    public void CapacityToBuckets_Four_ReturnsEight() {
        InvokeCapacityToBuckets(4).Should().Be(8);
    }

    /// <summary>验证 capacity=7 返回 8。</summary>
    [Fact]
    public void CapacityToBuckets_Seven_ReturnsEight() {
        InvokeCapacityToBuckets(7).Should().Be(8);
    }

    /// <summary>验证 capacity=8 走 87.5% 负载因子分支。</summary>
    [Fact]
    public void CapacityToBuckets_Eight_ReturnsNextPowerOfTwo() {
        // 8*8/7 = 9.14 → 9,nextPowerOfTwo(9) = 16
        InvokeCapacityToBuckets(8).Should().Be(16);
    }

    /// <summary>验证小容量阈值边界(cap < 8 分支)。</summary>
    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 8)]
    [InlineData(6, 8)]
    [InlineData(7, 8)]
    public void CapacityToBuckets_SmallCapacity_ReturnsExpected(int cap, int expected) {
        InvokeCapacityToBuckets(cap).Should().Be(expected);
    }

    /// <summary>验证中容量走 87.5% 负载因子分支。</summary>
    [Fact]
    public void CapacityToBuckets_MediumCapacity_ReturnsNextPowerOfTwo() {
        // 100 * 8 / 7 = 114, nextPowerOfTwo(114) = 128
        InvokeCapacityToBuckets(100).Should().Be(128);
    }

    /// <summary>验证 0x01FFFFFF 阈值边界(乘 8 不溢出的最大值)。</summary>
    [Fact]
    public void CapacityToBuckets_At1FFFFFF_ReturnsPowerOfTwo() {
        // 0x01FFFFFF * 8 / 7 = 38354607, nextPowerOfTwo = 2^26 = 67108864
        InvokeCapacityToBuckets(0x01FFFFFF).Should().Be(67108864);
    }

    /// <summary>验证 0x37FFFFFF 阈值边界(返回 2^30)。</summary>
    [Fact]
    public void CapacityToBuckets_At37FFFFFF_Returns2Pow30() {
        InvokeCapacityToBuckets(0x37FFFFFF).Should().Be(0x4000_0000);
    }

    /// <summary>验证超过 0x37FFFFFF 抛异常(容量溢出,反射调用包装为 TargetInvocationException)。</summary>
    [Fact]
    public void CapacityToBuckets_Above37FFFFFF_ThrowsOverflow() {
        var act = () => InvokeCapacityToBuckets(0x38000000);
        // 反射调用包装内部异常为 TargetInvocationException
        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<Exception>()
            .WithMessage("capacity overflow");
    }

    /// <summary>验证 capacity=0 行为:Debug模式触发Assert,Release模式安全返回4。</summary>
#if DEBUG
    [Fact]
    public void CapacityToBuckets_Zero_TriggersDebugAssert() {
        var act = () => InvokeCapacityToBuckets(0);
        act.Should().Throw<System.Reflection.TargetInvocationException>();
    }
#else
    [Fact]
    public void CapacityToBuckets_Zero_ReleaseMode_Returns4() {
        var result = InvokeCapacityToBuckets(0);
        result.Should().Be(4);
    }
#endif
}

/// <summary>
/// SwissTable 构造函数、EnsureCapacity、TrimExcess 的 capacity 守卫测试。
/// 覆盖 capacity &lt; 0 抛 ArgumentOutOfRangeException。
/// </summary>
public class SwissTableCapacityGuardTests {
    // === 构造函数 capacity < 0 ===

    /// <summary>验证构造函数 capacity < 0 抛 ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException(int capacity) {
        var act = () => new SwissTable<int, int>(capacity);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>验证构造函数 capacity = 0 创建空表。</summary>
    [Fact]
    public void Constructor_ZeroCapacity_CreatesEmptyTable() {
        var table = new SwissTable<int, int>(0);
        table.Count.Should().Be(0);
    }

    /// <summary>验证构造函数 capacity = 正数正常工作。</summary>
    [Fact]
    public void Constructor_PositiveCapacity_WorksCorrectly() {
        var table = new SwissTable<int, int>(16);
        table.Count.Should().Be(0);
        for (var i = 0; i < 16; i++)
            table.Add(i, i);
        table.Count.Should().Be(16);
    }

    // === EnsureCapacity capacity < 0 ===

    /// <summary>验证 EnsureCapacity(-1) 抛 ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void EnsureCapacity_Negative_ThrowsArgumentOutOfRangeException(int capacity) {
        var table = new SwissTable<int, int>();
        var act = () => table.EnsureCapacity(capacity);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>验证 EnsureCapacity(0) 不抛异常。</summary>
    [Fact]
    public void EnsureCapacity_Zero_DoesNotThrow() {
        var table = new SwissTable<int, int>();
        var act = () => table.EnsureCapacity(0);
        act.Should().NotThrow();
    }

    /// <summary>验证 EnsureCapacity(正数) 扩容成功。</summary>
    [Fact]
    public void EnsureCapacity_Positive_ExpandsTable() {
        var table = new SwissTable<int, int>();
        var result = table.EnsureCapacity(100);
        result.Should().BeGreaterThanOrEqualTo(100);
    }

    // === TrimExcess capacity < 0 ===

    /// <summary>验证 TrimExcess(-1) 抛 ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void TrimExcess_Negative_ThrowsArgumentOutOfRangeException(int capacity) {
        var table = new SwissTable<int, int>();
        var act = () => table.TrimExcess(capacity);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>验证 TrimExcess(0) 对空表不抛异常。</summary>
    [Fact]
    public void TrimExcess_ZeroOnEmptyTable_DoesNotThrow() {
        var table = new SwissTable<int, int>();
        var act = () => table.TrimExcess(0);
        act.Should().NotThrow();
    }

    /// <summary>验证 TrimExcess(capacity < Count) 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    public void TrimExcess_LessThanCount_ThrowsArgumentOutOfRangeException() {
        var table = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++)
            table.Add(i, i);
        var act = () => table.TrimExcess(5);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>验证 TrimExcess(Count) 不抛异常。</summary>
    [Fact]
    public void TrimExcess_EqualToCount_DoesNotThrow() {
        var table = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++)
            table.Add(i, i);
        var act = () => table.TrimExcess(10);
        act.Should().NotThrow();
    }

    /// <summary>验证无参 TrimExcess 不抛异常。</summary>
    [Fact]
    public void TrimExcess_NoArg_DoesNotThrow() {
        var table = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++)
            table.Add(i, i);
        var act = () => table.TrimExcess();
        act.Should().NotThrow();
    }
}
