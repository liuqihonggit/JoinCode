namespace NativeTui.Core.Components;

/// <summary>
/// 静态文本块 — 单样式文本叶子组件（多段样式后续由 RichText 扩展）。
/// </summary>
public sealed class TextBlock
{
    private readonly string _text;

    /// <summary>创建文本块。</summary>
    public TextBlock(string text)
    {
        _text = text;
    }

    /// <summary>渲染到指定帧区域（按显示宽度截断到 frame 边界）。</summary>
    public void Render(Screen screen, Rect frame, int? renderStyle)
    {
        var visibleEnd = 0;
        var width = 0;
        foreach (var rune in _text.AsSpan().EnumerateRunes())
        {
            var runeWidth = RuneWidth.Of(rune);
            if (width + runeWidth > frame.Width)
            {
                break;
            }

            width += runeWidth;
            visibleEnd += rune.Utf16SequenceLength;
        }

        screen.WriteText(frame.Y, frame.X, _text.AsSpan(0, visibleEnd), renderStyle ?? 0);
    }

    /// <summary>按宽度测量的内容高度（当前单行不换行，恒 1）。</summary>
    public int MeasureHeight(int width) => 1;
}
