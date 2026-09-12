namespace JoinCode.Abstractions.Security.Sandbox;

[JsonConverter(typeof(JsonStringEnumConverter<SandboxExecutionTimeout>))]
public enum SandboxExecutionTimeout
{
    [EnumValue("2min")] TwoMinutes,
    [EnumValue("4min")] FourMinutes,
    [EnumValue("8min")] EightMinutes,
    [EnumValue("custom")] Custom
}
