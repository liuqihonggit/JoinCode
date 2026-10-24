namespace NativeTui.Tests.Repl;

/// <summary>
/// statusline 模板渲染器测试 — 激活 settings 死键 statusline.format（StatuslineCommand 写入）：
/// 变量替换 {model}/{tokens}/{mode}/{time}/{cost}（未知变量原样保留；无变量模板原样返回）。
/// </summary>
public sealed class StatusLineTemplateRendererTests
{
    [Fact]
    public void Render_AllVariables_Replaced()
    {
        var result = StatusLineTemplateRenderer.Render(
            "{model} · {tokens} · {mode} · {time}",
            model: "glm-4.7",
            tokens: "12k/200k",
            mode: "auto",
            time: "14:30");

        Assert.Equal("glm-4.7 · 12k/200k · auto · 14:30", result);
    }

    [Fact]
    public void Render_UnknownVariable_KeptAsIs()
    {
        var result = StatusLineTemplateRenderer.Render("{model} {unknown}", model: "m1", tokens: null, mode: null, time: null);

        Assert.Equal("m1 {unknown}", result);
    }

    [Fact]
    public void Render_NullVariable_EmptyString()
    {
        var result = StatusLineTemplateRenderer.Render("[{tokens}]", model: null, tokens: null, mode: null, time: null);

        Assert.Equal("[]", result);
    }

    [Fact]
    public void Render_NoVariables_ReturnedAsIs()
    {
        var result = StatusLineTemplateRenderer.Render("纯文本状态行", model: null, tokens: null, mode: null, time: null);

        Assert.Equal("纯文本状态行", result);
    }

    [Fact]
    public void Render_CostVariable_ReplacedWhenProvided()
    {
        var result = StatusLineTemplateRenderer.Render("${cost}", model: null, tokens: null, mode: null, time: null, cost: "0.42");

        Assert.Equal("$0.42", result);
    }
}
