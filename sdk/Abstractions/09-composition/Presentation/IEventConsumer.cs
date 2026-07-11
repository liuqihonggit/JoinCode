namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 事件消费策略接口 — 解耦事件处理与表示层输出
/// CLI 模式: CliEventConsumer (TerminalHelper 输出)
/// TUI 模式: TuiEventConsumer (IPresentationAdapter 输出)
/// </summary>
public interface IEventConsumer
{
    /// <summary>
    /// 收到文本内容
    /// </summary>
    void OnText(string content);

    /// <summary>
    /// 收到思考内容
    /// </summary>
    void OnThinking(string thinking);

    /// <summary>
    /// 工具调用开始
    /// </summary>
    void OnToolStart(string toolName, string? toolCallId, string? arguments);

    /// <summary>
    /// 工具调用结束
    /// </summary>
    void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch);

    /// <summary>
    /// 工具调用进度
    /// </summary>
    void OnToolProgress(string toolName, string progressType, string? progressMessage);

    /// <summary>
    /// 检测到循环输出
    /// </summary>
    void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern);

    /// <summary>
    /// 计时摘要
    /// </summary>
    void OnTimingSummary(string summary);

    /// <summary>
    /// 流式响应完成
    /// </summary>
    void OnDone(TokenUsage? usage, string? modelId);
}

/// <summary>
/// 可重置的事件消费策略 — SessionController 在每轮开始前自动调用 Reset
/// </summary>
public interface IResettableEventConsumer : IEventConsumer
{
    /// <summary>
    /// 重置内部状态（新一轮对话前调用）
    /// </summary>
    void Reset();
}
