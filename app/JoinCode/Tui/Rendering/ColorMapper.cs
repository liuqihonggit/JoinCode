namespace JoinCode.Tui.Rendering;

/// <summary>
/// 颜色映射器 — 将 MessageStyle 的 RGB 字符串映射到 Terminal.Gui Drawing.Color/Attribute。
/// 所有颜色解析缓存到 FrozenDictionary，避免重复解析。
/// </summary>
public static class ColorMapper
{
    private static readonly FrozenDictionary<string, GuiColor> _colorCache = ParseKnownColors();

    /// <summary>将 MessageStyle 映射到 Terminal.Gui Attribute。</summary>
    public static GuiAttribute ToAttribute(MessageStyle? style)
    {
        if (style is null) return GuiAttribute.Default;

        var foreground = ParseColor(style.Foreground);
        var background = ParseColor(style.Background);
        var textStyle = ToTextStyle(style);
        return new GuiAttribute(foreground, background, textStyle);
    }

    /// <summary>将 RGB 字符串（如 "#58a6ff"）解析为 Terminal.Gui Color。</summary>
    public static GuiColor ParseColor(string? rgb)
    {
        if (string.IsNullOrEmpty(rgb)) return GuiColor.None;

        if (_colorCache.TryGetValue(rgb!, out var cached)) return cached;

        var color = ParseRgb(rgb!);
        return color;
    }

    /// <summary>将 MessageStyle 的 Italic/Bold/Dim 映射到 TextStyle。</summary>
    public static GuiTextStyle ToTextStyle(MessageStyle style)
    {
        var ts = GuiTextStyle.None;
        if (style.Bold) ts |= GuiTextStyle.Bold;
        if (style.Italic) ts |= GuiTextStyle.Italic;
        if (style.Dim) ts |= GuiTextStyle.Faint;
        return ts;
    }

    private static GuiColor ParseRgb(string rgb)
    {
        if (rgb.StartsWith('#') && rgb.Length >= 7)
        {
            var r = Convert.ToInt32(rgb[1..3], 16);
            var g = Convert.ToInt32(rgb[3..5], 16);
            var b = Convert.ToInt32(rgb[5..7], 16);
            var a = rgb.Length >= 9 ? Convert.ToInt32(rgb[7..9], 16) : 255;
            return new GuiColor(r, g, b, a);
        }
        return new GuiColor(rgb);
    }

    private static FrozenDictionary<string, GuiColor> ParseKnownColors()
    {
        var styles = new[]
        {
            MessageStyle.User, MessageStyle.Thinking, MessageStyle.Content,
            MessageStyle.ToolCall, MessageStyle.ToolResult, MessageStyle.SubAgentCard,
            MessageStyle.Warning, MessageStyle.Error, MessageStyle.Separator,
            MessageStyle.Pending,
        };

        var dict = new Dictionary<string, GuiColor>(StringComparer.Ordinal);
        foreach (var s in styles)
        {
            if (s.Foreground is not null && !dict.ContainsKey(s.Foreground))
                dict[s.Foreground] = ParseRgb(s.Foreground);
            if (s.Background is not null && !dict.ContainsKey(s.Background))
                dict[s.Background] = ParseRgb(s.Background);
        }
        return dict.ToFrozenDictionary();
    }
}
