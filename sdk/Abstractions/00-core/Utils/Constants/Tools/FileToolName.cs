namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 文件操作工具名称枚举
/// </summary>
public enum FileToolName
{
    [EnumValue("Read")] FileRead,
    [EnumValue("Write")] FileWrite,
    [EnumValue("Edit")] FileEdit,
    [EnumValue("file_edit_regex")] FileEditRegex,
    [EnumValue("file_insert_lines")] FileInsertLines,
    [EnumValue("file_delete_lines")] FileDeleteLines,
    [EnumValue("file_batch_edit")] FileBatchEdit,
    [EnumValue("file_delete")] FileDelete,
    [EnumValue("file_move")] FileMove,
    [EnumValue("directory_list")] DirectoryList,
    [EnumValue("file_list")] FileList,
    [EnumValue("file_snip_lines")] FileSnipLines,
    [EnumValue("file_snip_preview")] FileSnipPreview,
}
