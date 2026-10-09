namespace Abs.Tests.Utils;

/// <summary>
/// LowFrequencyInductionService 单元测试 — 验证低频驱动诱导机制。
/// <para>全局静态状态，用 Reset 隔离测试。用短时长验证间隔控制。</para>
/// </summary>
public sealed class LowFrequencyInductionServiceTest {
    private static string NewKey() => $"lf-induction-test-{Guid.NewGuid():N}";

    #region ShouldInduce 间隔控制

    [Fact]
    public void ShouldInduce_FirstCall_ReturnsTrue() {
        var key = NewKey();
        LowFrequencyInductionService.ShouldInduce(key, TimeSpan.FromMinutes(10)).Should().BeTrue("首次应诱导");
    }

    [Fact]
    public void ShouldInduce_WithinInterval_ReturnsFalse() {
        var key = NewKey();
        var interval = TimeSpan.FromMilliseconds(100);
        LowFrequencyInductionService.RecordInduction(key);

        LowFrequencyInductionService.ShouldInduce(key, interval).Should().BeFalse("间隔内不应诱导（避免高频注入）");
    }

    [Fact]
    public async Task ShouldInduce_AfterInterval_ReturnsTrue() {
        var key = NewKey();
        var interval = TimeSpan.FromMilliseconds(50);
        LowFrequencyInductionService.RecordInduction(key);
        await Task.Delay(70);

        LowFrequencyInductionService.ShouldInduce(key, interval).Should().BeTrue("间隔过后应可再次诱导");
    }

    #endregion

    #region 不同 key 独立

    [Fact]
    public void ShouldInduce_DifferentKeys_AreIndependent() {
        var keyA = NewKey();
        var keyB = NewKey();
        LowFrequencyInductionService.RecordInduction(keyA);

        LowFrequencyInductionService.ShouldInduce(keyB, TimeSpan.FromMinutes(10)).Should().BeTrue("不同 key 互不影响");
        LowFrequencyInductionService.ShouldInduce(keyA, TimeSpan.FromMinutes(10)).Should().BeFalse("keyA 间隔内不诱导");
    }

    #endregion

    #region GetInductionPrompt 提示

    [Fact]
    public void GetInductionPrompt_ContainsEventKey() {
        var key = NewKey();
        var prompt = LowFrequencyInductionService.GetInductionPrompt(key, "工具健康度下降");
        prompt.Should().Contain(key, "提示应包含事件名");
        prompt.Should().Contain("工具健康度下降", "提示应包含健康详情");
        prompt.Should().Contain("低频诱导", "提示应标明低频诱导");
    }

    [Fact]
    public void GetInductionPrompt_WithoutDetail_StillContainsKey() {
        var key = NewKey();
        var prompt = LowFrequencyInductionService.GetInductionPrompt(key);
        prompt.Should().Contain(key);
    }

    #endregion

    #region Reset 重置

    [Fact]
    public void Reset_ClearsAllState() {
        var key = NewKey();
        LowFrequencyInductionService.RecordInduction(key);
        LowFrequencyInductionService.ShouldInduce(key, TimeSpan.FromMinutes(10)).Should().BeFalse();

        LowFrequencyInductionService.Reset();

        LowFrequencyInductionService.ShouldInduce(key, TimeSpan.FromMinutes(10)).Should().BeTrue("Reset 后应可诱导");
    }

    #endregion
}
