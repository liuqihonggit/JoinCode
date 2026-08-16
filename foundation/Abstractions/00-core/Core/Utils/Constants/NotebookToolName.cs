namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Notebook 工具名称枚举
/// </summary>
public enum NotebookToolName
{
    [EnumValue("NotebookEdit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookEdit,

    [EnumValue("notebook_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookCreate,

    [EnumValue("notebook_read")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    NotebookRead,

    [EnumValue("notebook_add_cell")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookAddCell,

    [EnumValue("notebook_delete_cell")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookDeleteCell,

    [EnumValue("notebook_edit_cell")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookEditCell,

    [EnumValue("notebook_move_cell")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookMoveCell,

    [EnumValue("notebook_change_cell_type")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookChangeCellType,

    [EnumValue("notebook_clear_outputs")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    NotebookClearOutputs,

    [EnumValue("notebook_get_cell")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    NotebookGetCell,
}
