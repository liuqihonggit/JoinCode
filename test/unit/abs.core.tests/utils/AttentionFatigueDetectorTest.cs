namespace Abs.Tests.Utils;

/// <summary>
/// AttentionFatigueDetector 单元测试 — 验证注意力涣散检测机制。
/// <para>全局静态状态，用 Reset 隔离测试。用短时长配置验证。</para>
/// </summary>
public sealed class AttentionFatigueDetectorTest {
    private static AttentionFatigueConfig FastConfig() => new() {
        CompactionThreshold = 2,
        DurationThreshold = TimeSpan.FromMilliseconds(50),
        ErrorRateThreshold = 0.3
    };

    #region RecordCompaction 计数

    [Fact]
    public void RecordCompaction_IncrementsCount() {
        AttentionFatigueDetector.Reset();
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();

        AttentionFatigueDetector.CompactionCount.Should().Be(2);
    }

    #endregion

    #region IsFatigued 未达阈值

    [Fact]
    public async Task IsFatigued_BelowCompactionThreshold_ReturnsFalse() {
        AttentionFatigueDetector.Reset();
        var config = FastConfig();

        AttentionFatigueDetector.RecordCompaction();
        await Task.Delay(60);
        AttentionFatigueDetector.RecordErrorDecision();
        AttentionFatigueDetector.RecordDecision();

        AttentionFatigueDetector.IsFatigued(config).Should().BeFalse("压缩次数未达阈值");
    }

    [Fact]
    public async Task IsFatigued_BelowDurationThreshold_ReturnsFalse() {
        AttentionFatigueDetector.Reset();
        var config = FastConfig() with { DurationThreshold = TimeSpan.FromHours(1) };

        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordErrorDecision();

        AttentionFatigueDetector.IsFatigued(config).Should().BeFalse("运行时长未达阈值");
    }

    [Fact]
    public async Task IsFatigued_BelowErrorRateThreshold_ReturnsFalse() {
        AttentionFatigueDetector.Reset();
        var config = FastConfig();

        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        await Task.Delay(60);
        for (var i = 0; i < 10; i++)
            AttentionFatigueDetector.RecordDecision();
        AttentionFatigueDetector.RecordErrorDecision();

        AttentionFatigueDetector.IsFatigued(config).Should().BeFalse("错误率未达阈值");
    }

    #endregion

    #region IsFatigued 达阈值

    [Fact]
    public async Task IsFatigued_AllThresholdsMet_ReturnsTrue() {
        AttentionFatigueDetector.Reset();
        var config = FastConfig();

        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        await Task.Delay(60);
        AttentionFatigueDetector.RecordErrorDecision();
        AttentionFatigueDetector.RecordErrorDecision();
        AttentionFatigueDetector.RecordDecision();

        AttentionFatigueDetector.IsFatigued(config).Should().BeTrue("压缩>阈值 且 时长>阈值 且 错误率>阈值");
    }

    #endregion

    #region GetFatiguePrompt 提示

    [Fact]
    public void GetFatiguePrompt_ContainsClearAndNewSessionHint() {
        var prompt = AttentionFatigueDetector.GetFatiguePrompt();

        prompt.Should().Contain("/clear", "提示应建议用 /clear 重置上下文");
        prompt.Should().Contain("新会话", "提示应建议开启新会话");
    }

    #endregion

    #region Reset 重置

    [Fact]
    public void Reset_ClearsAllState() {
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordCompaction();
        AttentionFatigueDetector.RecordErrorDecision();

        AttentionFatigueDetector.Reset();

        AttentionFatigueDetector.CompactionCount.Should().Be(0);
        AttentionFatigueDetector.ErrorDecisionCount.Should().Be(0);
    }

    #endregion
}
