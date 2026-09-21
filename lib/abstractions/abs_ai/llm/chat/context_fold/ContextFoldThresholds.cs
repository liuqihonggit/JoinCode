namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldThresholds {
    /// <summary>获取或设置触发折叠的阈值比例。</summary>
    public double FoldThreshold { get; init; } = 0.5;
    /// <summary>获取或设置触发激进折叠的阈值比例。</summary>
    public double AggressiveThreshold { get; init; } = 0.7;
    /// <summary>获取或设置强制摘要的阈值比例。</summary>
    public double ForceSummaryThreshold { get; init; } = 0.8;
    /// <summary>获取或设置紧急折叠阈值比例。</summary>
    public double EmergencyThreshold { get; init; } = 0.95;
    /// <summary>获取或设置保留尾部消息的比例。</summary>
    public double TailFraction { get; init; } = 0.2;
    /// <summary>获取或设置激进模式下保留尾部消息的比例。</summary>
    public double AggressiveTailFraction { get; init; } = 0.1;
    /// <summary>获取或设置最小节省比例。</summary>
    public double MinSavingsFraction { get; init; } = 0.3;
    /// <summary>获取或设置每令牌字符数估算值。</summary>
    public int CharsPerToken { get; init; } = 4;
    /// <summary>获取或设置延迟折叠的次数上限。</summary>
    public int DeferFoldLimit { get; init; } = 3;
    /// <summary>获取或设置卡顿时折叠的次数上限。</summary>
    public int StuckFoldLimit { get; init; } = 2;
    /// <summary>获取或设置可剪裁的最小字符数。</summary>
    public int MinSnipChars { get; init; } = 1024;
    /// <summary>获取或设置剪裁保留的头部行数。</summary>
    public int SnipHeadLines { get; init; } = 40;
    /// <summary>获取或设置剪裁保留的尾部行数。</summary>
    public int SnipTailLines { get; init; } = 40;
    /// <summary>获取或设置剪裁保留的头部字符数。</summary>
    public int SnipHeadChars { get; init; } = 8000;
    /// <summary>获取或设置剪裁保留的尾部字符数。</summary>
    public int SnipTailChars { get; init; } = 8000;

    /// <summary>
    /// 剪裁时保护区的最小消息数兜底 — 对齐 Reasonix Go 版 tailStart 的 minKeep/tailFloor。
    /// 当末条消息单独超预算导致 ComputeTailBoundary 归零时，仍保留最近 N 条消息逐字，
    /// 更早的过期大工具结果允许剪裁，避免"末条巨大→前面永不再剪"。
    /// </summary>
    public int RecentKeepTailMessages { get; init; } = 2;

    /// <summary>
    /// L4 prune 保护阈值（token）— 保护最近此 token 数的消息不被 prune，对齐 openCode prune protect 40k。
    /// </summary>
    public int PruneProtectTokens { get; init; } = 40_000;

    /// <summary>
    /// L4 prune 门槛（token）— 只 prune 超过此 token 数的过期工具结果，对齐 openCode prune minimum 20k。
    /// </summary>
    public int PruneMinimumTokens { get; init; } = 20_000;

    /// <summary>获取默认阈值实例。</summary>
    public static ContextFoldThresholds Default { get; } = new();
}