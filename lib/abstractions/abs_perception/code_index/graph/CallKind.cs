namespace JoinCode.Abstractions.CodeIndex;

public enum CallKind
{
    [EnumValue("direct")]
    Direct,
    [EnumValue("virtual")]
    Virtual,
    [EnumValue("static")]
    Static,
    [EnumValue("constructor")]
    Constructor,
    [EnumValue("event_handler")]
    EventHandler
}
