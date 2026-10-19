namespace NativeTui.Core.Repl;

/// <summary>
/// Spinner 滚动项 — 对齐基准 SpinnerAnimationRow + Spinner.tsx 的 50ms 帧组装：
/// marginTop 1 + 行内容（glyph + 消息 + 状态 parts）。内部持有 SpinnerAnimator 跨帧状态，
/// 每次 Render 以 AnimTimeMs 推进一帧；elapsed 用墙钟（暂停感知）。
/// 状态 parts：suffix → timer → tokens(↓/↑) → thinking，` · ` dim 分隔，
/// 整体 dim 括号包裹（thinkingOnly 例外：单 part shimmer 色无括号）；
/// 渐进宽度门控 availableSpace = columns - messageWidth - 5（对齐基准 Progressive width gating）。
/// </summary>
public sealed class SpinnerItem : IScrollItem
{
    /// <summary>思考 shimmer 常量（基准 THINKING_INACTIVE，非 theme 键）。</summary>
    private static readonly Color ThinkingInactive = Color.FromRgb(153, 153, 153);

    /// <summary>思考 shimmer 亮色端（基准 THINKING_INACTIVE_SHIMMER）。</summary>
    private static readonly Color ThinkingInactiveShimmer = Color.FromRgb(185, 185, 185);

    /// <summary>思考 shimmer 延迟（基准 THINKING_DELAY_MS）。</summary>
    private const int ThinkingDelayMs = 3000;

    /// <summary>思考 shimmer 周期秒（基准 THINKING_GLOW_PERIOD_S）。</summary>
    private const double ThinkingGlowPeriodS = 2;

    /// <summary>裸 "thinking" 宽度（缩退阈值，基准 THINKING_BARE_WIDTH）。</summary>
    private const int ThinkingBareWidth = 8;

    /// <summary>30s 后无需 verbose 也显示 timer/tokens（基准 SHOW_TOKENS_AFTER_MS）。</summary>
    private const long ShowTokensAfterMs = 30_000;

    private readonly SpinnerAnimator _animator = new();

    /// <summary>随机源（动词懒采样用，测试注入种子）。</summary>
    private Random _random = Random.Shared;

    /// <summary>懒采样缓存的随机动词（Message 空时生效；基准 useState 挂载采样一次）。</summary>
    private string? _cachedVerb;

    /// <summary>动词文案（调用方拼接 '…'，基准 message = verb + '…'）；空时 fallback 随机动词。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>随机源（默认 Random.Shared；测试注入种子验证确定性）。</summary>
    public Random Random
    {
        get => _random;
        set
        {
            _random = value;
            _cachedVerb = null;
        }
    }

    /// <summary>重采样动词（新回合挂载语义：ResetVerb 后下一帧重新随机）。</summary>
    public void ResetVerb() => _cachedVerb = null;

    /// <summary>覆盖色（非空时替代 theme.Claude 且抑制停滞红，基准 overrideColor 语义）。</summary>
    public Color? OverrideColor { get; set; }

    /// <summary>覆盖 shimmer 色（非空时替代 theme.ClaudeShimmer）。</summary>
    public Color? OverrideShimmerColor { get; set; }

    /// <summary>spinner 模式。</summary>
    public SpinnerMode Mode { get; set; } = SpinnerMode.Responding;

    /// <summary>是否有活动工具（重置停滞计时）。</summary>
    public bool HasActiveTools { get; set; }

    /// <summary>当前响应长度（token 数 ×4 的字符量）。</summary>
    public int ResponseLength { get; set; }

    /// <summary>减弱动态。</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>verbose（恒显 timer/tokens）。</summary>
    public bool Verbose { get; set; }

    /// <summary>后缀部件（如 stop hook 提示，首个 part）。</summary>
    public string? SpinnerSuffix { get; set; }

    /// <summary>思考状态输入。</summary>
    public ThinkingState Thinking { get; set; } = ThinkingState.None;

    /// <summary>动画时钟（50ms 帧累计；M6 接真实时钟，测试直接注入）。</summary>
    public long AnimTimeMs { get; set; }

    /// <summary>墙钟当前值（elapsed 计算）。</summary>
    public long WallNowMs { get; set; }

    /// <summary>本轮开始墙钟。</summary>
    public long LoadingStartMs { get; set; }

    /// <summary>暂停开始墙钟（非空时 elapsed 冻结至此时刻）。</summary>
    public long? PauseStartMs { get; set; }

    /// <summary>累计已暂停毫秒。</summary>
    public long TotalPausedMs { get; set; }

