namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 统一消息模型 — 归纳原 MailboxMessage 与 CoordinatorMessage，消除两套定义。
/// <para>文件邮箱层用 MessageId/SessionId/IsRead 管理持久化与已读状态。</para>
/// <para>进程内邮箱层用 StructuredType/RequestId/Payload 表达结构化语义。</para>
/// <para>差异字段按需使用，LINQ 投影提取所需子集。</para>
/// </summary>
public sealed class CoordinatorMessage
{
    /// <summary>消息唯一标识 — 自动生成 GUID，跨进程去重的唯一依据</summary>
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public required string FromAgentId { get; init; }
    public required string ToAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>会话标识 — 文件邮箱层用于定位 JSONL 文件，进程内邮箱层可为 null</summary>
    public string? SessionId { get; init; }

    /// <summary>是否已读 — 文件邮箱层标记消息已读状态</summary>
    public bool IsRead { get; set; }

    /// <summary>结构化消息类型 — 进程内邮箱层用于路由结构化消息</summary>
    public TeammateMessageType? StructuredType { get; init; }

    /// <summary>请求标识 — 进程内邮箱层关联请求-响应对</summary>
    public string? RequestId { get; init; }

    /// <summary>结构化负载 — 进程内邮箱层携带键值对负载</summary>
    public Dictionary<string, JsonElement> Payload { get; init; } = [];

    /// <summary>是否结构化消息 — StructuredType 非 null 时为 true</summary>
    public bool IsStructured => StructuredType is not null;

    /// <summary>消息可见性 — 控制跨通道路由范围，对标 QQ 系统消息/私信/撤回 — ADR 0111 决策8。</summary>
    public MessageVisibility Visibility { get; init; } = MessageVisibility.Public;
}
