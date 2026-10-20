namespace NativeTui.Core.Animation;

/// <summary>
/// 单帧动画派生态 — 对齐基准 SpinnerAnimationRow 每帧从 50ms 时钟派生的全部值。
/// 渲染层（SpinnerRow）消费本状态 + 消息文本产出 cell，本层不含任何渲染逻辑。
/// </summary>
public readonly record struct SpinnerAnimState
{
    /// <summary>glyph 帧序号（未取模，渲染处 % 12；reducedMotion 恒 0）。</summary>
    public int Frame { get; init; }

    /// <summary>是否停滞（3s 无新 token 且无活动工具）。</summary>
    public bool IsStalled { get; init; }

    /// <summary>停滞强度 0..1（平滑后；驱动 glyph/message 变红插值）。</summary>
    public double StalledIntensity { get; init; }

    /// <summary>shimmer 高亮索引（-100 = 熄灭）。</summary>
    public int GlimmerIndex { get; init; }

    /// <summary>tool-use 整体 flash 透明度 0..1。</summary>
    public double FlashOpacity { get; init; }

    /// <summary>平滑后的 token 显示值。</summary>
    public int DisplayedResponseLength { get; init; }
}

/// <summary>
/// Spinner 动画器门面 — 组合停滞跟踪 + token 平滑 + 无状态派生（frame/glimmer/flash）
/// 为单一 <see cref="SpinnerAnimState"/>。对齐基准 SpinnerAnimationRow 的 50ms 帧循环体。
/// </summary>
public sealed class SpinnerAnimator
{
    private readonly StallTracker _stall = new();
    private readonly TokenCounter _tokens = new();

    /// <summary>
    /// 推进一帧（由 50ms 时钟调用；reducedMotion 时调用方应停表，仅状态变化时推进）。
    /// </summary>
    /// <param name="timeMs">动画时钟（挂载起累计毫秒）。</param>
    /// <param name="responseLength">当前真实响应长度（token 数 ×4 之前的字符量）。</param>
    /// <param name="hasActiveTools">是否有活动工具（重置停滞计时）。</param>
    /// <param name="reducedMotion">减弱动态（全静态 + 瞬时强度）。</param>
    /// <param name="mode">spinner 模式（决定 shimmer 方向/速度与 flash）。</param>
    /// <param name="messageWidth">消息显示宽度（决定 shimmer 周期）。</param>
    public SpinnerAnimState Tick(long timeMs, int responseLength, bool hasActiveTools, bool reducedMotion, SpinnerMode mode, int messageWidth)
    {
        var (isStalled, stalledIntensity) = _stall.Tick(timeMs, responseLength, hasActiveTools, reducedMotion);
        return new SpinnerAnimState
        {
            Frame = reducedMotion ? 0 : SpinnerMath.FrameOf(timeMs),
            IsStalled = isStalled,
            StalledIntensity = stalledIntensity,
            GlimmerIndex = SpinnerMath.GlimmerIndex(mode, timeMs, messageWidth, isStalled, reducedMotion),
            FlashOpacity = SpinnerMath.FlashOpacity(mode, timeMs, reducedMotion),
            DisplayedResponseLength = _tokens.Tick(responseLength, reducedMotion),
        };
    }
}
