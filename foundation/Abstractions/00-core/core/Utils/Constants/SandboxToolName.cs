namespace JoinCode.Abstractions.Utils;

public enum SandboxToolName
{
    [EnumValue("sandbox_enter")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SandboxEnter,

    [EnumValue("sandbox_exit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SandboxExit,

    [EnumValue("sandbox_switch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SandboxSwitch,

    [EnumValue("sandbox_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SandboxStatus,

    [EnumValue("sandbox_exec")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    SandboxExec,

    [EnumValue("sandbox_exec_continue")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    SandboxExecContinue,
}
