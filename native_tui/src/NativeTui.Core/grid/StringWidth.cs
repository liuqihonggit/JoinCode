namespace NativeTui.Core.Grid;

/// <summary>字符串显示宽度（rune 宽求和，零宽 rune 不占列）— 对齐基准 ink/stringWidth 的 ASCII/常用路径。</summary>
public static class StringWidth
{
    /// <summary>求字符串占用的终端列数。</summary>
    public static int Of(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneWidth.Of(rune);
        }

        return width;
    }
}
