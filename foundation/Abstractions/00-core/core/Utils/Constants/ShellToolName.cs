namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Shell/PowerShell 工具名称枚举
/// </summary>
public enum ShellToolName
{
    [EnumValue("Bash")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    Bash,

    [EnumValue("shell_check")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ShellCheck,

    [EnumValue("PowerShell")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    Powershell,

    [EnumValue("shell_background_get")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ShellBackgroundGet,

    [EnumValue("shell_background_list")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ShellBackgroundList,

    [EnumValue("shell_background_output")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ShellBackgroundOutput,

    [EnumValue("shell_background_cancel")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ShellBackgroundCancel,

    [EnumValue("shell_background_kill_all")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    ShellBackgroundKillAll,

    [EnumValue("powershell_script")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    PowershellScript,

    [EnumValue("powershell_version")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PowershellVersion,

    [EnumValue("powershell_execution_policy")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PowershellExecutionPolicy,

    [EnumValue("powershell_set_execution_policy")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    PowershellSetExecutionPolicy,
}
