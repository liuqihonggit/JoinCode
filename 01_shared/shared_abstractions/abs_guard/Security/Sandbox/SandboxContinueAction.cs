namespace JoinCode.Abstractions.Security.Sandbox;

[JsonConverter(typeof(JsonStringEnumConverter<SandboxContinueAction>))]
public enum SandboxContinueAction
{
    [EnumValue("wait")] Wait,
    [EnumValue("stop")] Stop
}
