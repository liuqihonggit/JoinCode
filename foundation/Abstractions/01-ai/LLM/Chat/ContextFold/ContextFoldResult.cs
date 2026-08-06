namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldResult
{
    public bool Folded { get; init; }
    public int HeadMessageCount { get; init; }
    public int TailMessageCount { get; init; }
    public int OriginalMessageCount { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ContextFoldDecision Decision { get; init; }

    /// <summary>本次折叠前执行的工具结果剪裁统计（无可剪裁内容时为空）。</summary>
    public SnipStats? Snip { get; init; }
}
