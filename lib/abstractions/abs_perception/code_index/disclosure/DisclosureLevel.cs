namespace JoinCode.Abstractions.CodeIndex;

public enum DisclosureLevel {
    [EnumValue("index")]
    Index = 0,
    [EnumValue("relationships")]
    Relationships = 1,
    [EnumValue("source")]
    Source = 2
}