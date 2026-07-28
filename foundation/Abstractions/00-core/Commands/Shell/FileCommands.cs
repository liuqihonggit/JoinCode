
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 文件读取命令
/// </summary>
public sealed record FileReadCommand(
    [Required(ErrorMessage = "file_path 不能为空")]
    [StringLength(4096, ErrorMessage = "文件路径过长")]
    string FilePath,
    int? Offset = null,
    int? Limit = null);

/// <summary>
/// 文件写入命令
/// </summary>
public sealed record FileWriteCommand(
    [Required(ErrorMessage = "file_path 不能为空")]
    [StringLength(4096, ErrorMessage = "文件路径过长")]
    string FilePath,
    string Content);

/// <summary>
/// 文件编辑命令
/// </summary>
public sealed record FileEditCommand(
    [Required(ErrorMessage = "file_path 不能为空")]
    [StringLength(4096, ErrorMessage = "文件路径过长")]
    string FilePath,
    [Required(ErrorMessage = "old_string 不能为空")]
    string OldString,
    string NewString,
    bool ReplaceAll = false);

/// <summary>
/// 文件删除命令
/// </summary>
public sealed record FileDeleteCommand(
    [Required(ErrorMessage = "file_path 不能为空")]
    [StringLength(4096, ErrorMessage = "文件路径过长")]
    string FilePath);

/// <summary>
/// 目录列表命令
/// </summary>
public sealed record DirectoryListCommand(
    [Required(ErrorMessage = "directory_path 不能为空")]
    [StringLength(4096, ErrorMessage = "目录路径过长")]
    string DirectoryPath,
    bool Recursive = false);
