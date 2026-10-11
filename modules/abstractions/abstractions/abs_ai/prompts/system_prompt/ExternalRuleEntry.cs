
namespace JoinCode.Abstractions.Prompts;

public sealed record ExternalRuleEntry {
    /// <summary>获取规则名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取规则内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取来源路径。</summary>
    public string SourcePath { get; init; } = string.Empty;
    /// <summary>获取是否始终应用。</summary>
    public bool AlwaysApply { get; init; }
    /// <summary>获取 glob 匹配模式。</summary>
    public string Globs { get; init; } = string.Empty;
    /// <summary>获取规则描述。</summary>
    public string Description { get; init; } = string.Empty;
}