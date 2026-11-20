namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具行 ● 状态口径测试（TASK069-M2 — 对齐基准 ToolUseLoader）：
/// 未决 = 默认前景 + dim + 600ms 方波闪烁（基准 `t1 = isError || !isUnresolved || isBlinking ? ● : " "`）；
/// 完成 = success 恒亮；错误 = error 恒亮且恒显。
/// 单一实现源 MessageRenderer.BulletChar/BulletStyle（工具行 / 聚合摘要行 / 子代理组头共用）。
/// </summary>
public sealed class ToolCallBulletStyleTests
{
    private static ToolCallItem Item(ToolCallStatus status = ToolCallStatus.Pending, long animTimeMs = 0) =>
        new("Bash", "ls -la") { Status = status, AnimTimeMs = animTimeMs };

    [Fact]
    public void Pending_FirstPhase_ShowsDimBullet()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item().Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("●", RowText.Of(screen, 1, 0, 1));
        Assert.Equal(dim, screen[1, 0].StyleId);
    }

    [Fact]
    public void Pending_BlinkOffPhase_HidesBulletWithoutShiftingLayout()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item(animTimeMs: 600).Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        Assert.Equal(" ", RowText.Of(screen, 1, 0, 1));
        // 布局不跳动 — 工具名恒从内容列（X+2）起
        Assert.Equal("Bash", RowText.Of(screen, 1, 2, 4));
    }

    [Fact]
    public void Pending_BlinkOnAgainAfterSecondInterval()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item(animTimeMs: 1200).Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        Assert.Equal("●", RowText.Of(screen, 1, 0, 1));
    }

    [Fact]
    public void Success_UsesSuccessColorAndStaysLit()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item(ToolCallStatus.Success, animTimeMs: 600).Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        Assert.Equal("●", RowText.Of(screen, 1, 0, 1));
        Assert.Equal(success, screen[1, 0].StyleId);
    }

    [Fact]
    public void Error_UsesErrorColorAndStaysLit()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item(ToolCallStatus.Error, animTimeMs: 600).Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal("●", RowText.Of(screen, 1, 0, 1));
        Assert.Equal(error, screen[1, 0].StyleId);
    }

    [Fact]
    public void ToolNameStaysBoldRegardlessOfStatus()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        Item(ToolCallStatus.Error).Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(bold, screen[1, 2].StyleId);
    }
}
