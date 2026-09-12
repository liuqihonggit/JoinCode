namespace JoinCode.Abstractions.Security.Sandbox;

[JsonConverter(typeof(JsonStringEnumConverter<SandboxType>))]
public enum SandboxType
{
    [EnumValue("none")] None,
    [EnumValue("soft")] Soft,
    [EnumValue("process")] Process,
    [EnumValue("docker")] Docker,
    [EnumValue("bubblewrap")] Bubblewrap
}
