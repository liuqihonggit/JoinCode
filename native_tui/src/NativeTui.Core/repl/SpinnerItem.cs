namespace NativeTui.Core.Repl;

/// <summary>
/// Spinner 滚动项 — 对齐基准 SpinnerAnimationRow + Spinner.tsx 的 50ms 帧组装：
/// marginTop 1 + 行内容（glyph + 消息 + 状态 parts）。内部持有 SpinnerAnimator 跨帧状态，
/// 每次 Render 以 AnimTimeMs 推进一帧；elapsed 用墙钟（暂停感知）。
/// 状态 parts：suffix → timer → tokens(↓/↑) → thinking，` · ` dim 分隔，
/// 整体 dim 括号包裹（thinkingOnly 例外：单 part shimmer 色无括号）；
/// 渐进宽度门控 availableSpace = columns - messageWidth - 5（对齐基准 Progressive width gating）。
/// 扩展态（通知与 footer 域 S4）：三态显示 — Hidden（零输出）&gt; IsIdle（dim 静态空闲行）&gt;
/// 正常动画 + 时间驱动提示行（elapsed &gt; 30s /btw、&gt; 1800s /clear — 帧拍 elapsed 通道复用，
/// 基准 Spinner.tsx:255-259）+ 终端字符集分支（<see cref="SpinnerCharsets"/> — darwin/ghostty）。
/// </summary>
public sealed class SpinnerItem : IScrollItem, IAnimatable
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

    /// <summary>/btw 提示阈值（基准 Spinner.tsx:258 — elapsed &gt; 30_000ms 且未用过 /btw）。</summary>
    private const long BtwTipThresholdMs = 30_000;

    /// <summary>/clear 提示阈值（基准 Spinner.tsx:257 — elapsed &gt; 1_800_000ms）。</summary>
    private const long ClearTipThresholdMs = 1_800_000;

    /// <summary>/btw 提示文案（汉化 — 基准 "Use /btw to ask a quick side question..."）。</summary>
    private const string BtwTipText = "/btw 记录随想";

    /// <summary>/clear 提示文案（汉化 — 基准 "Use /clear to start fresh when switching topics..."）。</summary>
    private const string ClearTipText = "/clear 清理上下文";

    /// <summary>提示行前缀（汉化 — 基准 `Tip: ${effectiveTip}`）。</summary>
    private const string TipPrefix = "提示: ";

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

    /// <summary>思考力度后缀（如 " hard"，基准 getEffortSuffix）。</summary>
    public string EffortSuffix { get; set; } = string.Empty;

    /// <summary>思考生命周期状态机（内部驱动，Mode==Thinking 触发）。</summary>
    private readonly ThinkingLifecycle _thinkingLifecycle = new();

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

    /// <summary>隐藏态（对齐基准 REPL.tsx showSpinner：空闲非 busy 完全不渲染 — 高度 0）。
    /// driver BeginTurn/EndTurn 驱动（busy=显示：等待/思考/工具运行反馈；idle=隐藏：基准空闲无 spinner 行）。</summary>
    public bool Hidden { get; set; }

    /// <summary>空闲态（子代理运行中 leader 空闲 — 显示 dim 静态空闲行替代动画 spinner，
    /// 基准 Spinner.tsx:229 leaderIsIdle 分支：规避 useStalledAnimation 3s 无新 token 误报停滞红；
    /// 接线方（SubagentGroup/Driver 域）判定 leader 空闲 + 有运行中子代理时置 true）。
    /// 优先级低于 <see cref="Hidden"/>、高于正常动画。</summary>
    public bool IsIdle { get; set; }

    /// <summary>空闲态显示文本（默认 "空闲"；接线方可传变体如 "已工作 Ns" — 基准 Worked for 分支汉化通道）。</summary>
    public string IdleText { get; set; } = "空闲";

    /// <summary>外部基础提示文本（时间提示未激活时的兜底层 — 基准 spinnerTip prop；null = 无）。</summary>
    public string? SpinnerTip { get; set; }

    /// <summary>提示开关（基准 settings.spinnerTipsEnabled !== false；默认开）。</summary>
    public bool TipsEnabled { get; set; } = true;

    /// <summary>spinner 12 帧往返序列（终端字符集分支 — Windows Terminal/默认 `*` 集、
    /// ghostty `✽→*`、darwin `✳`，见 <see cref="SpinnerCharsets"/>；null = 默认集
    /// <see cref="SpinnerMath.SpinnerFrames"/> — 既有消费方零改动兼容）。</summary>
    public string[]? Frames { get; set; }

    /// <summary>内容高度 = Hidden ? 0 : IsIdle ? margin 1 + 空闲行 1 : margin 1 + 行 1 + 提示行 0/1。
    /// 空闲态无提示行（基准 leaderIsIdle 分支独立 return，不渲染 tip）。</summary>
    public int MeasureHeight(int width) => Hidden ? 0 : IsIdle ? 2 : 2 + TipHeight();

    /// <summary>提示行高（0 = 无生效提示）。</summary>
    private int TipHeight() => EffectiveTip(ElapsedMs()) is null ? 0 : 1;

    /// <summary>渲染：margin 行留空，行内容 = glyph + 消息 + 状态 parts（消息空时随机动词 + '…'）；
    /// Hidden 时零输出（不动画不占行）；IsIdle 时静态空闲行（dim ✻ + 文本，无动画）；
    /// 提示行（时间驱动 — elapsed &gt; 30s / &gt; 1800s）追加在 spinner 行下方。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (Hidden)
        {
            return;
        }

        if (IsIdle)
        {
            RenderIdle(screen, frame);
            return;
        }

        var message = EffectiveMessage();
        var messageColor = OverrideColor ?? theme.Claude;
        var shimmerColor = OverrideShimmerColor ?? theme.ClaudeShimmer;
        var glimmerWidth = StringWidth.Of(message);
        var state = _animator.Tick(AnimTimeMs, ResponseLength, HasActiveTools, ReducedMotion, Mode, glimmerWidth);
        var row = frame with { Y = frame.Y + 1, Height = 1 };
        SpinnerRow.Render(screen, row, theme, message, messageColor, shimmerColor, Mode, RenderState(state), ReducedMotion, AnimTimeMs, Frames);
        var thinking = _thinkingLifecycle.Tick(Mode == SpinnerMode.Thinking, WallNowMs, EffortSuffix);
        RenderStatus(screen, row, theme, state, glimmerWidth, thinking);
        RenderTip(screen, frame);
    }

    /// <summary>空闲态渲染（基准 Spinner.tsx:229-238 leaderIsIdle 分支）：
    /// margin 行留空 + dim `✻ {IdleText}`（静态无动画）。</summary>
    private void RenderIdle(Screen screen, Rect frame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var row = frame.Y + 1;
        var col = screen.WriteText(row, frame.X, Figures.TeardropAsterisk, dim, frame.Right);
        screen.WriteText(row, col, $" {IdleText}", dim, frame.Right);
    }

    /// <summary>提示行渲染（spinner 行下方 — 基准 Spinner.tsx:294-298 MessageResponse 形态）：
    /// dim 缩进前缀 `  ⎿  ` + dim 提示文本。</summary>
    private void RenderTip(Screen screen, Rect frame)
    {
        if (EffectiveTip(ElapsedMs()) is not { } tip)
        {
            return;
        }

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var row = frame.Y + 2;
        var col = screen.WriteText(row, frame.X, Figures.ToolResultIndent, dim, frame.Right);
        screen.WriteText(row, col, tip, dim, frame.Right);
    }

    /// <summary>生效提示（基准 Spinner.tsx:255-259 链，nextTask 通道无消费方暂缺）：
    /// 关 → null；elapsed &gt; 1800s /clear &gt; elapsed &gt; 30s /btw &gt; 外部 <see cref="SpinnerTip"/> 兜底。</summary>
    /// <param name="elapsedMs">本轮已进行毫秒（暂停感知 — <see cref="ElapsedMs"/>）。</param>
    private string? EffectiveTip(long elapsedMs) => !TipsEnabled ? null
        : elapsedMs > ClearTipThresholdMs ? TipPrefix + ClearTipText
        : elapsedMs > BtwTipThresholdMs ? TipPrefix + BtwTipText
        : SpinnerTip is { Length: > 0 } tip ? TipPrefix + tip
        : null;

    /// <summary>生效消息：显式 Message 优先，空时随机动词 + '…'（基准 verb = overrideMessage ?? randomVerb; message = verb + '…'）。</summary>
    private string EffectiveMessage() =>
        Message.Length > 0 ? Message : (_cachedVerb ??= SpinnerVerbs.Sample(_random)) + "…";

    /// <summary>overrideColor 抑制停滞红（基准 stalledIntensity={overrideColor ? 0 : ...}）。</summary>
    private SpinnerAnimState RenderState(SpinnerAnimState state) =>
        OverrideColor is null ? state : state with { StalledIntensity = 0 };

    /// <summary>状态行计算结果（parts + thinkingOnly 标志）。</summary>
    private readonly record struct StatusRow(List<(string Text, int StyleId)> Parts, bool ThinkingOnly);

    private void RenderStatus(Screen screen, Rect row, Theme theme, SpinnerAnimState state, int glimmerWidth, ThinkingState thinking)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var status = ComputeStatus(screen, theme, state, dim, glimmerWidth, row.Width, thinking);
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
    private StatusRow ComputeStatus(Screen screen, Theme theme, SpinnerAnimState state, int dim, int glimmerWidth, int columns, ThinkingState thinking)
    {
        var elapsedMs = ElapsedMs();
        var leaderTokens = (int)Math.Round(state.DisplayedResponseLength / 4.0, MidpointRounding.AwayFromZero);
        var timerText = Format.Duration(elapsedMs);
        var tokensText = $"{(Mode == SpinnerMode.Requesting ? Figures.UpArrow : Figures.DownArrow)} {Format.Number(leaderTokens)} tokens";
        var thinkingText = ThinkingText(thinking);

        var availableSpace = columns - (glimmerWidth + 2) - 5;
        var wantsTimerAndTokens = Verbose || elapsedMs > ShowTokensAfterMs;
        var showThinking = thinkingText != null && availableSpace > StringWidth.Of(thinkingText);
        if (!showThinking && thinkingText != null && thinking is ThinkingState.Active { EffortSuffix: { Length: > 0 } } && availableSpace > ThinkingBareWidth)
        {
            thinkingText = "thinking";
            showThinking = true;
        }

        var usedAfterThinking = showThinking && thinkingText != null ? StringWidth.Of(thinkingText) + 3 : 0;
        var showTimer = wantsTimerAndTokens && availableSpace > usedAfterThinking + StringWidth.Of(timerText);
        var usedAfterTimer = usedAfterThinking + (showTimer ? StringWidth.Of(timerText) + 3 : 0);
        var showTokens = wantsTimerAndTokens && leaderTokens > 0 && availableSpace > usedAfterTimer + StringWidth.Of(tokensText);

        var thinkingOnly = showThinking && thinking is ThinkingState.Active && string.IsNullOrEmpty(SpinnerSuffix) && !showTimer && !showTokens;
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
            var styleId = thinking is ThinkingState.Active && !ReducedMotion
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
    private static string? ThinkingText(ThinkingState thinking) => thinking switch
    {
        ThinkingState.Active active => $"thinking{active.EffortSuffix}",
        ThinkingState.ThoughtFor thought => $"thought for {Math.Max(1L, (long)Math.Round(thought.DurationMs / 1000.0, MidpointRounding.AwayFromZero))}s",
        _ => null,
    };
}
