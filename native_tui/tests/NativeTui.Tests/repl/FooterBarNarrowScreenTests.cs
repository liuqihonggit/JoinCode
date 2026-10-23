namespace NativeTui.Tests.Repl;

/// <summary>
/// footer 窄屏降级测试（TASK069 N3 — 对齐基准 Notifications.tsx:175 columns&lt;80 上下堆叠 /
/// PromptInputFooter.tsx:105,139 flex-start）：窄屏（&lt;80 列）时右侧内容（LoginError）不再与
/// 左侧模式串同行右对齐互挤覆盖 — 独立行堆叠，左侧模式串完整保留。
/// </summary>
public sealed class FooterBarNarrowScreenTests
{
    /// <summary>窄屏 + LoginError → 独立行（模式串尾不被覆盖）。</summary>
    [Fact]
    public void NarrowScreen_LoginError_OwnRow_PreservesModeText()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var state = new FooterState
        {
            Mode = PermissionMode.Plan,
            LoginError = "未登录 · 运行 /login 输入密钥完成认证",
        };

        new FooterBar().Render(screen, new Rect(0, 0, 60, 3), Theme.Dark, state);

        // 行 0 模式串完整（plan 前缀符号 + 标题）—— 尾部"已开启"未被 LoginError 覆盖
        var row0 = ExtractRowText(screen, 0, 60);
        Assert.Contains("已开启", row0);
        // LoginError 在独立行（行 1）可见
        var row1 = ExtractRowText(screen, 1, 60);
        Assert.Contains("未登录", row1);
    }

    /// <summary>宽屏（≥80）既有行为回归守卫 — LoginError 仍行 1 右对齐（两行总高不变）。</summary>
    [Fact]
    public void WideScreen_LoginError_Row1RightAligned()
    {
        using var screen = new Screen(cols: 100, rows: 4);
        var state = new FooterState
        {
            Mode = PermissionMode.Plan,
            LoginError = "未登录 · 运行 /login",
        };

        new FooterBar().Render(screen, new Rect(0, 0, 100, 2), Theme.Dark, state);

        var row0 = ExtractRowText(screen, 0, 100);
        Assert.Contains("已开启", row0);
        Assert.Contains("未登录", row0);
    }

    /// <summary>窄屏高度计算 — LoginError 独立行使高度增加（窄屏 2 行 vs 宽屏 1 行）。</summary>
    [Fact]
    public void NarrowScreen_LoginError_IncreasesHeight()
    {
        var state = new FooterState
        {
            Mode = PermissionMode.Plan,
            LoginError = "未登录 · 运行 /login 输入密钥完成认证",
        };
        var footer = new FooterBar();

        // 宽屏语义守卫：LoginError 存在 + 无行 2 内容 → 1 行（右对齐不增行）
        Assert.Equal(1, footer.MeasureHeight(state));
        // 窄屏独立行高度经 MeasureHeight(narrow) 重载暴露
        Assert.Equal(2, footer.MeasureHeight(state, cols: 60));
    }

    private static string ExtractRowText(Screen screen, int row, int cols)
    {
        var builder = new System.Text.StringBuilder();
        for (var col = 0; col < cols; col++)
        {
            var cell = screen[row, col];
            if (cell.Width == CellWidth.SpacerTail)
            {
                continue;
            }

            builder.Append(cell.Value.Value == ' ' ? ' ' : cell.Value.ToString());
        }

        return builder.ToString().TrimEnd();
    }
}
