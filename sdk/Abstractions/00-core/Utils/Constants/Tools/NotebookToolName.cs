namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Notebook 工具名称枚举
/// </summary>
public enum NotebookToolName
{
    [EnumValue("NotebookEdit")] NotebookEdit,
    [EnumValue("notebook_create")] NotebookCreate,
    [EnumValue("notebook_read")] NotebookRead,
    [EnumValue("notebook_add_cell")] NotebookAddCell,
    [EnumValue("notebook_delete_cell")] NotebookDeleteCell,
    [EnumValue("notebook_edit_cell")] NotebookEditCell,
    [EnumValue("notebook_move_cell")] NotebookMoveCell,
    [EnumValue("notebook_change_cell_type")] NotebookChangeCellType,
    [EnumValue("notebook_clear_outputs")] NotebookClearOutputs,
    [EnumValue("notebook_get_cell")] NotebookGetCell,
}
