namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 一次工具结果剪裁（snip）维护的结果 — 对齐 Reasonix Go 版 PruneStats。
/// 剪裁是"免费"的上下文管理：过期的工具结果可重派生（文件可重读、命令可重跑），
/// 重写它们无需调用摘要器、也不会丢弃任何消息。
/// </summary>
public sealed record SnipStats
{
    /// <summary>被剪裁的工具结果条数。</summary>
    public int Results { get; init; }

    /// <summary>通过剪裁节省的字符数。</summary>
    public int SavedChars { get; init; }
}
