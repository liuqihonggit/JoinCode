namespace JoinCode.Abstractions.Configuration;

/// <summary>规则文件。</summary>
public sealed record RuleFile {
    /// <summary>获取规则名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取规则内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取源文件路径。</summary>
    public string SourcePath { get; init; } = string.Empty;
    /// <summary>获取是否总是应用。</summary>
    public bool AlwaysApply { get; init; }
    /// <summary>获取 glob 匹配模式。</summary>
    public string Globs { get; init; } = string.Empty;
    /// <summary>获取规则描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取匹配策略。</summary>
    public RuleMatchStrategy MatchStrategy {
        get {
            if (AlwaysApply) return RuleMatchStrategy.Always;
            if (!string.IsNullOrEmpty(Globs)) return RuleMatchStrategy.Glob;
            if (!string.IsNullOrEmpty(Description)) return RuleMatchStrategy.Description;
            return RuleMatchStrategy.Manual;
        }
    }
}

/// <summary>规则匹配策略枚举。</summary>
public enum RuleMatchStrategy {
    [EnumValue("always")] Always,
    [EnumValue("glob")] Glob,
    [EnumValue("description")] Description,
    [EnumValue("manual")] Manual
}
