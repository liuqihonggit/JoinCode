namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldResult {
    /// <summary>获取是否已折叠。</summary>
    public bool Folded { get; init; }
    /// <summary>获取头部消息数。</summary>
    public int HeadMessageCount { get; init; }
    /// <summary>获取尾部消息数。</summary>
    public int TailMessageCount { get; init; }
    /// <summary>获取原始消息数。</summary>
    public int OriginalMessageCount { get; init; }
    /// <summary>获取摘要文本。</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>获取折叠决策。</summary>
    public ContextFoldDecision Decision { get; init; }

    /// <summary>本次折叠前执行的工具结果剪裁统计（无可剪裁内容时为空）。</summary>
    public SnipStats? Snip { get; init; }

    /// <summary>获取折叠后是否仍超过紧急阈值（溢出标记）。</summary>
    /// <para>为 true 时表示折叠未能将 token 占用降到安全范围，调用方应触发降级处理
    ///（如 ReactiveCompactService 或输出警告事件），而非抛异常导致 Agent 崩溃。</para>
    public bool Overflow { get; init; }

    /// <summary>获取溢出时的 token 估算数（仅当 <see cref="Overflow"/> 为 true 时有效）。</summary>
    public int OverflowTokens { get; init; }

    /// <summary>获取上下文窗口最大 token 数（仅当 <see cref="Overflow"/> 为 true 时有效）。</summary>
    public int ContextMaxTokens { get; init; }
}