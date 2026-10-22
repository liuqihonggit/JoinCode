namespace NativeTui.Core.Repl;

/// <summary>
/// Token 警告三态 — footer 右侧渲染分支（对齐基准 TokenWarning.tsx:166-177 两分支 +
/// 不显示态）：None = 不渲染；Ok = dim 百分比提示；Low = warning 色低位警告。
/// </summary>
public enum TokenWarningLevel : byte
{
    /// <summary>不显示（无 limit 或剩余充足 — 未达警告显示阈值）。</summary>
    None = 0,

    /// <summary>常态提示（dim：`N% 直到自动压缩` — 基准 autocompact 开分支 dim 色百分比汉化）。</summary>
    Ok = 1,

    /// <summary>低位警告（warning 色：`上下文不足 (N%) · 运行 /compact` — 基准 Context low 分支汉化）。</summary>
    Low = 2,
}

/// <summary>Token 警告渲染投影（不可变 — 快照纯派生，每次计算产出新值）。</summary>
/// <param name="Level">三态。</param>
/// <param name="PercentLeft">剩余百分比（0..100，四舍五入远离零）。</param>
/// <param name="Text">渲染文本（None 时 null）。</param>
public sealed record TokenWarningInfo(TokenWarningLevel Level, int PercentLeft, string? Text)
{
    /// <summary>无警告单例（复用避免分配 — None 是常态路径）。</summary>
    public static TokenWarningInfo None { get; } = new(TokenWarningLevel.None, 0, null);
}

/// <summary>
/// Token 用量跟踪器 — 契约 API 供 Driver 消费（S1 接线：引擎 usage 事件 → RecordUsage）：
/// input/output 逐次累计、context/limit 最新快照（null 保持原值 — 引擎部分上报容忍）、
/// 三态警告派生。阈值对齐基准 services/compact/autoCompact.ts:62-65 缓冲常量
/// （13k 自动压缩缓冲 / 20k 警告显示缓冲）与 components/TokenWarning.tsx:166-177 渲染分支：
/// percentLeft = (limit-context)/limit（max(0, round) — autoCompact.ts:108-111 的钳制/舍入语义）；
/// 显示阈值 = 自动压缩线（limit-13k）再让 20k；越过自动压缩线转 Low；无 limit（null/≤0）恒 None。
/// 纯函数化：无时钟依赖（不同于通知队列 — token 快照是事件驱动非帧拍），Warning 为快照纯派生。
/// </summary>
public sealed class TokenUsageTracker
{
    /// <summary>自动压缩缓冲（基准 AUTOCOMPACT_BUFFER_TOKENS = 13000 — limit 减得自动压缩触发线）。</summary>
    public const long AutoCompactBufferTokens = 13_000;

    /// <summary>警告显示缓冲（基准 WARNING_THRESHOLD_BUFFER_TOKENS = 20000 — 触发线再减得显示阈值）。</summary>
    public const long WarningBufferTokens = 20_000;

    /// <summary>累计输入 token（逐次 RecordUsage 求和）。</summary>
    public long InputTokens { get; private set; }

    /// <summary>累计输出 token（逐次 RecordUsage 求和）。</summary>
    public long OutputTokens { get; private set; }

    /// <summary>当前上下文占用（最新快照；null = 引擎未上报）。</summary>
    public long? ContextTokens { get; private set; }

    /// <summary>上下文窗口上限（最新快照；null = 未上报 → 警告恒 None）。</summary>
    public long? ContextLimitTokens { get; private set; }

    /// <summary>累计总 token（输入 + 输出）。</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>三态警告（快照纯派生 — FooterBar 行 2 右侧渲染源）。</summary>
    public TokenWarningInfo Warning => ComputeWarning(ContextTokens, ContextLimitTokens);

    /// <summary>
    /// 记录一次用量（Driver 契约入口）：input/output 增量累计、context/limit 覆盖快照
    /// （null = 本次未上报，保持原快照）。
    /// </summary>
    /// <param name="inputTokens">本次输入 token（增量）。</param>
    /// <param name="outputTokens">本次输出 token（增量）。</param>
    /// <param name="contextTokens">当前上下文占用（引擎权威快照；null = 未上报）。</param>
    /// <param name="contextLimitTokens">上下文窗口上限（null = 未上报）。</param>
    public void RecordUsage(long inputTokens, long outputTokens, long? contextTokens, long? contextLimitTokens)
    {
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        ContextTokens = contextTokens ?? ContextTokens;
        ContextLimitTokens = contextLimitTokens ?? ContextLimitTokens;
    }

    /// <summary>用量重置（新会话 / 手动压缩后调用方驱动）。</summary>
    public void Reset()
    {
        InputTokens = 0;
        OutputTokens = 0;
        ContextTokens = null;
        ContextLimitTokens = null;
    }

    /// <summary>
    /// 三态警告纯函数（可独立单测）：
    /// None = 无 limit（null/≤0）或 context &lt; limit-13000-20000（未达显示阈值）；
    /// Ok = 显示阈值内但未到自动压缩线 → dim `N% 直到自动压缩`；
    /// Low = 越过自动压缩线 → warning `上下文不足 (N%) · 运行 /compact`。
    /// </summary>
    /// <param name="contextTokens">上下文占用快照。</param>
    /// <param name="contextLimitTokens">窗口上限快照。</param>
    /// <returns>三态渲染投影。</returns>
    internal static TokenWarningInfo ComputeWarning(long? contextTokens, long? contextLimitTokens)
    {
        if (contextTokens is not { } context || contextLimitTokens is not { } limit || limit <= 0)
        {
            return TokenWarningInfo.None;
        }

        var percentLeft = Math.Max(0, (int)Math.Round((limit - context) * 100.0 / limit, MidpointRounding.AwayFromZero));
        var autoCompactThreshold = limit - AutoCompactBufferTokens;
        if (context < autoCompactThreshold - WarningBufferTokens)
        {
            return TokenWarningInfo.None;
        }

        return context < autoCompactThreshold
            ? new TokenWarningInfo(TokenWarningLevel.Ok, percentLeft, $"{percentLeft}% 直到自动压缩")
            : new TokenWarningInfo(TokenWarningLevel.Low, percentLeft, $"上下文不足 ({percentLeft}%) · 运行 /compact");
    }
}
