namespace JoinCode.Abstractions.Brain.Context.Resolution;

public enum ReferenceMatchType
{
    [EnumValue("exact")] Exact,

    [EnumValue("pattern")] Pattern,

    [EnumValue("fuzzy")] Fuzzy,

    [EnumValue("partial")] Partial
}
