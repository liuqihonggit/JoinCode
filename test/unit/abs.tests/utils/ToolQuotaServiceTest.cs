namespace Abs.Tests.Utils;

/// <summary>
/// ToolQuotaService 单元测试 — 验证单工具频率限制/强烈重复性警告机制。
/// <para>
/// ToolQuotaService 为全局静态状态，每个测试用唯一工具名隔离，避免并行干扰。
/// 用短时长（毫秒级）+ Task.Delay 验证窗口过期。
/// </para>
/// </summary>
public sealed class ToolQuotaServiceTest {
    private static string NewToolName() => $"quota-tool-{Guid.NewGuid():N}";

    private static ToolQuotaConfig FastConfig(int threshold = 3) => new() {
        Window = TimeSpan.FromMilliseconds(80),
        Threshold = threshold
    };

    #region RecordCall 计次

    [Fact]
    public void RecordCall_UnderThreshold_ShouldWarnFalse() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 5);

        for (var i = 0; i < 4; i++)
            ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.ShouldWarn(tool, config).Should().BeFalse("未超阈值不应警告");
    }

    [Fact]
    public void RecordCall_ReachingThreshold_ShouldWarnTrue() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 3);

        for (var i = 0; i < 3; i++)
            ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.ShouldWarn(tool, config).Should().BeTrue("达到阈值应警告");
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
        ToolQuotaService.ShouldWarn(tool, config).Should().BeFalse("窗口外2次+窗口内1次=1次，未超阈值");
    }

    #endregion

    #region 持续警告（不冷却拒绝）

    [Fact]
    public void ShouldWarn_AboveThreshold_AlwaysTrue_NoCooldownRejection() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);

        ToolQuotaService.ShouldWarn(tool, config).Should().BeTrue("超阈值后持续警告，不冷却拒绝");
        ToolQuotaService.ShouldWarn(tool, config).Should().BeTrue("多次检查仍应警告");
    }

    #endregion

    #region 窗口过期后恢复

    [Fact]
    public async Task ShouldWarn_AfterWindowExpires_ReturnsFalse() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.ShouldWarn(tool, config).Should().BeTrue();

        await Task.Delay(120);

        ToolQuotaService.ShouldWarn(tool, config).Should().BeFalse("窗口过期后旧记录不计入，应恢复");
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

        ToolQuotaService.ShouldWarn(toolA, config).Should().BeTrue("toolA 超阈值应警告");
        ToolQuotaService.ShouldWarn(toolB, config).Should().BeFalse("toolB 未调用不应警告");
    }

    #endregion

    #region GetWarningPrompt 强烈重复性警告提示

    [Fact]
    public void GetWarningPrompt_ContainsToolNameAndCount() {
        var tool = NewToolName();
        var config = FastConfig() with { AlternativeToolHint = "专用工具Y" };

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);

        var prompt = ToolQuotaService.GetWarningPrompt(tool, config);

        prompt.Should().Contain(tool, "提示应包含工具名");
        prompt.Should().Contain("专用工具Y", "提示应包含替代工具建议");
        prompt.Should().Contain("3", "提示应包含当前调用次数");
        prompt.Should().Contain("强烈", "提示应为强烈重复性警告");
    }

    [Fact]
    public void GetWarningPrompt_WithoutAlternative_StillContainsToolName() {
        var tool = NewToolName();
        var config = FastConfig();

        var prompt = ToolQuotaService.GetWarningPrompt(tool, config);

        prompt.Should().Contain(tool, "无替代提示时仍应包含工具名");
    }

    #endregion

    #region GetHighFrequencyTools 高频工具集合

    [Fact]
    public void GetHighFrequencyTools_ReturnsOnlyHighFreqTools() {
        var toolA = NewToolName();
        var toolB = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(toolA, config);
        ToolQuotaService.RecordCall(toolA, config);
        ToolQuotaService.RecordCall(toolB, config);

        var highFreq = ToolQuotaService.GetHighFrequencyTools(config);
        highFreq.Should().Contain(toolA, "toolA 超阈值应在集合中");
        highFreq.Should().NotContain(toolB, "toolB 未超阈值不应在集合中");
    }

    #endregion

    #region Reset 重置

    [Fact]
    public void Reset_ClearsAllQuotas() {
        var tool = NewToolName();
        var config = FastConfig(threshold: 2);

        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.RecordCall(tool, config);
        ToolQuotaService.ShouldWarn(tool, config).Should().BeTrue();

        ToolQuotaService.Reset();

        ToolQuotaService.ShouldWarn(tool, config).Should().BeFalse("Reset 后不应警告");
    }

    #endregion

    #region 默认配置

    [Fact]
    public void RecordCall_WithDefaultConfig_WorksWithBashLikeScenario() {
        var tool = NewToolName();

        for (var i = 0; i < ToolQuotaConfig.Default.Threshold - 1; i++)
            ToolQuotaService.RecordCall(tool);

        ToolQuotaService.ShouldWarn(tool).Should().BeFalse("默认阈值-1次不应警告");
    }

    #endregion
}
