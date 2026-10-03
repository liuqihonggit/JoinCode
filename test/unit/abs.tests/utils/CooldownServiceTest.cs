namespace Abs.Tests.Utils;

/// <summary>
/// CooldownService 单元测试 — 验证首次触发、冷却内抑制、冷却后恢复、自定义时长、key 隔离。
/// <para>
/// CooldownService 为全局静态状态（ConcurrentDictionary），每个测试用唯一 key 隔离，
/// 避免并行测试间干扰。冷却过期用短时长（毫秒级）+ Task.Delay 验证。
/// </para>
/// </summary>
public sealed class CooldownServiceTest {
    private static string NewKey() => $"cooldown-test-{Guid.NewGuid():N}";

    #region ShouldTrigger 首次触发

    [Fact]
    public void ShouldTrigger_FirstCall_ReturnsTrue() {
        var key = NewKey();

        CooldownService.ShouldTrigger(key).Should().BeTrue("首次触发应返回 true");
    }

    #endregion

    #region 冷却内抑制

    [Fact]
    public void ShouldTrigger_AfterRecord_WithinDefaultCooldown_ReturnsFalse() {
        var key = NewKey();
        CooldownService.RecordTrigger(key);

        CooldownService.ShouldTrigger(key).Should().BeFalse("默认冷却（5分钟）内应返回 false");
    }

    [Fact]
    public void ShouldTrigger_AfterRecord_WithinCustomCooldown_ReturnsFalse() {
        var key = NewKey();
        var cooldown = TimeSpan.FromMilliseconds(500);
        CooldownService.RecordTrigger(key);

        CooldownService.ShouldTrigger(key, cooldown).Should().BeFalse("自定义冷却期内应返回 false");
    }

    #endregion

    #region 冷却过期后恢复

    [Fact]
    public async Task ShouldTrigger_AfterCooldownExpires_ReturnsTrue() {
        var key = NewKey();
        var cooldown = TimeSpan.FromMilliseconds(50);
        CooldownService.RecordTrigger(key);
        await Task.Delay(80);

        CooldownService.ShouldTrigger(key, cooldown).Should().BeTrue("冷却过期后应返回 true");
    }

    [Fact]
    public async Task ShouldTrigger_CustomShortCooldown_Elapses_ReturnsTrue() {
        var key = NewKey();
        var cooldown = TimeSpan.FromMilliseconds(30);
        CooldownService.RecordTrigger(key);
        await Task.Delay(60);

        CooldownService.ShouldTrigger(key, cooldown).Should().BeTrue("自定义短冷却过期后应返回 true");
    }

    #endregion

    #region 不同 key 互不影响

    [Fact]
    public void ShouldTrigger_DifferentKeys_AreIndependent() {
        var keyA = NewKey();
        var keyB = NewKey();
        CooldownService.RecordTrigger(keyA);

        CooldownService.ShouldTrigger(keyB).Should().BeTrue("不同 key 互不影响，keyB 首次应 true");
        CooldownService.ShouldTrigger(keyA).Should().BeFalse("keyA 冷却期内应 false");
    }

    #endregion

    #region RecordTrigger 更新触发时间

    [Fact]
    public async Task RecordTrigger_UpdatesTriggerTime_AllowsReCooldown() {
        var key = NewKey();
        var cooldown = TimeSpan.FromMilliseconds(40);

        CooldownService.RecordTrigger(key);
        await Task.Delay(60);
        // 第一次冷却已过期，应可再次触发
        CooldownService.ShouldTrigger(key, cooldown).Should().BeTrue("冷却过期后应可再次触发");

        // 重新记录后再次进入冷却
        CooldownService.RecordTrigger(key);
        CooldownService.ShouldTrigger(key, cooldown).Should().BeFalse("重新记录后应再次进入冷却");
    }

    #endregion
}
