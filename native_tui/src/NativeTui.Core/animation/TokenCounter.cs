namespace NativeTui.Core.Animation;

/// <summary>
/// token 计数平滑器 — 对齐基准 SpinnerAnimationRow 的 tokenCounterRef 动画：
/// 真实 token 数跳变时显示值渐进追赶（gap&lt;70 → +3；gap&lt;200 → max(8, ceil(gap·0.15))；
/// 其余 → +50），永不越过当前值。reducedMotion 时直接吸附。
/// </summary>
public sealed class TokenCounter
{
    private int _displayed;
    private bool _seeded;

    /// <summary>推进一帧，返回平滑后的显示值（首帧以当前值种子）。</summary>
    public int Tick(int currentLength, bool reducedMotion)
    {
        if (!_seeded)
        {
            _displayed = currentLength;
            _seeded = true;
        }

        return _displayed = reducedMotion
            ? currentLength
            : CatchUp(currentLength);
    }

    private int CatchUp(int currentLength)
    {
        var gap = currentLength - _displayed;
        if (gap <= 0)
        {
            return _displayed;
        }

        var increment = gap switch
        {
            < 70 => 3,
            < 200 => Math.Max(8, (int)Math.Ceiling(gap * 0.15)),
            _ => 50,
        };
        return Math.Min(_displayed + increment, currentLength);
    }
}
