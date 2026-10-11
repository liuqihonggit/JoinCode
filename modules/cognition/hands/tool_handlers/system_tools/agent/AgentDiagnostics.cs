namespace Tools.Handlers;

/// <summary>
/// Agent 工具诊断消息构建器 — 集中管理所有结构化诊断的创建，
/// 供 AgentToolHandlers 各 partial 文件共享。
/// </summary>
internal static class AgentDiagnostics {

    /// <summary>
    /// 构建 agent_id 为空的结构化诊断。
    /// 适用于 GetAgentStatusAsync、StopAgentAsync、GetMessagesAsync 的参数验证。
    /// </summary>
    internal static ToolDiagnostic BuildAgentIdEmptyDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentIdEmpty",
            formattedMessage: "agent_id cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "agent_id"),
            ],
            suggestions:
            [
                "提供有效的代理 ID 或名称。",
            ]);
    }

    /// <summary>
    /// 构建代理未找到的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildAgentNotFoundDiagnostic(string agentId) {
        return ToolDiagnostic.Create(
            reason: "AgentNotFound",
            formattedMessage: $"Agent not found: {agentId}",
            details:
            [
                new DiagnosticDetail("AgentId", agentId),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认代理 ID 或名称拼写正确。",
            ]);
    }

    /// <summary>
    /// 构建停止代理失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildStopAgentFailedDiagnostic(string agentId) {
        return ToolDiagnostic.Create(
            reason: "AgentStopFailed",
            formattedMessage: $"Failed to stop agent or agent not found: {agentId}",
            details:
            [
                new DiagnosticDetail("AgentId", agentId),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认代理 ID 或名称拼写正确。",
                "代理可能已完成或已停止。",
            ]);
    }

    /// <summary>
    /// 构建代理协调器未初始化的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildCoordinatorNotInitializedDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentCoordinatorNotInitialized",
            formattedMessage: L.T(StringKey.AgentCoordinatorNotInitialized),
            details:
            [
                new DiagnosticDetail("Component", "IAgentService"),
            ],
            suggestions:
            [
                "确认 AgentToolHandlers 构造时传入了 IAgentService 实例。",
            ]);
    }

    /// <summary>
    /// 构建消息接收者为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildRecipientEmptyDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentRecipientEmpty",
            formattedMessage: "Recipient (to) cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "to"),
            ],
            suggestions:
            [
                "提供接收者名称、代理 ID 或 * 进行广播。",
            ]);
    }

    /// <summary>
    /// 构建消息内容为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildMessageEmptyDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentMessageEmpty",
            formattedMessage: "message cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "message"),
            ],
            suggestions:
            [
                "提供要发送的消息内容。",
            ]);
    }

    /// <summary>
    /// 构建结构化消息不支持广播的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastStructuredMessageDiagnostic(string messageType) {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastStructuredMessage",
            formattedMessage: $"Cannot broadcast structured message (type: {messageType}). Send to a specific teammate instead.",
            details:
            [
                new DiagnosticDetail("MessageType", messageType),
                new DiagnosticDetail("Recipient", "*"),
            ],
            suggestions:
            [
                "将结构化消息发送给特定的 teammate，而非使用 * 广播。",
            ]);
    }

    /// <summary>
    /// 构建发送消息失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildSendMessageFailedDiagnostic(string recipient) {
        return ToolDiagnostic.Create(
            reason: "AgentSendMessageFailed",
            formattedMessage: $"Failed to send message: agent '{recipient}' not found or messaging service unavailable",
            details:
            [
                new DiagnosticDetail("Recipient", recipient),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认接收者名称或代理 ID 正确。",
                "检查消息服务是否可用。",
            ]);
    }

    /// <summary>
    /// 构建广播服务不可用的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastServiceUnavailableDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastServiceUnavailable",
            formattedMessage: "Broadcast failed: team service not available",
            details:
            [
                new DiagnosticDetail("Component", "ITeamManager"),
            ],
            suggestions:
            [
                "确认 ITeamManager 已在 DI 容器中注册。",
            ]);
    }

    /// <summary>
    /// 构建广播无团队的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastNoTeamsDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastNoTeams",
            formattedMessage: "Broadcast failed: no teams exist",
            details: [],
            suggestions:
            [
                "先创建团队后再发送广播消息。",
            ]);
    }
}
