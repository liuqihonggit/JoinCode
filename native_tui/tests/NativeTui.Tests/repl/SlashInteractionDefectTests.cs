namespace NativeTui.Tests.Repl;

/// <summary>
/// 斜杠命令交互缺陷测试（用户验收反馈 `/btw 你好` 两问题）—
/// ①ArgumentHint 占位符不可补全：参数模式仅有提示（如 "&lt;question&gt;"）时 Tab 不填入、
/// Enter 提交输入原文（修复：占位符被当参数值填入，`/btw &lt;question&gt;` 字面量发给 AI）；
/// ②命令输出 ANSI 剥离：ESC[38;2;...m 色码剥离（修复：ESC 零宽跳过后残骸显示 "38;2;153;153;153m──"）。
/// </summary>
public sealed class SlashInteractionDefectTests
{
    private static ReplShell CreateShell(params CommandSuggestion[] commands) {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0", "m", "b", "C:\\"), new StringWriter(), commands: commands);
        shell.SeedContent();
        return shell;
    }

    private static void Type(ReplShell shell, string text) {
        foreach (var ch in text)
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
    }

    private static void Key(ReplShell shell, ConsoleKey key, char c = '\0') =>
        shell.HandleKey(new ConsoleKeyInfo(c, key, false, false, false));

    /// <summary>占位提示命令（/btw 形态：仅 ArgumentHint 无值清单）。</summary>
    private static CommandSuggestion HintCommand() =>
        new("btw", "侧边问题", [], IsHidden: false, ArgumentHint: "<question>");

    /// <summary>真值清单命令（/effort 形态）。</summary>
    private static CommandSuggestion ValuesCommand() =>
        new("effort", "推理力度", [], IsHidden: false, ArgumentHint: "[low|medium|high]", Arguments: ["low", "medium", "high"]);

    /// <summary>占位提示 — Tab 不填入（输入保持 "/btw " 空参数）。</summary>
    [Fact]
    public void HintOnly_TabDoesNotFillPlaceholder()
    {
        var shell = CreateShell(HintCommand());
        Type(shell, "/btw ");

        Key(shell, ConsoleKey.Tab);

        Assert.Equal("/btw ", shell.Input.Text);
    }

    /// <summary>占位提示 — Enter 提交输入原文（用户实敲内容，非占位符）。</summary>
    [Fact]
    public void HintOnly_EnterSubmitsOriginalInput()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0", "m", "b", "C:\\"), new StringWriter(), commands: [HintCommand()]);
        shell.SeedContent();
        Type(shell, "/btw 你好");

        Key(shell, ConsoleKey.Enter);

        Assert.Equal(["/btw 你好"], engine.Submits);
    }

    /// <summary>真值清单 — Tab 照常补全（回归保护：/effort low 补全不受影响）。</summary>
    [Fact]
    public void RealValues_TabStillCompletes()
    {
        var shell = CreateShell(ValuesCommand());
        Type(shell, "/effort l");

        Key(shell, ConsoleKey.Tab);

        Assert.Equal("/effort low ", shell.Input.Text);
    }

    /// <summary>真值清单 — Enter 补全提交（回归保护）。</summary>
    [Fact]
    public void RealValues_EnterCompletesAndSubmits()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0", "m", "b", "C:\\"), new StringWriter(), commands: [ValuesCommand()]);
        shell.SeedContent();
        Type(shell, "/effort hi");

        Key(shell, ConsoleKey.Enter);

        Assert.Equal(["/effort high"], engine.Submits);
    }
}
