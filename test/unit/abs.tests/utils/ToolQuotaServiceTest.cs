namespace Abs.Tests.Utils;

/// <summary>
/// ToolQuotaService 单元测试 — 验证单工具频率限制/没收冷却期机制。
/// <para>
/// ToolQuotaService 为全局静态状态，每个测试用唯一工具名隔离，避免并行干扰。
/// 用短时长（毫秒级）+ Task.Delay 验证窗口过期和冷却期过期。
/// </para>
/// </summary>
public sealed class ToolQuotaServiceTest {
    private static string NewToolName() => $"quota-tool-{Guid.NewGuid():N}";

    private static ToolQuotaConfig FastConfig(int threshold = 3) => new() {
        Window = TimeSpan.FromMilliseconds(80),
        Threshold = threshold,
        Cooldown = TimeSpan.FromMilliseconds(80)
    };

    #region RecordCall 计次

    [Fact]
    public void RecordCall_UnderThreshold_NotCoolingDown() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 5);

        for (var i = 0; i < 4; i++)
            ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.IsCoolingDown(tool, config).Should().BeFalse("未超阈值不应冷却");
    }

    [Fact]
    public void RecordCall_ReachingThreshold_EntersCooldown() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 3);

        for (var i = 0; i < 3; i++)
            ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.IsCoolingDown(tool, config).Should().BeTrue("达到阈值应进入冷却期");
    }

    #endregion

    #region 窗口外旧记录不计入

    [Fact]
    public async Task RecordCall_OldCallsOutsideWindow_NotCounted() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 3);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        await Task.Delay(100);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.IsCoolingDown(tool, config).Should().BeFalse("窗口外2次+窗口内1次=1次，未超阈值");
    }

    #endregion

    #region 冷却期内拒绝

    [Fact]
    public void IsCoolingDown_WithinCooldown_ReturnsTrue() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.IsCoolingDown(tool, config).Should().BeTrue("冷却期内应拒绝");
        ToolQuotaService.IsCoolingDown(tool, config).Should().BeTrue("多次检查仍应拒绝");
    }

    #endregion

    #region 冷却期过后恢复

    [Fact]
    public async Task IsCoolingDown_AfterCooldownExpires_ReturnsFalse() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.IsCoolingDown(tool, config).Should().BeTrue();

        await Task.Delay(120);

        ToolQuotaService.IsCoolingDown(tool, config).Should().BeFalse("冷却过期后应恢复");
    }

    #endregion

    #region 不同工具名独立

    [Fact]
    public void RecordCall_DifferentTools_AreIndependent() {
        var toolA = NewToolName();
        var toolB = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(toolA, config);
        ToolQuotaService.RecordCall(toolA, config);

        ToolQuotaService.IsCoolingDown(toolA, config).Should().BeTrue("toolA 超阈值应冷却");
        ToolQuotaService.IsCoolingDown(toolB, config).Should().BeFalse("toolB 未调用不应冷却");
    }

    #endregion

    #region GetCooldownPrompt 冷却提示

    [Fact]
    public void GetCooldownPrompt_ContainsToolName() {
        var tool = NewToolName();
        var config = FastConfig() with { AlternativeToolHint = "专用工具Y" };

        var prompt = ToolQuotaService.GetCooldownPrompt(tool, config);

        prompt.Should().Contain(tool, "提示应包含被冷却的工具名");
        prompt.Should().Contain("专用工具Y", "提示应包含替代工具建议");
    }

    [Fact]
    public void GetCooldownPrompt_WithoutAlternative_StillContainsToolName() {
        var tool = NewToolName();
        var config = FastConfig();

        var prompt = ToolQuotaService.GetCooldownPrompt(tool, config);

        prompt.Should().Contain(tool, "无替代提示时仍应包含工具名");
    }

    #endregion

    #region Reset 重置

    [Fact]
    public void Reset_ClearsAllQuotas() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.IsCoolingDown(tool, config).Should().BeTrue();

        ToolQuotaService.Reset();

        ToolQuotaService.IsCoolingDown(tool, config).Should().BeFalse("Reset 后不应冷却");
    }

    #endregion

    #region 默认配置

    [Fact]
    public void RecordCall_WithDefaultConfig_WorksWithBashLikeScenario() {
        var tool = NewToolName();

        for (var i = 0; i < ToolQuotaConfig.Default.Threshold - 1; i++)
            ToolQuotaService.RecordCall(tool);

        ToolQuotaService.IsCoolingDown(tool).Should().BeFalse("默认阈值-1次不应冷却");
    }

    #endregion
}
