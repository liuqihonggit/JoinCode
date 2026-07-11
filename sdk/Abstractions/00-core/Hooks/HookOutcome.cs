namespace JoinCode.Abstractions.Hooks;

public enum HookOutcome
{
    [EnumValue("success")] Success,
    [EnumValue("blocking")] Blocking,
    [EnumValue("nonBlockingError")] NonBlockingError,
    [EnumValue("cancelled")] Cancelled
}
