namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 内存管理工具名称枚举
/// </summary>
public enum MemoryToolName
{
    [EnumValue("memory_scan")] MemoryScan,
    [EnumValue("memory_age")] MemoryAge,
    [EnumValue("memory_cleanup")] MemoryCleanup,
    [EnumValue("memory_health")] MemoryHealth,
    [EnumValue("memory_add_team_path")] MemoryAddTeamPath,
    [EnumValue("memory_list_team_paths")] MemoryListTeamPaths,
    [EnumValue("memory_remove_team_path")] MemoryRemoveTeamPath,
    [EnumValue("memory_scan_team")] MemoryScanTeam,
    [EnumValue("memory_daily_log_append")] MemoryDailyLogAppend,
    [EnumValue("memory_daily_log_get")] MemoryDailyLogGet,
    [EnumValue("memory_search_history")] MemorySearchHistory,
    [EnumValue("memory_team_sync")] MemoryTeamSync,
    [EnumValue("memory_team_status")] MemoryTeamStatus,
}
