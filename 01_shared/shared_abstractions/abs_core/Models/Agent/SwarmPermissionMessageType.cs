namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Swarm 权限消息类型枚举 — 替代原 SwarmPermissionMessageTypes 静态常量类
/// </summary>
public enum SwarmPermissionMessageType
{
    [EnumValue("permission_request")] PermissionRequest,
    [EnumValue("permission_response")] PermissionResponse,
}
