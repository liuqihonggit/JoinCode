namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldThresholds
{
    public double FoldThreshold { get; init; } = 0.5;
    public double AggressiveThreshold { get; init; } = 0.7;
    public double ForceSummaryThreshold { get; init; } = 0.8;
    public double EmergencyThreshold { get; init; } = 0.95;
    public double TailFraction { get; init; } = 0.2;
    public double AggressiveTailFraction { get; init; } = 0.1;
    public double MinSavingsFraction { get; init; } = 0.3;
    public int CharsPerToken { get; init; } = 4;
    public int DeferFoldLimit { get; init; } = 3;
    public int StuckFoldLimit { get; init; } = 2;
    public int MinSnipChars { get; init; } = 1024;
    public int SnipHeadLines { get; init; } = 40;
    public int SnipTailLines { get; init; } = 40;
    public int SnipHeadChars { get; init; } = 8000;
    public int SnipTailChars { get; init; } = 8000;

    /// <summary>
    /// 剪裁时保护区的最小消息数兜底 — 对齐 Reasonix Go 版 tailStart 的 minKeep/tailFloor。
    /// 当末条消息单独超预算导致 ComputeTailBoundary 归零时，仍保留最近 N 条消息逐字，
    /// 更早的过期大工具结果允许剪裁，避免"末条巨大→前面永不再剪"。
    /// </summary>
    public int RecentKeepTailMessages { get; init; } = 2;

    public static ContextFoldThresholds Default { get; } = new();
}
