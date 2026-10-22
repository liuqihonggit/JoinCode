namespace NativeTui.Host.Tests;

/// <summary>
/// Program 装配层参数提示改写测试（TASK065 F7）— /model 无参被 CommandPickerEngine 拦截走选择器模态，
/// 静态 ArgumentHint "[model-id|default|info]" 诱导用户走带参路径与拦截行为失配 →
/// Host 装配 commands 清单时改写为无参选择器诱导（与拦截声明同点维护；GUI 宿主不拦截不受影响）。
/// </summary>
public sealed class ProgramCommandHintTests
{
    /// <summary>标准命令集（model 带引擎真实 hint 形态 + 对照命令 theme）。</summary>
    private static List<CommandSuggestion> Catalog() =>
    [
        new("model", "切换或查看模型", [], IsHidden: false, ArgumentHint: "[model-id|default|info]"),
        new("theme", "切换控制台主题", [], IsHidden: false, Arguments: ["dark", "light", "auto"]),
    ];

    /// <summary>model 命令 hint 改写为无参选择器提示（消除带参诱导失配）。</summary>
    [Fact]
    public void ApplyPickerHints_RewritesModelHintToPickerIndication()
    {
        var source = Catalog();

        var result = Program.ApplyPickerHints(source);

        Assert.Equal("（无参打开选择器）", result.Single(c => c.Name == "model").ArgumentHint);
    }

    /// <summary>非拦截命令 hint 原样透传（theme 的值域补全不受影响）。</summary>
    [Fact]
    public void ApplyPickerHints_PassesThroughNonInterceptedCommands()
    {
        var result = Program.ApplyPickerHints(Catalog());

        var theme = result.Single(c => c.Name == "theme");
        Assert.Equal(["dark", "light", "auto"], theme.ArgumentValues);
    }

    /// <summary>纯投影不改写源清单（record with 不可变替换 — 装配层无副作用）。</summary>
    [Fact]
    public void ApplyPickerHints_DoesNotMutateSource()
    {
        var source = Catalog();

        _ = Program.ApplyPickerHints(source);

        Assert.Equal("[model-id|default|info]", source.Single(c => c.Name == "model").ArgumentHint);
    }
}
