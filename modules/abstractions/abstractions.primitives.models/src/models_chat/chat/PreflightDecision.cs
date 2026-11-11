namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PreflightDecision {
    /// <summary>获取是否需要执行动作。</summary>
    public bool NeedsAction { get; init; }
    /// <summary>获取预估占用比率。</summary>
    public double EstimatedRatio { get; init; }
}
