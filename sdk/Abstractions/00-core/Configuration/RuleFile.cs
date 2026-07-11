namespace JoinCode.Abstractions.Configuration;

public sealed record RuleFile
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public bool AlwaysApply { get; init; }
    public string Globs { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public RuleMatchStrategy MatchStrategy
    {
        get
        {
            if (AlwaysApply) return RuleMatchStrategy.Always;
            if (!string.IsNullOrEmpty(Globs)) return RuleMatchStrategy.Glob;
            if (!string.IsNullOrEmpty(Description)) return RuleMatchStrategy.Description;
            return RuleMatchStrategy.Manual;
        }
    }
}

public enum RuleMatchStrategy
{
    [EnumValue("always")] Always,
    [EnumValue("glob")] Glob,
    [EnumValue("description")] Description,
    [EnumValue("manual")] Manual
}
