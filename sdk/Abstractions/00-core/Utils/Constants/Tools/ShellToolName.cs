namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Shell/PowerShell 工具名称枚举
/// </summary>
public enum ShellToolName
{
    [EnumValue("Bash")] Bash,
    [EnumValue("shell_check")] ShellCheck,
    [EnumValue("PowerShell")] Powershell,
    [EnumValue("shell_background_get")] ShellBackgroundGet,
    [EnumValue("shell_background_list")] ShellBackgroundList,
    [EnumValue("shell_background_output")] ShellBackgroundOutput,
    [EnumValue("shell_background_cancel")] ShellBackgroundCancel,
    [EnumValue("shell_background_kill_all")] ShellBackgroundKillAll,
    [EnumValue("powershell_script")] PowershellScript,
    [EnumValue("powershell_version")] PowershellVersion,
    [EnumValue("powershell_execution_policy")] PowershellExecutionPolicy,
    [EnumValue("powershell_set_execution_policy")] PowershellSetExecutionPolicy,
}
