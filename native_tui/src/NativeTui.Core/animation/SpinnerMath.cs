namespace NativeTui.Core.Animation;

/// <summary>
/// Spinner 纯函数数学 — 对齐基准 components/Spinner/utils.ts + SpinnerGlyph.tsx 常量 +
/// useShimmerAnimation.ts + SpinnerAnimationRow.tsx 派生式。
/// 无状态：全部由 (time, 输入) 直接求值，L3 帧级测试可逐帧断言。
/// </summary>
public static class SpinnerMath
{
    /// <summary>glyph 帧周期 120ms（SpinnerAnimationRow: floor(time/120)）。</summary>
    public const int GlyphFrameMs = 120;

    /// <summary>非 requesting shimmer 步长 200ms。</summary>
    public const int GlimmerSlowMs = 200;

    /// <summary>requesting shimmer 步长 50ms。</summary>
    public const int GlimmerFastMs = 50;

    /// <summary>shimmer 窗口左右各延 10 列（进入/退出消息两侧的缓冲）。</summary>
    public const int GlimmerMargin = 10;

    /// <summary>cycleLength = messageWidth + 20（一个完整往返周期宽度）。</summary>
    public const int GlimmerTrail = 20;

    /// <summary>shimmer 熄灭哨兵值（reducedMotion 或停滞时）。</summary>
    public const int GlimmerOff = -100;

    /// <summary>停滞判定阈值：3s 无新 token。</summary>
    public const double StallThresholdMs = 3000;

    /// <summary>停滞渐变时长：2s 内 0→1。</summary>
    public const double StallFadeMs = 2000;

    /// <summary>停滞平滑步长周期（每 50ms 步进一次）。</summary>
    public const int StallSmoothStepMs = 50;

    /// <summary>停滞平滑每步逼近比例（diff * 0.1）。</summary>
    public const double StallSmoothRate = 0.1;

    /// <summary>停滞红（基准 ERROR_RED rgb(171,43,63)）。</summary>
    public static readonly Color ErrorRed = Color.FromRgb(171, 43, 63);

    /// <summary>非 darwin 默认字符集（utils.ts getDefaultCharacters：✳→*）。</summary>
    public static readonly string[] DefaultCharacters = ["·", "✢", "*", "✶", "✻", "✽"];

    /// <summary>12 帧往返弹跳序列（前向 + 反转拼接，SpinnerGlyph.tsx）。</summary>
    public static readonly string[] SpinnerFrames =
    [
        .. DefaultCharacters,
        .. DefaultCharacters.Reverse(),
    ];

    /// <summary>glyph 帧序号（未取模，渲染处 % SpinnerFrames.Length）。</summary>
    public static int FrameOf(long timeMs) => (int)(timeMs / GlyphFrameMs);

    /// <summary>
    /// shimmer 高亮索引 — requesting 模式右移（pos%cycle-10），其余左移（width+10-pos%cycle）。
    /// stalled/reducedMotion 时返回 <see cref="GlimmerOff"/>（熄灭）。
    /// </summary>
    public static int GlimmerIndex(SpinnerMode mode, long timeMs, int messageWidth, bool isStalled, bool reducedMotion)
    {
        if (reducedMotion || isStalled)
        {
            return GlimmerOff;
        }

        var glimmerSpeed = mode == SpinnerMode.Requesting ? GlimmerFastMs : GlimmerSlowMs;
        var cycleLength = messageWidth + GlimmerTrail;
        var cyclePosition = timeMs / glimmerSpeed % cycleLength;
        return mode == SpinnerMode.Requesting
            ? (int)cyclePosition - GlimmerMargin
            : messageWidth + GlimmerMargin - (int)cyclePosition;
    }

    /// <summary>tool-use 整体闪烁透明度：(sin(time/1000·π)+1)/2，周期 2s；其余模式恒 0。</summary>
    public static double FlashOpacity(SpinnerMode mode, long timeMs, bool reducedMotion) =>
        reducedMotion || mode != SpinnerMode.ToolUse
            ? 0
            : (Math.Sin(timeMs / 1000.0 * Math.PI) + 1) / 2;

    /// <summary>
    /// RGB 逐通道线性插值 — 对齐基准 interpolateColor（Math.round 半值远离零）。
    /// 非 Rgb 色无法插值，离散回退：t 过半取目标色。
    /// </summary>
    public static Color Interpolate(Color from, Color to, double t)
    {
        if (from.Kind != ColorKind.Rgb || to.Kind != ColorKind.Rgb)
        {
            return t < 0.5 ? from : to;
        }

        return Color.FromRgb(
            (int)Math.Round(from.R + (to.R - from.R) * t, MidpointRounding.AwayFromZero),
            (int)Math.Round(from.G + (to.G - from.G) * t, MidpointRounding.AwayFromZero),
            (int)Math.Round(from.B + (to.B - from.B) * t, MidpointRounding.AwayFromZero));
    }
}