    /// <summary>内容高度 = marginTop 1 + 行 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>渲染：margin 行留空，行内容 = glyph + 消息 + 状态 parts（消息空时随机动词 + '…'）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var message = EffectiveMessage();
        var messageColor = OverrideColor ?? theme.Claude;
        var shimmerColor = OverrideShimmerColor ?? theme.ClaudeShimmer;
        var glimmerWidth = StringWidth.Of(message);
        var state = _animator.Tick(AnimTimeMs, ResponseLength, HasActiveTools, ReducedMotion, Mode, glimmerWidth);
        var row = frame with { Y = frame.Y + 1, Height = 1 };
        SpinnerRow.Render(screen, row, theme, message, messageColor, shimmerColor, Mode, RenderState(state), ReducedMotion, AnimTimeMs);
        RenderStatus(screen, row, theme, state, glimmerWidth);
    }

    /// <summary>生效消息：显式 Message 优先，空时随机动词 + '…'（基准 verb = overrideMessage ?? randomVerb; message = verb + '…'）。</summary>
    private string EffectiveMessage() =>
        Message.Length > 0 ? Message : (_cachedVerb ??= SpinnerVerbs.Sample(_random)) + "…";

    /// <summary>overrideColor 抑制停滞红（基准 stalledIntensity={overrideColor ? 0 : ...}）。</summary>
    private SpinnerAnimState RenderState(SpinnerAnimState state) =>
        OverrideColor is null ? state : state with { StalledIntensity = 0 };

    /// <summary>状态行计算结果（parts + thinkingOnly 标志）。</summary>
    private readonly record struct StatusRow(List<(string Text, int StyleId)> Parts, bool ThinkingOnly);

    private void RenderStatus(Screen screen, Rect row, Theme theme, SpinnerAnimState state, int glimmerWidth)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var status = ComputeStatus(screen, theme, state, dim, glimmerWidth, row.Width);
        if (status.Parts.Count == 0)
        {
            return;
        }

        var statusX = row.X + 2 + glimmerWidth + 1;
        if (statusX >= row.Right)
        {
            return;
        }

        if (status.ThinkingOnly)
        {
            screen.WriteText(row.Y, statusX, status.Parts[0].Text, status.Parts[0].StyleId, row.Right);
            return;
        }

        var col = screen.WriteText(row.Y, statusX, "(", dim, row.Right);
        for (var i = 0; i < status.Parts.Count; i++)
        {
            if (i > 0)
            {
                col = screen.WriteText(row.Y, col, Figures.MiddotSeparator, dim, row.Right);
            }

            col = screen.WriteText(row.Y, col, status.Parts[i].Text, status.Parts[i].StyleId, row.Right);
        }

        screen.WriteText(row.Y, col, ")", dim, row.Right);
    }

    /// <summary>计算状态 parts 与 thinkingOnly（对齐基准 Progressive width gating + parts 组装顺序）。</summary>
    private StatusRow ComputeStatus(Screen screen, Theme theme, SpinnerAnimState state, int dim, int glimmerWidth, int columns)
    {
        var elapsedMs = ElapsedMs();
        var leaderTokens = (int)Math.Round(state.DisplayedResponseLength / 4.0, MidpointRounding.AwayFromZero);
        var timerText = Format.Duration(elapsedMs);
        var tokensText = $"{(Mode == SpinnerMode.Requesting ? Figures.UpArrow : Figures.DownArrow)} {Format.Number(leaderTokens)} tokens";
        var thinkingText = ThinkingText();

        var availableSpace = columns - (glimmerWidth + 2) - 5;
        var wantsTimerAndTokens = Verbose || elapsedMs > ShowTokensAfterMs;
        var showThinking = thinkingText != null && availableSpace > StringWidth.Of(thinkingText);
        if (!showThinking && thinkingText != null && Thinking is ThinkingState.Active { EffortSuffix: { Length: > 0 } } && availableSpace > ThinkingBareWidth)
        {
            thinkingText = "thinking";
            showThinking = true;
        }

        var usedAfterThinking = showThinking && thinkingText != null ? StringWidth.Of(thinkingText) + 3 : 0;
        var showTimer = wantsTimerAndTokens && availableSpace > usedAfterThinking + StringWidth.Of(timerText);
        var usedAfterTimer = usedAfterThinking + (showTimer ? StringWidth.Of(timerText) + 3 : 0);
        var showTokens = wantsTimerAndTokens && leaderTokens > 0 && availableSpace > usedAfterTimer + StringWidth.Of(tokensText);

        var thinkingOnly = showThinking && Thinking is ThinkingState.Active && string.IsNullOrEmpty(SpinnerSuffix) && !showTimer && !showTokens;
        var parts = new List<(string Text, int StyleId)>();
        if (!string.IsNullOrEmpty(SpinnerSuffix))
        {
            parts.Add((SpinnerSuffix!, dim));
        }

        if (showTimer)
        {
            parts.Add((timerText, dim));
        }

        if (showTokens)
        {
            parts.Add((tokensText, dim));
        }

        if (showThinking && thinkingText != null)
        {
            var styleId = Thinking is ThinkingState.Active && !ReducedMotion
                ? screen.Styles.GetOrAdd(new TextStyle { Foreground = ThinkingShimmerColor() })
                : dim;
            parts.Add((thinkingOnly ? $"({thinkingText})" : thinkingText, styleId));
        }

        return new StatusRow(parts, thinkingOnly);
    }

    /// <summary>elapsed：暂停中冻结到 PauseStartMs，否则墙钟差值（减累计暂停）。</summary>
    private long ElapsedMs() =>
        PauseStartMs is long pause ? pause - LoadingStartMs - TotalPausedMs : WallNowMs - LoadingStartMs - TotalPausedMs;

    /// <summary>思考 shimmer 色：3s 延迟后 2s 周期正弦，153↔185 灰插值（基准 THINKING_* 常量）。</summary>
    private Color ThinkingShimmerColor()
    {
        var opacity = AnimTimeMs < ThinkingDelayMs
            ? 0
            : (Math.Sin((AnimTimeMs - ThinkingDelayMs) / 1000.0 * Math.PI * 2 / ThinkingGlowPeriodS) + 1) / 2;
        return SpinnerMath.Interpolate(ThinkingInactive, ThinkingInactiveShimmer, opacity);
    }

    /// <summary>思考文本（Active → "thinking"+后缀；ThoughtFor → "thought for Ns"；Off → null）。</summary>
    private string? ThinkingText() => Thinking switch
    {
        ThinkingState.Active active => $"thinking{active.EffortSuffix}",
        ThinkingState.ThoughtFor thought => $"thought for {Math.Max(1L, (long)Math.Round(thought.DurationMs / 1000.0, MidpointRounding.AwayFromZero))}s",
        _ => null,
    };
}
