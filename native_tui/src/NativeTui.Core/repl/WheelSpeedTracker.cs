namespace NativeTui.Core.Repl;

/// <summary>
/// 滚轮加速度 ramp 跟踪器（TASK066 板块 C P3 收口 — C5 最小接线登记的"加速度 ramp"遗留）。
/// 基准 computeWheelStep native 路径 TRACKPAD/HI-RES 段（ScrollKeybindingHandler.tsx:255-265）：
/// 40ms 判定窗内连续滚轮事件按 STEP=0.3 递增浮点乘数、首事件/超窗重置回 base、
/// cap = max(6, base×2)（:262）、返回 floor(乘数) — 连发 floor 序列 1,1,1,1,2,2,2,3,...
///（第 5 事件起 2 行；乘数非整数计数而是浮点累积）。
/// 常量对齐基准 :47-49（WHEEL_ACCEL_WINDOW_MS=40 / WHEEL_ACCEL_STEP=0.3 / WHEEL_ACCEL_MAX=6）。
/// 基准的弹跳延迟判定/wheelMode 衰减曲线/burst 计数（:176-253 鼠标编码器路径）不在最小收口
/// 范围；方向不参与判定（UpDown 共用乘数状态 — 基准方向翻转重置 mult 属弹跳检测配套逻辑，
/// 此处简化，后续任务登记）。时钟经构造注入（对齐 ReplShell.EscClock 先例 — 测试可控时间戳）；
/// base 经构造参数注入（宿主读 CLAUDE_CODE_SCROLL_SPEED 经 <see cref="ParseScrollSpeedBase"/>
/// 解析传参 — Core 不读 env 惯例）。
/// </summary>
public sealed class WheelSpeedTracker
{
    /// <summary>加速度判定窗（ms）— 基准 WHEEL_ACCEL_WINDOW_MS（ScrollKeybindingHandler.tsx:47）：
    /// 间隔 ≤ 窗的连续事件 ramp；基准 :259 gap &gt; 窗才重置（gap=40 恰在窗内）。</summary>
    public const long AccelWindowMs = 40;

    /// <summary>窗内乘数步进 — 基准 WHEEL_ACCEL_STEP（ScrollKeybindingHandler.tsx:48）：
    /// 浮点累积（非整数计数），返回时 floor。</summary>
    public const double AccelStep = 0.3;

    /// <summary>加速度上限 — 基准 WHEEL_ACCEL_MAX（ScrollKeybindingHandler.tsx:49）；
    /// 有效 cap = max(本值, base×2)（:262 — 高 base 旋钮时上限随之上抬）。</summary>
    public const double AccelMax = 6;

    /// <summary>基础步长（行/事件）— 基准 readScrollSpeedBase（:305-310）：CLAUDE_CODE_SCROLL_SPEED
    /// clamp (0,20] 默认 1；首事件/超窗的重置目标。</summary>
    private readonly double _base;

    /// <summary>当前乘数（基准 state.mult — 浮点累积，floor 后为行数）。</summary>
    private double _mult;

    /// <summary>上次事件时间戳（ms；null = 无先前事件 — 首事件直接重置回 base，不依赖时间原点：
    /// 基准 time=0 初值依赖墙钟远离原点的隐含假设，注入时钟从 0 起时本判定更稳健）。</summary>
    private long? _lastEventMs;

    /// <summary>时钟注入（默认墙钟；测试传可控时钟 — 对齐 ReplShell.EscClock 先例）。</summary>
    private readonly Func<long> _clock;

    /// <summary>
    /// 构造滚轮加速度跟踪器。
    /// </summary>
    /// <param name="clock">时间戳源（ms；null = Environment.TickCount64 墙钟）。</param>
    /// <param name="baseLines">基础步长（行/事件；默认 1 — 基准 readScrollSpeedBase 默认值）。</param>
    public WheelSpeedTracker(Func<long>? clock = null, double baseLines = 1)
    {
        _clock = clock ?? (static () => Environment.TickCount64);
        _base = baseLines;
        _mult = baseLines;
    }

    /// <summary>
    /// 滚轮事件 → 当前步长（行）。间隔 ≤ 40ms 的连续事件乘数 +0.3 递增（钳 cap）；
    /// 首事件/超窗重置回 base；返回 floor(乘数)（基准 computeWheelStep :255-265 —
    /// 调用方 scrollBy(floor(mult))；单发离散事件等价 base 行）。
    /// </summary>
    public int OnWheelEvent()
    {
        var now = _clock();
        if (_lastEventMs is { } last && now - last <= AccelWindowMs)
        {
            // 窗内连续 — ramp：mult += 0.3，cap = max(6, base×2)（基准 :262-263）
            var cap = Math.Max(AccelMax, _base * 2);
            _mult = Math.Min(cap, _mult + AccelStep);
        }
        else
        {
            // 首事件/超窗 — 重置回 base（基准 :259-260 gap > WHEEL_ACCEL_WINDOW_MS）
            _mult = _base;
        }

        _lastEventMs = now;
        return (int)Math.Floor(_mult);
    }

    /// <summary>
    /// 解析 CLAUDE_CODE_SCROLL_SPEED 旋钮值（基准 readScrollSpeedBase :305-310）：
    /// 缺失/空 → 1；解析失败或 ≤ 0 → 1；上限钳 20 — clamp (0,20]。数字解析对齐 parseFloat
    /// 前缀语义（"3x" → 3、跳过前导空白；指数记法不支持 — 环境旋钮实际形态为整数/小数）。
    /// 纯函数不读 env（Core 不读 env 惯例 — 宿主读环境变量传参，对齐 idleReturnThreshold 先例）。
    /// </summary>
    public static double ParseScrollSpeedBase(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return 1;
        }

        var value = ParseLeadingNumber(raw.AsSpan());
        return double.IsNaN(value) || value <= 0 ? 1 : Math.Min(value, 20);
    }

    /// <summary>parseFloat 前缀数字解析（跳过前导空白 + 可选符号 + ASCII 数字序列含至多一个小数点；
    /// 尾随垃圾与指数段截断；无有效数字前缀返回 NaN）。</summary>
    private static double ParseLeadingNumber(ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        var end = 0;
        if (end < span.Length && (span[end] == '+' || span[end] == '-'))
        {
            end++;
        }

        var start = end;
        var dotSeen = false;
        while (end < span.Length)
        {
            if (span[end] is >= '0' and <= '9')
            {
                end++;
            }
            else if (span[end] == '.' && !dotSeen)
            {
                dotSeen = true;
                end++;
            }
            else
            {
                break;
            }
        }

        // 至少一位数字才算有效前缀（"x"/"-"/"." → NaN 由调用方回退默认 1）
        var hasDigit = span[start..end].IndexOfAnyInRange('0', '9') >= 0;
        return hasDigit
            ? double.Parse(span[..end], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
            : double.NaN;
    }
}
