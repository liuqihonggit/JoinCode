namespace Infra.Tests.Utils.Cpu;

/// <summary>
/// CpuParallelism 确定性单元测试 — 验证 GetDegree(int maxDegree) 参数守卫与返回值范围。
/// <para>非法输入(maxDegree&lt;=0)抛 ArgumentOutOfRangeException,合法输入返回值在 [1, maxDegree] 区间。</para>
/// </summary>
public sealed class CpuParallelismTest {
    [Fact]
    [Trait("Category", "Deterministic")]
    public void GetDegree_ZeroMaxDegree_ThrowsArgumentOutOfRangeException() {
        var act = () => CpuParallelism.GetDegree(0);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxDegree*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void GetDegree_NegativeMaxDegree_ThrowsArgumentOutOfRangeException() {
        var act = () => CpuParallelism.GetDegree(-1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxDegree*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void GetDegree_ValidMaxDegree_ReturnsValueWithinRange() {
        const int maxDegree = 8;
        var result = CpuParallelism.GetDegree(maxDegree);
        result.Should().BeGreaterThan(0);
        result.Should().BeLessThanOrEqualTo(maxDegree);
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void GetDegree_SmallMaxDegree_ReturnsAtMostOne() {
        // maxDegree=1 时,无论 CPU 负载如何,结果必为 1
        var result = CpuParallelism.GetDegree(1);
        result.Should().Be(1);
    }
}
