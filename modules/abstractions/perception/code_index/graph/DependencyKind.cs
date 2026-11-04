namespace JoinCode.Abstractions.CodeIndex;

public enum DependencyKind {
    [EnumValue("inherits")]
    Inherits,
    [EnumValue("implements")]
    Implements,
    [EnumValue("uses")]
    Uses,
    [EnumValue("imports")]
    Imports,
    [EnumValue("contains")]
    Contains
}