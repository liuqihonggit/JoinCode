namespace NativeTui.Core.Repl;

/// <summary>
/// Spinner 行渲染 — 对齐基准 SpinnerGlyph.tsx + GlimmerMessage.tsx（+ SpinnerAnimationRow 的行组装）。
/// 行结构：glyph 2 列（帧字符 + 尾随空格）+ 消息 + 尾随空格 +（状态 parts，M5 后续批次）。
/// 颜色路径：停滞 → 整行向 ERROR_RED 插值；tool-use → 整体 flash 正弦插值；
/// 其余 → shimmer 高亮窗 [glimmerIndex-1, glimmerIndex+1] 三段切分。
/// </summary>
public static class SpinnerRow
{
    /// <summary>渲染整行（glyph + 消息）。状态 parts（timer/tokens/thinking）由后续批次接入。</summary>
    public static void Render(Screen screen, Rect frame, Theme theme, string message, Color messageColor, Color shimmerColor, SpinnerMode mode, in SpinnerAnimState state, bool reducedMotion, long timeMs)
    {
        RenderGlyph(screen, frame, theme, messageColor, state, reducedMotion, timeMs);
        var messageFrame = new Rect(frame.X + 2, frame.Y, Math.Max(0, frame.Width - 2), frame.Height);
        RenderGlimmer(screen, messageFrame, theme, messageColor, shimmerColor, message, mode, state);
    }

    /// <summary>
    /// 渲染 glyph（占 2 列：帧字符 + 空格）。reducedMotion 用 ● + 1s 亮/1s dim 半周期，
    /// 且忽略停滞红（基准 reducedMotion 分支先返回）。
    /// </summary>
    public static void RenderGlyph(Screen screen, Rect frame, Theme theme, Color messageColor, in SpinnerAnimState state, bool reducedMotion, long timeMs)
    {
        var glyph = reducedMotion
            ? "●"
            : SpinnerMath.SpinnerFrames[state.Frame % SpinnerMath.SpinnerFrames.Length];
        var color = reducedMotion ? messageColor : StallColor(theme, messageColor, state.StalledIntensity);
        var isDim = reducedMotion && timeMs / 1000 % 2 == 1;
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = color, Dim = isDim });
        screen.WriteText(frame.Y, frame.X, glyph, style, frame.Right);
    }

    /// <summary>
    /// 渲染消息 + 尾随空格。空消息不渲染（基准 !message 早退）。
    /// 优先级：停滞红 &gt; tool-use flash &gt; shimmer 窗口 &gt; 纯色。
    /// </summary>
    public static void RenderGlimmer(Screen screen, Rect frame, Theme theme, Color messageColor, Color shimmerColor, string message, SpinnerMode mode, in SpinnerAnimState state)
    {
        if (message.Length == 0)
        {
            return;
        }

        var messageWidth = WidthOf(message);
        var col = frame.X;
        if (state.StalledIntensity > 0)
        {
            var redStyle = StyleOf(screen, StallColor(theme, messageColor, state.StalledIntensity));
            col = screen.WriteText(frame.Y, col, message, redStyle, frame.Right);
            screen.WriteText(frame.Y, col, " ", redStyle, frame.Right);
            return;
        }

        if (mode == SpinnerMode.ToolUse)
        {
            var flashStyle = StyleOf(screen, SpinnerMath.Interpolate(messageColor, shimmerColor, state.FlashOpacity));
            col = screen.WriteText(frame.Y, col, message, flashStyle, frame.Right);
            screen.WriteText(frame.Y, col, " ", StyleOf(screen, messageColor), frame.Right);
            return;
        }

        col = RenderShimmerWindow(screen, frame, message, messageWidth, messageColor, shimmerColor, state.GlimmerIndex);
        screen.WriteText(frame.Y, col, " ", StyleOf(screen, messageColor), frame.Right);
    }

    /// <summary>shimmer 三段切分渲染（before/shim/after），返回下一可写列。</summary>
    private static int RenderShimmerWindow(Screen screen, Rect frame, string message, int messageWidth, Color messageColor, Color shimmerColor, int glimmerIndex)
    {
        var messageStyle = StyleOf(screen, messageColor);
        var shimmerStart = glimmerIndex - 1;
        var shimmerEnd = glimmerIndex + 1;
        if (shimmerStart >= messageWidth || shimmerEnd < 0)
        {
            return screen.WriteText(frame.Y, frame.X, message, messageStyle, frame.Right);
        }

        var (before, shim, after) = SplitByWindow(message, Math.Max(0, shimmerStart), shimmerEnd);
        var col = frame.X;
        if (before.Length > 0)
        {
            col = screen.WriteText(frame.Y, col, before, messageStyle, frame.Right);
        }

        if (shim.Length > 0)
        {
            col = screen.WriteText(frame.Y, col, shim, StyleOf(screen, shimmerColor), frame.Right);
        }

        if (after.Length > 0)
        {
            col = screen.WriteText(frame.Y, col, after, messageStyle, frame.Right);
        }

        return col;
    }

    /// <summary>
    /// 按高亮窗 [clampedStart, shimmerEnd] 切分消息为三段（对齐基准 GlimmerMessage 段循环）：
    /// 段起点 + 宽 ≤ clampedStart → before；段起点 &gt; shimmerEnd → after；跨窗或窗内 → shim。
    /// 零宽 rune（组合附标）跟随前一 rune 所在段。
    /// </summary>
    private static (string Before, string Shim, string After) SplitByWindow(string message, int clampedStart, int shimmerEnd)
    {
        var before = new StringBuilder();
        var shim = new StringBuilder();
        var after = new StringBuilder();
        var bucket = before;
        var colPos = 0;
        foreach (var rune in message.EnumerateRunes())
        {
            var width = RuneWidth.Of(rune);
            if (width == 0)
            {
                bucket.Append(rune);
                continue;
            }

            if (colPos + width <= clampedStart)
            {
                before.Append(rune);
                bucket = before;
            }
            else if (colPos > shimmerEnd)
            {
                after.Append(rune);
                bucket = after;
            }
            else
            {
                shim.Append(rune);
                bucket = shim;
            }

            colPos += width;
        }

        return (before.ToString(), shim.ToString(), after.ToString());
    }

    /// <summary>停滞色：Rgb 色向 ERROR_RED 插值；非 Rgb 离散回退（过半取 theme.Error）。</summary>
    private static Color StallColor(Theme theme, Color messageColor, double intensity) =>
        messageColor.Kind == ColorKind.Rgb
            ? SpinnerMath.Interpolate(messageColor, SpinnerMath.ErrorRed, intensity)
            : intensity > 0.5 ? theme.Error : messageColor;

    private static int StyleOf(Screen screen, Color foreground) =>
        screen.Styles.GetOrAdd(new TextStyle { Foreground = foreground });

    /// <summary>消息显示宽度（rune 宽求和，零宽 rune 不占列）。</summary>
    private static int WidthOf(string message)
    {
        var width = 0;
        foreach (var rune in message.EnumerateRunes())
        {
            width += RuneWidth.Of(rune);
        }

        return width;
    }
}
