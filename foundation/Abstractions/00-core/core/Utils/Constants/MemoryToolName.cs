namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 内存管理工具名称枚举
/// </summary>
public enum MemoryToolName
{
    [EnumValue("memory_scan")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryScan,

    [EnumValue("memory_age")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryAge,

    [EnumValue("memory_cleanup")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    MemoryCleanup,

    [EnumValue("memory_health")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryHealth,

    [EnumValue("memory_add_team_path")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    MemoryAddTeamPath,

    [EnumValue("memory_list_team_paths")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryListTeamPaths,

    [EnumValue("memory_remove_team_path")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    MemoryRemoveTeamPath,

    [EnumValue("memory_scan_team")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryScanTeam,

    [EnumValue("memory_daily_log_append")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    MemoryDailyLogAppend,

    [EnumValue("memory_daily_log_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryDailyLogGet,

    [EnumValue("memory_search_history")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemorySearchHistory,

    [EnumValue("memory_team_sync")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    MemoryTeamSync,

    [EnumValue("memory_team_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MemoryTeamStatus,
}
