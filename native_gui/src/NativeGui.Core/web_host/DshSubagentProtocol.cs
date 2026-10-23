namespace JoinCode.Gui.WebHost;

// 子代理交互 DTO（TASK066 C6）：subagents/prompt、subagents/interruptByParent 与 skills/list。
// 线上形状以 dsh-subagent / dsh-api-session-controller 的 typert 描述符为事实源。

/// <summary>subagents/prompt 参数体（wire=request）</summary>
internal sealed class DshSubagentPromptRequest {
    /// <summary>调用方请求 ID（回显为 messageId）</summary>
    public string RequestId { get; set; } = "";
    /// <summary>父会话 ID</summary>
    public string ParentSessionId { get; set; } = "";
    /// <summary>子会话 ID</summary>
    public string ChildSessionId { get; set; } = "";
    /// <summary>投递模式（协议固定 continuable）</summary>
    public string Mode { get; set; } = "continuable";
    /// <summary>投递语义（queue / steer）</summary>
    public string Delivery { get; set; } = "queue";
    /// <summary>内容块</summary>
    public List<DshSubagentContentPart> Content { get; set; } = [];
    /// <summary>客户端时区（可选，宿主忽略）</summary>
    public string? ClientTimeZone { get; set; }
}

/// <summary>subagents/prompt 内容块（目前只消费 text）</summary>
internal sealed class DshSubagentContentPart {
    /// <summary>块类型（text / image）</summary>
    public string Type { get; set; } = "";
    /// <summary>文本正文</summary>
    public string? Text { get; set; }
}

/// <summary>subagents/prompt 结果</summary>
internal sealed class DshSubagentPromptReceipt {
    /// <summary>消息 ID（宿主以 requestId 回显，便于前端对齐乐观条目）</summary>
    public string MessageId { get; set; } = "";
}

/// <summary>subagents/interruptByParent 参数</summary>
internal sealed class DshSubagentInterruptArgs {
    /// <summary>子会话 ID</summary>
    public string ChildSessionId { get; set; } = "";
    /// <summary>父会话 ID</summary>
    public string ParentSessionId { get; set; } = "";
    /// <summary>中断模式（协议固定 continuable）</summary>
    public string Mode { get; set; } = "continuable";
}

/// <summary>subagents/interruptByParent 结果</summary>
internal sealed class DshSubagentInterruptReceipt {
    /// <summary>已受理</summary>
    public bool Accepted { get; set; } = true;
}

/// <summary>skills/list 参数体（wire=request）</summary>
internal sealed class DshSkillListArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>plan/mode 事件数据（前端计划模式指示器）</summary>
internal sealed class DshPlanModeData {
    /// <summary>是否处于计划模式</summary>
    public bool Active { get; set; }
}
