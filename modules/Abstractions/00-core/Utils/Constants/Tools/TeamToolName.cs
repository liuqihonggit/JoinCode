namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Team 工具名称枚举
/// </summary>
public enum TeamToolName
{
    [EnumValue("TeamCreate")] TeamCreate,
    [EnumValue("TeamDelete")] TeamDelete,
    [EnumValue("team_get")] TeamGet,
    [EnumValue("team_list")] TeamList,
    [EnumValue("team_add_member")] TeamAddMember,
    [EnumValue("team_remove_member")] TeamRemoveMember,
    [EnumValue("team_send_message")] TeamSendMessage,
    [EnumValue("team_send_direct_message")] TeamSendDirectMessage,
    [EnumValue("team_broadcast")] TeamBroadcast,
    [EnumValue("team_get_messages")] TeamGetMessages,
}
