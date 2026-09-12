namespace JoinCode.Abstractions.Security.Sandbox;

[JsonConverter(typeof(JsonStringEnumConverter<SandboxHealthState>))]
public enum SandboxHealthState
{
    [EnumValue("healthy")] Healthy,
    [EnumValue("fallback")] Fallback,
    [EnumValue("degraded")] Degraded,
    [EnumValue("unresponsive")] Unresponsive
}
