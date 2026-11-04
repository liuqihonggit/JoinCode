namespace Abs.Tests.Context.Hierarchy;

/// <summary>
/// ContextHierarchyOptionsBuilder 确定性测试 — 取值范围守卫。
/// 覆盖 WithCompressionRatio [0,1] + WithMaxLayers >0。
/// 不依赖时序/IO,给定越界输入 → 断言抛 ArgumentOutOfRangeException。
/// </summary>
public sealed class ContextHierarchyOptionsTest {
    // === WithCompressionRatio: 取值范围 [0,1] 守卫 ===

    /// <summary>ratio &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithCompressionRatio_BelowZero_ThrowsArgumentOutOfRangeException() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithCompressionRatio(-0.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("ratio");
    }

    /// <summary>ratio &gt; 1 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithCompressionRatio_AboveOne_ThrowsArgumentOutOfRangeException() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithCompressionRatio(1.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("ratio");
    }

    /// <summary>边界值 ratio=0.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithCompressionRatio_ZeroBoundary_DoesNotThrow() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithCompressionRatio(0.0);

        act.Should().NotThrow();
    }

    /// <summary>边界值 ratio=1.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithCompressionRatio_OneBoundary_DoesNotThrow() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithCompressionRatio(1.0);

        act.Should().NotThrow();
    }

    /// <summary>典型值 ratio=0.5 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithCompressionRatio_TypicalValue_DoesNotThrow() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithCompressionRatio(0.5);

        act.Should().NotThrow();
    }

    // === WithMaxLayers: >0 守卫 ===

    /// <summary>layers=0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxLayers_Zero_ThrowsArgumentOutOfRangeException() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithMaxLayers(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("layers");
    }

    /// <summary>layers &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxLayers_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithMaxLayers(-1);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("layers");
    }

    /// <summary>边界值 layers=1 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxLayers_OneBoundary_DoesNotThrow() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithMaxLayers(1);

        act.Should().NotThrow();
    }

    /// <summary>典型值 layers=3 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxLayers_TypicalValue_DoesNotThrow() {
        var act = () => ContextHierarchyOptionsBuilder.Create().WithMaxLayers(3);

        act.Should().NotThrow();
    }

    // === 链式调用: 守卫在链中正确触发 ===

    /// <summary>链式调用中越界 ratio 抛异常 — 守卫不因链式而失效。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Chained_WithCompressionRatio_OutOfRange_Throws() {
        var act = () => ContextHierarchyOptionsBuilder.Create()
            .WithMaxLayers(3)
            .WithCompressionRatio(1.5)
            .Build();

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("ratio");
    }

    /// <summary>链式调用中越界 layers 抛异常 — 守卫不因链式而失效。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Chained_WithMaxLayers_OutOfRange_Throws() {
        var act = () => ContextHierarchyOptionsBuilder.Create()
            .WithMaxLayers(0)
            .WithCompressionRatio(0.5)
            .Build();

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("layers");
    }

    /// <summary>合法链式调用应成功构建。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Chained_AllValid_BuildsSuccessfully() {
        var act = () => ContextHierarchyOptionsBuilder.Create()
            .WithMaxLayers(3)
            .WithCompressionRatio(0.5)
            .Build();

        act.Should().NotThrow();
    }
}
