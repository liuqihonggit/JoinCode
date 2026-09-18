namespace JoinCode.Cli;

// ─── UsageBar ───

/// <summary>
/// 使用量进度条 — CLI 简化版
/// </summary>
public sealed class UsageBar
{
    private readonly double _percentage;
    private readonly int _width;
    private readonly string _filledColor;
    private readonly string _emptyColor;

    /// <summary>
    /// 构造使用量进度条实例
    /// </summary>
    /// <param name="percentage">百分比，0 到 1</param>
    /// <param name="width">进度条宽度，默认 20</param>
    /// <param name="filledColor">已填充部分颜色，可选，默认使用主色</param>
    /// <param name="emptyColor">未填充部分颜色，可选，默认使用弱化色</param>
    public UsageBar(double percentage, int width = 20, string? filledColor = null, string? emptyColor = null)
    {
        _percentage = percentage;
        _width = width;
        _filledColor = filledColor ?? TerminalColors.Primary;
        _emptyColor = emptyColor ?? TerminalColors.Muted;
    }

    /// <summary>
    /// 渲染进度条为带颜色的文本
    /// </summary>
    /// <returns>带 ANSI 颜色的进度条文本</returns>
    public string Render()
    {
        var filled = (int)Math.Round(_percentage * _width);
        if (filled < 0) filled = 0;
        if (filled > _width) filled = _width;
        var bar = $"{_filledColor}{new string('█', filled)}{_emptyColor}{new string('░', _width - filled)}{AnsiStyleEnumConstants.Reset}";
        return bar;
    }

    /// <summary>
    /// 静态渲染进度条，百分比超过 80 时使用警告色
    /// </summary>
    /// <param name="percentage">百分比，0 到 100</param>
    /// <param name="width">进度条宽度，默认 20</param>
    /// <returns>带 ANSI 颜色和百分比的进度条文本</returns>
    public static string Render(double percentage, int width = 20)
    {
        var filled = (int)Math.Round(percentage / 100 * width);
        if (filled < 0) filled = 0;
        if (filled > width) filled = width;
        var bar = new string('█', filled) + new string('░', width - filled);
        var color = percentage > 80 ? TerminalColors.Warning : TerminalColors.Primary;
        return $"{color}{bar}{AnsiStyleEnumConstants.Reset} {percentage:F1}%";
    }
}

