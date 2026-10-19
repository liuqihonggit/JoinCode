namespace NativeTui.Core.Components;

/// <summary>边框风格（对齐 cli-boxes 库的子集）。</summary>
public enum BorderKind : byte
{
    /// <summary>圆角 ╭─╮│╰╯（主用，输入框/对话框）。</summary>
    Round = 0,

    /// <summary>虚线 ╌╎（基准注释：无虚线角字符，角用空格）。</summary>
    Dashed = 1,
}

/// <summary>
/// 边框绘制器 — 对齐基准 ink/render-border.ts（cli-boxes）+ PromptInput.tsx 的标志性单顶边。
/// </summary>
public static class BorderPainter
{
    /// <summary>只画顶边（输入框标志性样式）：╭ + ─×(w-2) + ╮。</summary>
    public static void DrawTopOnly(Screen screen, Rect rect, int styleId)
    {
        if (rect.Width < 2)
        {
            return;
        }

        screen[rect.Y, rect.X] = new Cell(new Rune('╭'), styleId, CellWidth.Default);
        screen[rect.Y, rect.Right - 1] = new Cell(new Rune('╮'), styleId, CellWidth.Default);
        for (var col = rect.X + 1; col < rect.Right - 1; col++)
        {
            screen[rect.Y, col] = new Cell(new Rune('─'), styleId, CellWidth.Default);
        }
    }

    /// <summary>画完整圆角边框（宽高 ≥ 2 才绘制）。</summary>
    public static void DrawFull(Screen screen, Rect rect, int styleId)
    {
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        DrawTopOnly(screen, rect, styleId);
        screen[rect.Bottom - 1, rect.X] = new Cell(new Rune('╰'), styleId, CellWidth.Default);
        screen[rect.Bottom - 1, rect.Right - 1] = new Cell(new Rune('╯'), styleId, CellWidth.Default);
        for (var col = rect.X + 1; col < rect.Right - 1; col++)
        {
            screen[rect.Bottom - 1, col] = new Cell(new Rune('─'), styleId, CellWidth.Default);
        }

        for (var row = rect.Y + 1; row < rect.Bottom - 1; row++)
        {
            screen[row, rect.X] = new Cell(new Rune('│'), styleId, CellWidth.Default);
            screen[row, rect.Right - 1] = new Cell(new Rune('│'), styleId, CellWidth.Default);
        }
    }

    /// <summary>画虚线顶边（╌，角为空格）。</summary>
    public static void DrawDashedTop(Screen screen, Rect rect, int styleId)
    {
        for (var col = rect.X; col < rect.Right; col++)
        {
            var glyph = col == rect.X || col == rect.Right - 1 ? ' ' : '╌';
            screen[rect.Y, col] = new Cell(new Rune(glyph), styleId, CellWidth.Default);
        }
    }
}
