namespace JoinCode.Abstractions.Security.Sandbox;

[JsonConverter(typeof(JsonStringEnumConverter<SandboxExecutionState>))]
public enum SandboxExecutionState
{
    [EnumValue("running")] Running,
    [EnumValue("completed")] Completed,
    [EnumValue("timed_out")] TimedOut,
    [EnumValue("force_stopped")] ForceStopped,
    [EnumValue("failed")] Failed
}
