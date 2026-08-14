namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 文件操作工具名称枚举
/// </summary>
public enum FileToolName
{
    [EnumValue("Read")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    FileRead,

    [EnumValue("Write")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanDenied = true, AskAllowed = true)]
    FileWrite,

    [EnumValue("Edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanDenied = true, AskAllowed = true)]
    FileEdit,

    [EnumValue("file_edit_regex")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileEditRegex,

    [EnumValue("file_insert_lines")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileInsertLines,

    [EnumValue("file_delete_lines")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileDeleteLines,

    [EnumValue("file_batch_edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileBatchEdit,

    [EnumValue("file_delete")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    FileDelete,

    [EnumValue("file_move")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    FileMove,

    [EnumValue("directory_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    DirectoryList,

    [EnumValue("file_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    FileList,

    [EnumValue("file_snip_lines")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileSnipLines,

    [EnumValue("file_snip_preview")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    FileSnipPreview,

    [EnumValue("apply_patch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    FileApplyPatch,
}
