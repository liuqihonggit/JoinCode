namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 事件消费策略接口 — 解耦事件处理与表示层输出
/// CLI 模式: CliEventConsumer (TerminalHelper 输出)
/// TUI 模式: TuiEventConsumer (IPresentationAdapter 输出)
/// </summary>
public interface IEventConsumer {
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

    /// <summary>
    /// 子代理启动（DSG041 批1 — 协议本体先扩，投影层后跟；默认空实现 — 既有实现方零改动，
    /// 默认实现须经接口类型调用才可见）
    /// </summary>
    /// <param name="subagentId">子代理标识（事件路由键，分组渲染 AddAgent 键）</param>
    /// <param name="agentType">代理类型（组头同型拼接 + 八色取色键）</param>
    /// <param name="description">任务描述（状态行括号段）</param>
    void OnSubagentStarted(string subagentId, string agentType, string description) { }

    /// <summary>
    /// 子代理工具活动（DSG041 批1 — Started 与 Finished 间的过程事件；默认空实现）
    /// </summary>
    /// <param name="subagentId">子代理标识</param>
    /// <param name="toolName">工具名</param>
    /// <param name="summary">活动摘要（参数/结果/进度文本）</param>
    /// <param name="tokenGain">令牌增量（批2 通道接通后填充真实值；批1 恒 0）</param>
    void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) { }

    /// <summary>
    /// 子代理完成（DSG041 批1 — 终态定格；默认空实现）
    /// </summary>
    /// <param name="subagentId">子代理标识</param>
    /// <param name="isError">是否失败</param>
    /// <param name="tokenTotal">子代理总令牌消耗（TASK062 P7 — AgentFinished 事件 Usage.TotalTokens 权威值；
    /// 完成态一次性校正优于逐活动增量 — 引擎数据流中 token 仅回合/完成粒度存在（RecordTokenUsage 亦完成时调用）；
    /// 0 = 无 usage 数据（FinalizePending 等兜底路径），消费方保持原累计值）</param>
    void OnSubagentFinished(string subagentId, bool isError, int tokenTotal) { }
}

/// <summary>
/// 可重置的事件消费策略 — SessionController 在每轮开始前自动调用 Reset
/// </summary>
public interface IResettableEventConsumer : IEventConsumer {
    /// <summary>
    /// 重置内部状态（新一轮对话前调用）
    /// </summary>
    void Reset();
}