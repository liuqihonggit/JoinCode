namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 消息可见性 — 控制消息投递范围，对标 QQ 系统消息/私信/撤回 — ADR 0109 决策8。
/// </summary>
public enum MessageVisibility
{
    /// <summary>所有人可见（普通聊天消息）</summary>
    [EnumValue("public")] Public = 0,

    /// <summary>系统通知（"xxx 加入"/"xxx 退出"），所有人可见但样式区分</summary>
    [EnumValue("system")] System = 1,

    /// <summary>仅管理员/群主可见（"xxx 被禁言"/"xxx 踢出"操作日志）</summary>
    [EnumValue("admin_only")] AdminOnly = 2,

    /// <summary>私信（仅发送者+接收者可见）</summary>
    [EnumValue("private")] Private = 3,

    /// <summary>隐藏（撤回的消息、被过滤的消息，仅持久化不投递）</summary>
    [EnumValue("hidden")] Hidden = 4,
}
