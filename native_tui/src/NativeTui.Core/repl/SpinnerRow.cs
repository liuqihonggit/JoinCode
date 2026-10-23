namespace NativeTui.Core.Repl;

/// <summary>
/// Spinner 行渲染 — 对齐基准 SpinnerGlyph.tsx + GlimmerMessage.tsx（+ SpinnerAnimationRow 的行组装）。
/// 行结构：glyph 2 列（帧字符 + 尾随空格）+ 消息 + 尾随空格 +（状态 parts，M5 后续批次）。
/// 颜色路径：停滞 → 整行向 ERROR_RED 插值；tool-use → 整体 flash 正弦插值；
/// 其余 → shimmer 高亮窗 [glimmerIndex-1, glimmerIndex+1] 三段切分。
/// </summary>
public static class SpinnerRow
{
    /// <summary>渲染整行（glyph + 消息）。状态 parts（timer/tokens/thinking）由后续批次接入。
    /// frames：终端字符集 12 帧往返序列（null = 默认集 <see cref="SpinnerMath.SpinnerFrames"/>）。</summary>
    public static void Render(Screen screen, Rect frame, Theme theme, string message, Color messageColor, Color shimmerColor, SpinnerMode mode, in SpinnerAnimState state, bool reducedMotion, long timeMs, string[]? frames = null)
    {
        RenderGlyph(screen, frame, theme, messageColor, state, reducedMotion, timeMs, frames);
        var messageFrame = new Rect(frame.X + 2, frame.Y, Math.Max(0, frame.Width - 2), frame.Height);
        RenderGlimmer(screen, messageFrame, theme, messageColor, shimmerColor, message, mode, state);
    }

    /// <summary>
    /// 渲染 glyph（占 2 列：帧字符 + 空格）。reducedMotion 用 ● + 1s 亮/1s dim 半周期，
    /// 且忽略停滞红（基准 reducedMotion 分支先返回）。
    /// frames：终端字符集 12 帧往返序列（null = 默认集 — darwin/ghostty 分支见 <see cref="SpinnerCharsets"/>）。
    /// </summary>
    public static void RenderGlyph(Screen screen, Rect frame, Theme theme, Color messageColor, in SpinnerAnimState state, bool reducedMotion, long timeMs, string[]? frames = null)
    {
        var sequence = frames ?? SpinnerMath.SpinnerFrames;
        var glyph = reducedMotion
            ? "●"
            : sequence[state.Frame % sequence.Length];
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

        var messageWidth = StringWidth.Of(message);
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
}

/// <summary>
/// Spinner 终端字符集 — 对齐基准 Spinner/utils.ts:4-11 getDefaultCharacters 分支：
/// ghostty（TERM=xterm-ghostty）✽→*（该终端渲染偏移规避）、darwin ✳ 保留、
/// 其余（Windows Terminal/Linux/未知）✳→*（= <see cref="SpinnerMath.DefaultCharacters"/> 默认集）。
/// 输出为 12 帧往返弹跳序列（字符集 + 反转拼接 — 基准 SPINNER_FRAMES 构造）。
/// 探测：TERM 环境变量 + OperatingSystem.IsMacOS，进程内缓存一次（终端类型会话期不变）。
/// </summary>
public static class SpinnerCharsets
{
    /// <summary>ghostty 6 字符集（✽→* — 渲染偏移规避）。</summary>
    public static readonly string[] GhosttyCharacters = ["·", "✢", "✳", "✶", "✻", "*"];

    /// <summary>darwin 6 字符集（✳ 保留 — mac 终端渲染正常）。</summary>
    public static readonly string[] DarwinCharacters = ["·", "✢", "✳", "✶", "✻", "✽"];

    /// <summary>默认 12 帧序列（SpinnerMath 唯一数据源 — 非 darwin/ghostty 终端复用，不重复定义）。</summary>
    public static readonly string[] DefaultFrames = SpinnerMath.SpinnerFrames;

    /// <summary>探测结果缓存（null = 未探测）。</summary>
    private static string[]? _detected;

    /// <summary>当前终端 12 帧序列（探测一次并缓存 — 会话期终端类型不变；测试经 <see cref="Of"/> 注入绕过）。</summary>
    public static string[] Detect() =>
        _detected ??= Of(Environment.GetEnvironmentVariable("TERM"), OperatingSystem.IsMacOS());

    /// <summary>12 帧序列选择纯函数（term/isDarwin 注入可测 — ghostty &gt; darwin &gt; 默认分支序对齐基准
    /// Spinner/utils.ts:4-11：ghostty 优先判定（✽ 在 ghostty 有渲染偏移，基准注释明言规避 —
    /// TASK069 B-5 修复 mac+ghostty 组合误入 Darwin 集））。</summary>
    /// <param name="term">TERM 环境变量值（null = 未设置）。</param>
    /// <param name="isDarwin">是否 macOS 平台。</param>
    /// <returns>12 帧往返弹跳序列。</returns>
    public static string[] Of(string? term, bool isDarwin) =>
        term == "xterm-ghostty" ? Bounce(GhosttyCharacters)
        : isDarwin ? Bounce(DarwinCharacters)
        : DefaultFrames;

    /// <summary>6 字符集 → 12 帧往返弹跳（前向 + 反转拼接 — 基准 SPINNER_FRAMES 构造式）。</summary>
    private static string[] Bounce(string[] characters) =>
        [.. characters, .. characters.Reverse()];
}
