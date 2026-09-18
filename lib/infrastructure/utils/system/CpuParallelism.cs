namespace Infrastructure.Utils.Cpu;

/// <summary>
/// CPU 并行度计算工具 — 根据当前 CPU 负载动态推荐并行度
/// </summary>
public static class CpuParallelism
{
    private static readonly int _coreCount = Environment.ProcessorCount;
    private static readonly ExpiringValue<double> _loadCache = new(MeasureCpuLoad, TimeSpan.FromSeconds(1));

    private static WindowsCpuBaseline _windowsBaseline;
    private static FallbackCpuBaseline _fallbackBaseline;

    /// <summary>
    /// 根据当前 CPU 负载动态推荐并行度 — 负载&gt;90% 返回 1，&gt;70% 返回核数一半，否则返回核数
    /// </summary>
    /// <returns>推荐的并行度</returns>
    public static int GetDegree()
    {
        var load = _loadCache.GetOrRefresh();
        return load > 0.90 ? 1
             : load > 0.70 ? Math.Max(1, _coreCount / 2)
             : _coreCount;
    }

    /// <summary>
    /// 根据当前 CPU 负载推荐并行度，并限制不超过指定上限
    /// </summary>
    /// <param name="maxDegree">并行度上限</param>
    /// <returns>推荐的并行度，不超过 maxDegree</returns>
    public static int GetDegree(int maxDegree)
    {
        return Math.Min(GetDegree(), maxDegree);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    private static double MeasureCpuLoad()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return MeasureWindowsCpuLoad();
        return MeasureFallbackCpuLoad();
    }

    private static double MeasureWindowsCpuLoad()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return MeasureFallbackCpuLoad();

        if (!_windowsBaseline.HasBaseline)
        {
            _windowsBaseline = new WindowsCpuBaseline(idle, kernel, user);
            return 0;
        }

        var idleDelta = idle - _windowsBaseline.PrevIdle;
        var totalDelta = (kernel - _windowsBaseline.PrevKernel) + (user - _windowsBaseline.PrevUser);

        _windowsBaseline = new WindowsCpuBaseline(idle, kernel, user);

        if (totalDelta == 0) return 0;

        var busyDelta = totalDelta - idleDelta;
        return (double)busyDelta / totalDelta;
    }

    private static double MeasureFallbackCpuLoad()
    {
        var now = DateTime.UtcNow;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;

        if (!_fallbackBaseline.HasBaseline)
        {
            _fallbackBaseline = new FallbackCpuBaseline(now, cpu);
            return 0;
        }

        var elapsed = (now - _fallbackBaseline.PrevTime).TotalMilliseconds;
        var cpuUsed = (cpu - _fallbackBaseline.PrevCpu).TotalMilliseconds;

        _fallbackBaseline = new FallbackCpuBaseline(now, cpu);

        if (elapsed <= 0 || _coreCount <= 0) return 0;
        return Math.Min(1.0, cpuUsed / (elapsed * _coreCount));
    }

    private readonly record struct WindowsCpuBaseline(long PrevIdle, long PrevKernel, long PrevUser)
    {
        public readonly bool HasBaseline = true;
    }

    private readonly record struct FallbackCpuBaseline(DateTime PrevTime, TimeSpan PrevCpu)
    {
        public readonly bool HasBaseline = true;
    }
}
