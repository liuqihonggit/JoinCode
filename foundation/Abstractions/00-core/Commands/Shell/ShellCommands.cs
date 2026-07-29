
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// Shell 命令执行
/// </summary>
public sealed record ShellExecuteCommand(
    [Required(ErrorMessage = "command 不能为空")]
    [StringLength(8192, ErrorMessage = "命令过长")]
    string Command,
    [Range(1000, 300000, ErrorMessage = "超时时间必须在 1000-300000ms 之间")]
    int? Timeout = null,
    [StringLength(4096, ErrorMessage = "工作目录路径过长")]
    string? WorkingDirectory = null);

/// <summary>
/// PowerShell 命令执行
/// </summary>
public sealed record PowerShellExecuteCommand(
    [Required(ErrorMessage = "command 不能为空")]
    [StringLength(8192, ErrorMessage = "命令过长")]
    string Command,
    [Range(1000, 300000, ErrorMessage = "超时时间必须在 1000-300000ms 之间")]
    int? Timeout = null,
    [StringLength(4096, ErrorMessage = "工作目录路径过长")]
    string? WorkingDirectory = null);
