namespace NativeTui.Core.Animation;

/// <summary>
/// 停滞状态机 — 对齐基准 useStalledAnimation（React hook 的 C# 有状态转写）。
/// 由父级 50ms 动画时钟驱动（非墙钟，终端失焦时自动减速）。
/// 语义：新 token 到达重置计时；工具活跃期间恒不停滞且刷新计时点；
/// 超过 3s 无新 token 判定停滞，2s 内 0→1 渐变（每 50ms 步进逼近 10%）。
/// </summary>
public sealed class StallTracker
{
    private long _lastTokenTime;
    private int _lastResponseLength;
    private long _mountTime;
    private double _smoothedIntensity;
    private long _lastSmoothTime;
    private bool _mounted;

    /// <summary>
    /// 推进一帧。首次调用为挂载：lastTokenTime/mountTime/lastSmoothTime 均取当前时钟值。
    /// </summary>
    /// <returns>(是否停滞, 平滑后强度 0..1；reducedMotion 时为瞬时值)。</returns>
    public (bool IsStalled, double Intensity) Tick(long timeMs, int responseLength, bool hasActiveTools, bool reducedMotion)
    {
        SeedOnFirstTick(timeMs, responseLength);
        ResetIfNewTokens(timeMs, responseLength);

        var timeSinceLastToken = TimeSinceLastToken(timeMs, responseLength, hasActiveTools);
        var (isStalled, intensity) = StallDerivation(timeSinceLastToken, hasActiveTools);
        SmoothIntensity(timeMs, intensity, reducedMotion);

        return (isStalled, reducedMotion ? intensity : _smoothedIntensity);
    }

    private void SeedOnFirstTick(long timeMs, int responseLength)
    {
        if (_mounted)
        {
            return;
        }

        _lastTokenTime = timeMs;
        _lastResponseLength = responseLength;
        _mountTime = timeMs;
        _smoothedIntensity = 0;
        _lastSmoothTime = timeMs;
        _mounted = true;
    }

    private void ResetIfNewTokens(long timeMs, int responseLength)
    {
        if (responseLength <= _lastResponseLength)
        {
            return;
        }

        _lastTokenTime = timeMs;
        _lastResponseLength = responseLength;
        _smoothedIntensity = 0;
        _lastSmoothTime = timeMs;
    }

    private long TimeSinceLastToken(long timeMs, int responseLength, bool hasActiveTools)
    {
        if (hasActiveTools)
        {
            _lastTokenTime = timeMs;
            return 0;
        }

        return responseLength > 0 ? timeMs - _lastTokenTime : timeMs - _mountTime;
    }

    private static (bool IsStalled, double Intensity) StallDerivation(long timeSinceLastToken, bool hasActiveTools)
    {
        var isStalled = timeSinceLastToken > SpinnerMath.StallThresholdMs && !hasActiveTools;
        var intensity = isStalled
            ? Math.Min((timeSinceLastToken - SpinnerMath.StallThresholdMs) / SpinnerMath.StallFadeMs, 1)
            : 0;
        return (isStalled, intensity);
    }

    private void SmoothIntensity(long timeMs, double intensity, bool reducedMotion)
    {
        if (!reducedMotion && (intensity > 0 || _smoothedIntensity > 0))
        {
            StepSmoothedIntensity(timeMs, intensity);
        }
        else
        {
            _smoothedIntensity = intensity;
            _lastSmoothTime = timeMs;
        }
    }

    private void StepSmoothedIntensity(long timeMs, double intensity)
    {
        var dt = timeMs - _lastSmoothTime;
        if (dt < SpinnerMath.StallSmoothStepMs)
        {
            return;
        }

        var steps = (int)(dt / SpinnerMath.StallSmoothStepMs);
        var current = _smoothedIntensity;
        for (var i = 0; i < steps; i++)
        {
            var diff = intensity - current;
            if (Math.Abs(diff) < 0.01)
            {
                current = intensity;
                break;
            }

            current += diff * SpinnerMath.StallSmoothRate;
        }

        _smoothedIntensity = current;
        _lastSmoothTime = timeMs;
    }
}
