namespace NativeTui.Core.Repl;

/// <summary>
/// 滚轮加速度 ramp 跟踪器（TASK066 板块 C P3 收口 + 板块 E 收尾弹跳检测）。
/// 基准 computeWheelStep native 路径（ScrollKeybindingHandler.tsx:176-265）：
/// 方向翻转挂起 + 弹跳判定窗（:177-217 — 翻转事件 defer 返回 0 行不滚动，
/// 下一事件 200ms 窗内翻回原方向 = 编码器弹跳吞掉继续原方向 ramp；持续新方向或
/// 超窗 = 真翻转提交重置回 base）+ TRACKPAD/HI-RES 段（:255-265 — 40ms 判定窗内
/// 连续滚轮事件按 STEP=0.3 递增浮点乘数、首事件/超窗重置回 base、
/// cap = max(6, base×2)（:262）、返回 floor(乘数) — 连发 floor 序列 1,1,1,1,2,2,2,3,...
///（第 5 事件起 2 行；乘数非整数计数而是浮点累积）。
/// 常量对齐基准 :47-49/:65（WHEEL_ACCEL_WINDOW_MS=40 / WHEEL_ACCEL_STEP=0.3 /
/// WHEEL_ACCEL_MAX=6 / WHEEL_BOUNCE_GAP_MAX_MS=200）。
/// 基准的 wheelMode 衰减曲线/burst 计数/idle disengage（:182-186/:222-253 鼠标
/// 编码器专用加速通道 — 弹跳确认后切换的粘滞鼠标模式）不在收口范围：w2 无
/// wheelMode 概念，弹跳确认事件直接落 TRACKPAD ramp（基准 :201-205 弹跳确认后
/// 同样继续 ramp 累积 — bounce IS a physical click 计入 cadence）。
/// 时钟经构造注入（对齐 ReplShell.EscClock 先例 — 测试可控时间戳）；
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

    /// <summary>弹跳判定窗（ms）— 基准 WHEEL_BOUNCE_GAP_MAX_MS（ScrollKeybindingHandler.tsx:65）：
    /// 翻转事件挂起后，翻回原方向事件须在 ≤ 窗内到达才算编码器弹跳（翻转+翻回的
    /// 噪声模式）；超窗按真翻转提交重置。磨损/廉价光电编码器快速旋转时发反向
    /// 假刻度（实测 28% 事件率 :51-54），模式恒为翻转后翻回。</summary>
    public const long BounceGapMaxMs = 200;

    /// <summary>基础步长（行/事件）— 基准 readScrollSpeedBase（:305-310）：CLAUDE_CODE_SCROLL_SPEED
    /// clamp (0,20] 默认 1；首事件/超窗/真翻转提交的重置目标。</summary>
    private readonly double _base;

    /// <summary>当前乘数（基准 state.mult — 浮点累积，floor 后为行数）。</summary>
    private double _mult;

    /// <summary>上次事件时间戳（ms；null = 无先前事件 — 首事件直接重置回 base，不依赖时间原点：
    /// 基准 time=0 初值依赖墙钟远离原点的隐含假设，注入时钟从 0 起时本判定更稳健）。</summary>
    private long? _lastEventMs;

    /// <summary>上次事件方向（true=下滚；null = 无先前事件 — 首事件不算翻转，
    /// 对齐基准 state.dir !== 0 首事件守卫 :208）。</summary>
    private bool? _lastDir;

    /// <summary>挂起翻转标记（基准 state.pendingFlip :154-159 — 翻转方向与时间戳可由
    /// _lastDir 取反 + _lastEventMs 推导，仅存标记；翻转 defer 期间 dir/mult 不动）。</summary>
    private bool _pendingFlip;

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
    /// 滚轮事件 → 当前步长（行）。带方向弹跳检测（基准 computeWheelStep :177-217 +
    /// TRACKPAD 段 :255-265）：方向翻转事件挂起返回 0（调用方 ScrollBy(0) no-op —
    /// 基准 scrollBy(0) 同为无操作）；下一事件窗内（≤200ms）翻回原方向 = 编码器
    /// 弹跳吞掉（dir/mult 保持翻转前状态，本事件继续窗内 ramp 累积 — 基准 :201-205
    /// bounce IS a physical click 计入 cadence）；持续新方向或超窗 = 真翻转提交
    ///（dir 更新 + mult 重置回 base，返回 base 行 — 基准 :196-199）。同向事件：
    /// 间隔 ≤ 40ms 乘数 +0.3 递增（钳 cap）；首事件/超窗重置回 base；返回 floor(乘数)。
    /// </summary>
    /// <param name="down">方向（true = 下滚；false = 上滚）。</param>
    /// <returns>本次滚动行数（0 = 翻转挂起不滚动 — 调用方 no-op）。</returns>
    public int OnWheelEvent(bool down)
    {
        var now = _clock();

        // 挂起翻转解析（基准 :191-206）— 先于 gap/dir 状态刷新：需要翻转前 _lastDir
        // 区分弹跳（翻回原方向）与真翻转（持续新方向），_lastEventMs（翻转时间戳）做窗判定
        if (_pendingFlip)
        {
            _pendingFlip = false;
            if (_lastDir != down || now - _lastEventMs > BounceGapMaxMs)
            {
                // 真翻转提交（基准 :196-199）：新方向持续，或翻回原方向但超窗（两连真实改向）。
                // 方向更新 + mult 重置回 base + 返回 base 行（挂起事件丢失 1 行延迟 — 可接受）
                _lastDir = down;
                _lastEventMs = now;
                _mult = _base;
                return (int)Math.Floor(_mult);
            }

            // 弹跳确认（基准 :201-205）：窗内翻回原方向 — 翻转吞掉（dir/mult 保持翻转前
            // 状态，_lastEventMs 已在挂起时推进到翻转时刻），本事件继续走下方同向 ramp
        }

        var gap = _lastEventMs is { } last ? now - last : long.MaxValue;
        if (_lastDir is { } dir && down != dir)
        {
            // 方向翻转挂起（基准 :208-217）：本事件不滚动（返回 0），只推进时间戳
            //（弹跳判定窗锚点 — 若为弹跳，确认事件 gap = 翻转间隔反映真实刻度节奏）；
            // dir/mult 不动 — 下一事件解析弹跳 vs 真翻转
            _pendingFlip = true;
            _lastEventMs = now;
            return 0;
        }

        _lastDir = down;
        _lastEventMs = now;

        // ─── TRACKPAD / HI-RES 段（基准 :255-265）───
        if (gap > AccelWindowMs)
        {
            // 首事件/超窗 — 重置回 base（基准 :259-260 gap > WHEEL_ACCEL_WINDOW_MS）
            _mult = _base;
        }
        else
        {
            // 窗内连续 — ramp：mult += 0.3，cap = max(6, base×2)（基准 :262-263）
            var cap = Math.Max(AccelMax, _base * 2);
            _mult = Math.Min(cap, _mult + AccelStep);
        }

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
