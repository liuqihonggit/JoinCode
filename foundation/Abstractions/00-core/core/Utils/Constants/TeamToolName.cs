namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Team 工具名称枚举
/// </summary>
public enum TeamToolName
{
    [EnumValue("TeamCreate")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamCreate,

    [EnumValue("TeamDelete")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    TeamDelete,

    [EnumValue("team_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TeamGet,

    [EnumValue("team_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TeamList,

    [EnumValue("team_add_member")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamAddMember,

    [EnumValue("team_remove_member")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamRemoveMember,

    [EnumValue("team_send_message")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamSendMessage,

    [EnumValue("team_send_direct_message")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamSendDirectMessage,

    [EnumValue("team_broadcast")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TeamBroadcast,

    [EnumValue("team_get_messages")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TeamGetMessages,
}
