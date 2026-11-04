namespace Abs.Tests.Context.Resolution;

/// <summary>
/// ReferenceResolutionOptionsBuilder 确定性测试 — 取值范围守卫。
/// 覆盖 WithMinRelevanceScore/WithFuzzyMatchThreshold [0,1] + WithMaxResults >0。
/// 不依赖时序/IO,给定越界输入 → 断言抛 ArgumentOutOfRangeException。
/// </summary>
public sealed class ReferenceResolutionOptionsTest {
    // === WithMinRelevanceScore: 取值范围 [0,1] 守卫 ===

    /// <summary>score &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMinRelevanceScore_BelowZero_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMinRelevanceScore(-0.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("score");
    }

    /// <summary>score &gt; 1 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMinRelevanceScore_AboveOne_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMinRelevanceScore(1.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("score");
    }

    /// <summary>边界值 score=0.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMinRelevanceScore_ZeroBoundary_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMinRelevanceScore(0.0);

        act.Should().NotThrow();
    }

    /// <summary>边界值 score=1.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMinRelevanceScore_OneBoundary_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMinRelevanceScore(1.0);

        act.Should().NotThrow();
    }

    /// <summary>典型值 score=0.3 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMinRelevanceScore_TypicalValue_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMinRelevanceScore(0.3);

        act.Should().NotThrow();
    }

    // === WithFuzzyMatchThreshold: 取值范围 [0,1] 守卫 ===

    /// <summary>threshold &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithFuzzyMatchThreshold_BelowZero_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithFuzzyMatchThreshold(-0.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("threshold");
    }

    /// <summary>threshold &gt; 1 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithFuzzyMatchThreshold_AboveOne_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithFuzzyMatchThreshold(1.01);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("threshold");
    }

    /// <summary>边界值 threshold=0.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithFuzzyMatchThreshold_ZeroBoundary_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithFuzzyMatchThreshold(0.0);

        act.Should().NotThrow();
    }

    /// <summary>边界值 threshold=1.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithFuzzyMatchThreshold_OneBoundary_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithFuzzyMatchThreshold(1.0);

        act.Should().NotThrow();
    }

    // === WithMaxResults: >0 守卫 ===

    /// <summary>maxResults=0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxResults_Zero_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMaxResults(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("maxResults");
    }

    /// <summary>maxResults &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxResults_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMaxResults(-1);

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("maxResults");
    }

    /// <summary>边界值 maxResults=1 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxResults_OneBoundary_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMaxResults(1);

        act.Should().NotThrow();
    }

    /// <summary>典型值 maxResults=50 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void WithMaxResults_TypicalValue_DoesNotThrow() {
        var act = () => ReferenceResolutionOptionsBuilder.Create().WithMaxResults(50);

        act.Should().NotThrow();
    }

    // === 链式调用: 守卫在链中正确触发 ===

    /// <summary>链式调用中越界 score 抛异常 — 守卫不因链式而失效。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Chained_WithMinRelevanceScore_OutOfRange_Throws() {
        var act = () => ReferenceResolutionOptionsBuilder.Create()
            .WithMaxResults(10)
            .WithMinRelevanceScore(1.5)
            .WithFuzzyMatchThreshold(0.5)
            .Build();

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("score");
    }

    /// <summary>合法链式调用应成功构建。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Chained_AllValid_BuildsSuccessfully() {
        var act = () => ReferenceResolutionOptionsBuilder.Create()
            .WithMaxResults(10)
            .WithMinRelevanceScore(0.5)
            .WithFuzzyMatchThreshold(0.6)
            .Build();

        act.Should().NotThrow();
    }
}
