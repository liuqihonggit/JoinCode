namespace Core.Tests.Security;

/// <summary>
/// ClassificationResult 确定性测试 — Confidence 取值范围 [0,1] 守卫。
/// 不依赖时序/IO,给定越界输入 → 断言抛 ArgumentOutOfRangeException。
/// </summary>
public sealed class ClassificationResultTest {
    // === Confidence: 取值范围 [0,1] 守卫 ===

    /// <summary>Confidence &lt; 0 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_BelowZero_ThrowsArgumentOutOfRangeException() {
        var act = () => new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = -0.01,
            Action = SecurityAction.AutoApprove
        };

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("Confidence");
    }

    /// <summary>Confidence &gt; 1 抛 ArgumentOutOfRangeException。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_AboveOne_ThrowsArgumentOutOfRangeException() {
        var act = () => new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = 1.01,
            Action = SecurityAction.AutoApprove
        };

        act.Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("Confidence");
    }

    /// <summary>边界值 Confidence=0.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_ZeroBoundary_DoesNotThrow() {
        var act = () => new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = 0.0,
            Action = SecurityAction.AutoApprove
        };

        act.Should().NotThrow();
    }

    /// <summary>边界值 Confidence=1.0 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_OneBoundary_DoesNotThrow() {
        var act = () => new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = 1.0,
            Action = SecurityAction.AutoApprove
        };

        act.Should().NotThrow();
    }

    /// <summary>典型值 Confidence=0.5 合法 — 不抛异常。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_TypicalValue_DoesNotThrow() {
        var act = () => new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Action = SecurityAction.RequireConfirmation
        };

        act.Should().NotThrow();
    }

    /// <summary>合法构造应保留 Confidence 值。</summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Confidence_ValidValue_Preserved() {
        var result = new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = 0.95,
            Action = SecurityAction.AutoApprove
        };

        result.Confidence.Should().Be(0.95);
    }
}
