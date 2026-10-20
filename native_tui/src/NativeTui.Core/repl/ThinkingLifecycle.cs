namespace NativeTui.Core.Repl;

/// <summary>
/// 思考状态生命周期状态机 — 对齐基准 Spinner.tsx thinkingStatus（'thinking' | number | null）：
/// 进入 thinking 起 'thinking' 最短展示 2s（提前结束补足）；
/// 结束后展示 'thought for Ns'（真实时长）2s；随后清空。
/// 基准 setTimeout 语义离散化为帧循环判定：Tick(thinkingNow, wallNowMs) 纯状态推进。
/// </summary>
public sealed class ThinkingLifecycle
{
    /// <summary>thinking 最短总展示时长（基准 2000ms）。</summary>
    public const long MinThinkingDisplayMs = 2000;

    /// <summary>时长展示窗口（基准 setTimeout(clear, 2000)）。</summary>
    public const long DurationDisplayMs = 2000;

    private long? _startMs;
    private long? _stopMs;
    private long _durationMs;

    /// <summary>推进一帧：输入当前是否在思考与墙钟，输出思考展示状态。</summary>
    public ThinkingState Tick(bool thinkingNow, long wallNowMs, string effortSuffix = "")
    {
        if (thinkingNow)
        {
            if (_startMs is null)
            {
                _startMs = wallNowMs;
                _stopMs = null;
            }

            return new ThinkingState.Active(effortSuffix);
        }

        if (_startMs is long start)
        {
            _stopMs = wallNowMs;
            _durationMs = wallNowMs - start;
            _startMs = null;
        }

        if (_stopMs is long stop)
        {
            var remaining = Math.Max(0, MinThinkingDisplayMs - _durationMs);
            var durationStart = stop + remaining;
            if (wallNowMs < durationStart)
            {
                return new ThinkingState.Active(effortSuffix);
            }

            if (wallNowMs < durationStart + DurationDisplayMs)
            {
                return new ThinkingState.ThoughtFor(_durationMs);
            }

            _stopMs = null;
        }

        return ThinkingState.None;
    }
}
