namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息光标选中态强调测试（TASK069-M1 / TASK130-A1）— 对齐基准
/// <c>HighlightedThinkingText.tsx:24</c> 的 <c>isSelected ? suggestion : subtle</c>：
/// 选中用户消息时 ❯ 前缀提为 suggestion 前景、正文前景与灰底不变；
/// 未选中项（含助手 ● 前缀）保持原色 —— 防「顺手改错另一个前缀」。
/// </summary>
public sealed class MessageSelectionAccentTests
{
    [Fact]
    public void SelectedUserMessage_PointerUsesSuggestionColor()
    {
        var box = new ScrollBox();
        var user = new UserMessageItem("hi");
        box.Add(user);
        box.HighlightItem = user;

        using var screen = new Screen(cols: 20, rows: 4);
        box.Render(screen, new Rect(0, 0, 20, 4), Theme.Dark);

        // 用户项：margin 1 行 + 正文 1 行 → ❯ 在 row 1 / col 0。
        // 选中项背景由 ApplyHighlightBackground 统一覆写（MessageActionsBackground），
        // 故这里只断言强调负责的那一维：前景变 suggestion。
        Assert.Equal(new Rune(Figures.Pointer[0]), screen[1, 0].Value);
        Assert.Equal(Theme.Dark.Suggestion, screen.Styles.Get(screen[1, 0].StyleId).Foreground);

        // 正文前景不受强调影响（只改指示符前景）
        Assert.Equal(new Rune('h'), screen[1, 2].Value);
        Assert.Equal(Theme.Dark.Text, screen.Styles.Get(screen[1, 2].StyleId).Foreground);
    }

    [Fact]
    public void UnselectedUserMessage_PointerStaysSubtle()
    {
        var box = new ScrollBox();
        var user = new UserMessageItem("hi");
        var other = new AssistantMessageItem("answer");
        box.Add(user);
        box.Add(other);
        box.HighlightItem = other;

        using var screen = new Screen(cols: 20, rows: 6);
        box.Render(screen, new Rect(0, 0, 20, 6), Theme.Dark);

        var subtle = screen.Styles.GetOrAdd(new TextStyle
        {
            Foreground = Theme.Dark.Subtle,
            Background = Theme.Dark.UserMessageBackground,
        });
        Assert.Equal(subtle, screen[1, 0].StyleId);
    }

    [Fact]
    public void SelectedAssistantMessage_BulletKeepsTextColor()
    {
        var box = new ScrollBox();
        var assistant = new AssistantMessageItem("answer");
        box.Add(assistant);
        box.HighlightItem = assistant;

        using var screen = new Screen(cols: 20, rows: 6);
        box.Render(screen, new Rect(0, 0, 20, 6), Theme.Dark);

        // 助手项同样 margin 1 行 → ● 在 row 1 / col 0，强调不改变它（背景由高亮统一覆写，只比前景）
        Assert.Equal(new Rune(Figures.MessageBullet[0]), screen[1, 0].Value);
        Assert.Equal(Theme.Dark.Text, screen.Styles.Get(screen[1, 0].StyleId).Foreground);
    }
}
