namespace JoinCode.Abstractions.Utils;

public enum SandboxToolName
{
    [EnumValue("sandbox_enter")] SandboxEnter,
    [EnumValue("sandbox_exit")] SandboxExit,
    [EnumValue("sandbox_switch")] SandboxSwitch,
    [EnumValue("sandbox_status")] SandboxStatus,
    [EnumValue("sandbox_exec")] SandboxExec,
    [EnumValue("sandbox_exec_continue")] SandboxExecContinue,
}
